using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Odin.Services.LinkMetaExtractor;


public class LinkMetaExtractor(IDynamicHttpClientFactory clientFactory, ILogger<LinkMetaExtractor> logger) : ILinkMetaExtractor
{
    private const long maxContentLength = 3 * 1024 * 1024;

    /// <summary>
    /// List of sites that needs bot headers to be fetched CSR website
    /// </summary>
    private static readonly List<string> SiteThatNeedsOEmbed = ["x.com", "twitter.com"];
    private static readonly List<string> SiteThatNeedsMozillaHeaders = [];

    private class OEmbed
    {
        public IDynamicHttpClientFactory clientFactory = null;
        public ILogger<LinkMetaExtractor> logger = null;

        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("author_name")]
        public string AuthorName { get; set; }

        [JsonPropertyName("author_url")]
        public string AuthorUrl { get; set; }

        [JsonPropertyName("html")]
        public string Html { get; set; }

        [JsonPropertyName("width")]
        public int? Width { get; set; }

        [JsonPropertyName("height")]
        public int? Height { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("cache_age")]
        public string CacheAge { get; set; }

        [JsonPropertyName("provider_name")]
        public string ProviderName { get; set; }

        [JsonPropertyName("provider_url")]
        public string ProviderUrl { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        public async Task<string> ToHtml()
        {
            var description = ExtractPostText();
            var tweetId = ExtractTweetId();
            var imageUrl = await GetTwitterPostImageAsync(tweetId);

            var syntheticHtml = $@"
<html>
<head>
<meta property=""og:site_name"" content=""X"" />
<meta property=""og:title"" content=""{WebUtility.HtmlEncode(AuthorName ?? Title ?? "Post")} on X"" />
<meta property=""og:description"" content=""{WebUtility.HtmlEncode(description)}"" />
{(string.IsNullOrEmpty(imageUrl) ? "" : $"<meta property=\"og:image\" content=\"{WebUtility.HtmlEncode(imageUrl)}\" />")}
<meta property=""og:url"" content=""{WebUtility.HtmlEncode(Url)}"" />
<meta property=""og:type"" content=""article"" />
<meta name=""twitter:card"" content=""{(string.IsNullOrEmpty(imageUrl) ? "summary" : "summary_large_image")}"" />
</head>
</html>";
            return syntheticHtml;
        }

        private string ExtractTweetId()
        {
            if (string.IsNullOrEmpty(Html)) return "";
            // Match the href containing the status URL, capturing the numeric ID
            var match = Regex.Match(Html, @"<a\s+[^>]*href=""https?://(?:twitter\.com|x\.com)/(?:[^/]+/)?(?:i/web/)?status/(\d+)(?:\?[^""]*)?""", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!match.Success) return "";
            return match.Groups[1].Value;
        }


        private string ExtractPostText()
        {
            if (string.IsNullOrEmpty(Html)) return "";

            var match = Regex.Match(Html, @"<p[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!match.Success) return "";

            var inner = match.Groups[1].Value;
            // Strip HTML tags
            inner = Regex.Replace(inner, @"<[^>]+>", "");
            // Remove pic.twitter.com links if present
            inner = Regex.Replace(inner, @"pic\.twitter\.com\/\w+", "", RegexOptions.IgnoreCase).Trim();
            // Decode HTML entities
            return WebUtility.HtmlDecode(inner.Trim());
        }


        private async Task<string> GetTwitterPostImageAsync(string tweetId)
        {
            if (string.IsNullOrEmpty(tweetId)) return null;
            try
            {
                // Compute token for the syndication API
                string token = ComputeToken(tweetId);
                var url = $"https://cdn.syndication.twimg.com/tweet-result?id={tweetId}&lang=en&token={token}";
                // Alternatively: var url = $"https://react-tweet.vercel.app/api/tweet/{tweetId}";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Accept", "application/json");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/109.0.0.0 Safari/537.36");
                var content = await LinkHttpRequestHelper.HttpRequestStringAsync(request, clientFactory, logger, maxContentLength);
                if (string.IsNullOrEmpty(content)) return "";

                // Deserialize the JSON
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("mediaDetails", out var mediaDetails) &&
                mediaDetails.ValueKind == JsonValueKind.Array &&
                mediaDetails.GetArrayLength() > 0)
                {
                    var firstMedia = mediaDetails[0];
                    if (firstMedia.TryGetProperty("media_url_https", out var urlProp) &&
                    urlProp.ValueKind == JsonValueKind.String)
                    {
                        // It's jsut a preview, we want it small
                        // If Thumb turns out to be too small, we can upgrade it to "small"
                        return $"{urlProp.GetString()}:small";
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                logger.LogDebug("OEmbed: Error resolving image from ID {MediaId}. Error: {Error}", tweetId, ex.Message);
                return "";
            }
        }

        private string ComputeToken(string tweetId)
        {
            if (!double.TryParse(tweetId, out double id)) return "";
            double val = (id / 1_000_000_000_000_000.0) * Math.PI;
            string base36 = ToBase36(val);
            return base36.Replace("0", "").Replace(".", "");
        }
        private string ToBase36(double value)
        {
            if (value == 0) return "0";
            bool isNegative = value < 0;
            if (isNegative) value = -value;
            long intPart = (long)Math.Floor(value);
            double fracPart = value - intPart;
            var result = new System.Text.StringBuilder();
            if (intPart == 0)
            {
                result.Append('0');
            }
            else
            {
                while (intPart > 0)
                {
                    long remainder = intPart % 36;
                    result.Insert(0, CharForDigit(remainder));
                    intPart /= 36;
                }
            }
            result.Append('.');
            const int maxPrecision = 12; // Sufficient precision
            for (int i = 0; i < maxPrecision; i++)
            {
                fracPart *= 36;
                long digit = (long)Math.Floor(fracPart);
                result.Append(CharForDigit(digit));
                fracPart -= digit;
                if (fracPart == 0) break;
            }
            string str = result.ToString();
            if (isNegative) str = "-" + str;
            return str;
        }
        private char CharForDigit(long digit)
        {
            if (digit >= 0 && digit <= 9) return (char)('0' + digit);
            return (char)('a' + (digit - 10));
        }
    }

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
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        bool isOEmbed = SiteThatNeedsOEmbed.Any(site => url.Contains(site, StringComparison.OrdinalIgnoreCase));

        // Use specific User-Agent based on the URL
        if (isOEmbed)
        {
            var oEmbedUrl = $"https://publish.x.com/oembed?url={Uri.EscapeDataString(url.Replace("x.com", "twitter.com"))}";
            request.RequestUri = new Uri(oEmbedUrl);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/109.0.0.0 Safari/537.36");
        }
        else if (SiteThatNeedsMozillaHeaders.Any(site => url.Contains(site, StringComparison.OrdinalIgnoreCase)))
        {
            // Use a modern browser-like User-Agent for general requests
            request.Headers.Add("Accept", "text/html");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/109.0.0.0 Safari/537.36");
        }
        else
        {
            // Use facebookexternalhit for Open Graph previews
            request.Headers.Add("Accept", "text/html");
            request.Headers.Add("User-Agent", "facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)");
        }

        try
        {
            var content = await LinkHttpRequestHelper.HttpRequestStringAsync(request, clientFactory, logger, maxContentLength);
            if (string.IsNullOrEmpty(content)) return null;

            if (isOEmbed)
            {
                try
                {
                    var oembed = JsonSerializer.Deserialize<OEmbed>(content);
                    oembed.logger = logger; // Not ideal...
                    oembed.clientFactory = clientFactory;
                    var syntheticHtml = await oembed.ToHtml();
                    return syntheticHtml;
                }
                catch (Exception ex)
                {
                    logger.LogDebug("LinkExtractor: Failed to process oEmbed for {Url}. Error: {Error}", url, ex.Message);
                    return null;
                }
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
            using var request = new HttpRequestMessage(HttpMethod.Get, cleanedUrl);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/109.0.0.0 Safari/537.36");

            var imageUri = await LinkHttpRequestHelper.HttpRequestImageAsync(request, clientFactory, logger, maxImageSize, originalUrl);
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
