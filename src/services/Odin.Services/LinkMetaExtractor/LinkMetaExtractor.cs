using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Http;

namespace Odin.Services.LinkMetaExtractor;


public class LinkMetaExtractor(IDynamicHttpClientFactory clientFactory, ILogger<LinkMetaExtractor> logger) : ILinkMetaExtractor
{
    /// <summary>
    /// List of sites that needs bot headers to be fetched CSR website
    /// </summary>
    private static readonly List<string> SiteThatNeedsBotHeaders = ["x.com", "twitter.com"];
    private static readonly List<string> SiteThatNeedsMozillaHeaders = [];

    public static bool IsUrlSafe(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uriResult))
            return false;
        if (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps)
            return false;
        // Check if the host is an IP address
        if (IPAddress.TryParse(uriResult.Host, out IPAddress ipAddress))
        {
            // Check if the IP address is in a private or loopback range
            if (IsPrivateIp(ipAddress))
                return false;
        }
        else
        {
            // Resolve the DNS to get IP addresses
            try
            {
                var hostAddresses = System.Net.Dns.GetHostAddresses(uriResult.Host);
                if (hostAddresses.Any(IsPrivateIp))
                {
                    return false;
                }
            }
            catch
            {
                // DNS resolution failed, treat as unsafe
                return false;
            }
        }
        return true;
    }
    private static bool IsPrivateIp(IPAddress ipAddress)
    {
        switch (ipAddress.AddressFamily)
        {
            // IPv4 ranges
            case System.Net.Sockets.AddressFamily.InterNetwork:
            {
                var bytes = ipAddress.GetAddressBytes();
                switch (bytes[0])
                {
                    case 10:
                        return true; // Class A private network
                    case 172:
                        return bytes[1] >= 16 && bytes[1] <= 31; // Class B private network
                    case 192:
                        return bytes[1] == 168; // Class C private network
                    case 127:
                        return true; // Loopback
                }

                break;
            }
            // IPv6 ranges
            case System.Net.Sockets.AddressFamily.InterNetworkV6 when IPAddress.IsLoopback(ipAddress):
            case System.Net.Sockets.AddressFamily.InterNetworkV6 when ipAddress.IsIPv6LinkLocal || ipAddress.IsIPv6SiteLocal:
                return true;
        }

        return false;
    }


    public async Task<LinkMeta> ProcessHtmlAsync(string htmlContent, string url)
    {
        if (htmlContent == null)
            return null;

        var linkMeta = ProcessMetaData(htmlContent, url);
        if (linkMeta == null)
            return null;
        if (!string.IsNullOrEmpty(linkMeta.ImageUrl))
        {
            if (!LinkMeta.IsValidEmbeddedImage(linkMeta.ImageUrl))
            {
                var imageUrl = await ProcessImageAsync(linkMeta.ImageUrl, url);
                if (imageUrl != null)
                {
                    linkMeta.ImageUrl = imageUrl;
                }
                else
                {
                    linkMeta.ImageUrl = null;
                }
            }
        }
        return linkMeta;
    }


    public async Task<LinkMeta> ExtractAsync(string url)
    {
        if (!IsUrlSafe(url))
        {
            throw new OdinClientException($"Invalid or unsafe URL: {url}");
        }

        var htmlContent = await FetchHtmlContentAsync(url);

        return await ProcessHtmlAsync(htmlContent, url);
    }


    private async Task<string> FetchHtmlContentAsync(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept", "text/html");

        // Use specific User-Agent based on the URL
        if (SiteThatNeedsBotHeaders.Any(site => url.Contains(site, StringComparison.OrdinalIgnoreCase)))
        {
            request.Headers.Add("User-Agent", "grapeshot|googlebot|bingbot|msnbot|yahoo|Baidu|aolbuild|facebookexternalhit|iaskspider|DuckDuckBot|Applebot|Almaden|iarchive|archive.org_bot");
        }
        else if (SiteThatNeedsMozillaHeaders.Any(site => url.Contains(site, StringComparison.OrdinalIgnoreCase)))
        {
            // Use a modern browser-like User-Agent for general requests
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/109.0.0.0 Safari/537.36");
        }
        else
        {
            // Use facebookexternalhit for Open Graph previews
            request.Headers.Add("User-Agent", "facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)");
        }

        const long maxContentLength = 3 * 1024 * 1024;

        try
        {
            var response = await clientFactory.SendWithRedirectsAsync(
                request,
                timeout: TimeSpan.FromSeconds(20),
                httpCompletionOption: HttpCompletionOption.ResponseHeadersRead);

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                logger.LogDebug("LinkExtractor: Forbidden to fetch information from {Url}. Status code: {StatusCode}", url, response.StatusCode);
                return null;
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                logger.LogDebug("LinkExtractor: Not OK {Url}. Status code: {StatusCode}", url, response.StatusCode);
                return null;
            }

            // Check content length
            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > maxContentLength)
            {
                logger.LogDebug("LinkExtractor: Content length {ContentLength} exceeds maximum allowed size {MaxSize} for url {Url}",
                    contentLength.Value, maxContentLength, url);
                return null;
            }

            // Read the content with a limited buffer
            var content = await response.Content.ReadAsStringAsync();
            if (content.Length > maxContentLength)
            {
                logger.LogDebug("LinkExtractor: Content length {ContentLength} exceeds maximum allowed size {MaxSize} for url {Url}",
                    content.Length, maxContentLength, url);
                return null;
            }

            // Do NOT decode the HTML content. The chat client and stuff reliably handles unsafe "html".
            return content;
        }
        catch (OperationCanceledException)
        {
            // Operation was cancelled
            logger.LogDebug("LinkExtractor: Request to {Url} timed out", url);
            return null;
        }
        catch (HttpRequestException e)
        {
            logger.LogInformation("LinkExtractor: Error fetching information from {Url}. Error: {Error} StatusCode: {Status}", url,
                e.Message, e.StatusCode);
            throw new OdinClientException("LinkExtractor: Failed to fetch information from the URL");
        }
        catch (Exception e)
        {
            logger.LogInformation("LinkExtractor: Something went seriously wrong that you are here {Url}. Error: {Error}", url, e.Message);
            return null;
        }
    }


    private LinkMeta ProcessMetaData(string htmlContent, string url)
    {
        try
        {
            var meta = Parser.Parse(htmlContent);
            if (meta == null || meta.Count == 0)
                return null;

            var linkMeta = LinkMeta.FromMetaData(meta, url);

            if (string.IsNullOrEmpty(linkMeta.Title))
            {
                logger.LogDebug("LinkExtractor: The Title must be set (I'm not sure why) [{Url}]", url);
                return null;
            }

            return linkMeta;
        }
        catch (Exception e)
        {
            logger.LogDebug("LinkExtractor: Error processing metadata for {Url}. Error: {Error}", url, e.Message);
            return null;
        }
    }


    private async Task<string> ProcessImageAsync(string imageUrl, string originalUrl)
    {
        if (!IsUrlSafe(imageUrl))
        {
            logger.LogDebug("LinkExtractor: Unsafe image URL {ImageUrl} for original URL {OriginalUrl}", imageUrl, originalUrl);
            return null;
        }
        const long maxImageSize = 5 * 1024 * 1024;
        try
        {
            var cleanedUrl = WebUtility.HtmlDecode(imageUrl);
            var request = new HttpRequestMessage(HttpMethod.Get, cleanedUrl);
            request.Headers.Add("User-Agent", "*");

            var response = await clientFactory.SendWithRedirectsAsync(
                request,
                timeout: TimeSpan.FromSeconds(10),
                httpCompletionOption: HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("LinkExtractor: Something went wrong when downloading the image from {Url}. Status code: {StatusCode}", imageUrl, response.StatusCode);
                return null;
            }
            // Check content length
            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > maxImageSize)
            {
                logger.LogDebug("LinkExtractor: Image size {ContentLength} exceeds maximum allowed size {MaxSize} for url: {Url}", contentLength.Value, maxImageSize,imageUrl);
                return null;
            }
            var image = await response.Content.ReadAsByteArrayAsync();
            if (image.Length > maxImageSize)
            {
                logger.LogDebug("LinkExtractor: Image size {ContentLength} exceeds maximum allowed size {MaxSize} for url: {Url}", image.Length, maxImageSize,imageUrl);
                return null;
            }
            var mimeType = response.Content.Headers.ContentType?.ToString();
            if (string.IsNullOrEmpty(mimeType))
                mimeType = "image/png";
            var imageBase64 = Convert.ToBase64String(image);
            if (string.IsNullOrWhiteSpace(imageBase64))
            {
                logger.LogDebug("LinkExtractor: No image Data from imageUrl {Url}. Original Link URL {OriginalUrl}", imageUrl, originalUrl);
                return null;
            }
            var imageUri = $"data:{mimeType};base64,{imageBase64}";
            return imageUri;
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("LinkExtractor: Image download from {ImageUrl} timed out", imageUrl);
            return null;
        }
        catch (HttpRequestException e)
        {
            logger.LogDebug("LinkExtractor: Error downloading image from {ImageUrl}. Error: {Error} StatusCode: {Status}", imageUrl, e.Message, e.StatusCode);
            return null;
        }
    }
}
