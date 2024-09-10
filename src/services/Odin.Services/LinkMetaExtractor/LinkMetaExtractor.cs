using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using HttpClientFactoryLite;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using IHttpClientFactory = HttpClientFactoryLite.IHttpClientFactory;

namespace Odin.Services.LinkMetaExtractor;


public class LinkMetaExtractor(IHttpClientFactory clientFactory,ILogger<LinkMetaExtractor> logger) : ILinkMetaExtractor
{
    /// <summary>
    /// List of sites that needs bot headers to be fetched CSR website
    /// </summary>
    private static readonly List<string> SiteThatNeedsBotHeaders = ["twitter.com","x.com"];
    
    private static bool IsValidUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        Uri uriResult;
        var result = Uri.TryCreate(url, UriKind.Absolute, out uriResult) 
                      && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        
        return result;
    }

    public async Task<LinkMeta> ExtractAsync(string url)
    {
        if (!IsValidUrl(url))
        {
            logger.LogError("Invalid Url {Url}", url);
            throw new OdinClientException("Invalid Url");
        }
        
        var client = clientFactory.CreateClient<LinkMetaExtractor>();
        // These Headers are needed for request to be received as text/html
        // Some sites like Instagram does not return the meta data if no user agent specified
        client.DefaultRequestHeaders.Add("Accept", "text/html");
        if (SiteThatNeedsBotHeaders.Any(url.Contains))
        {
         client.DefaultRequestHeaders.Add("User-Agent", "grapeshot|googlebot|bingbot|msnbot|yahoo|Baidu|aolbuild|facebookexternalhit|iaskspider|DuckDuckBot|Applebot|Almaden|iarchive|archive.org_bot");
        }
        else
        {
            client.DefaultRequestHeaders.Add("User-Agent", "*");
        }
        if (string.IsNullOrEmpty(url))
        {
            throw new OdinClientException("Url cannot be empty");
        }
        try
        {
            var response = await client.GetAsync(url);
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                logger.LogWarning("Forbidden to fetch information from {Url}. Status code: {StatusCode}", url, response.StatusCode);
                return null;
            }
            var content = await response.Content.ReadAsStringAsync();

            // Decode the html content. May contain & as &amp; which breaks the url
            var decodedHtml = WebUtility.HtmlDecode(content);
            var meta = Parser.Parse(decodedHtml);
            if (meta.Count == 0)
                return null;

            var linkMeta = LinkMeta.FromMetaData(meta, url);
            if (!string.IsNullOrEmpty(linkMeta.ImageUrl))
            {
                var cleanedUrl = WebUtility.HtmlDecode(linkMeta.ImageUrl);
                // Download the image and convert it into uri data
                var imageResponse = await client.GetAsync(cleanedUrl);
                if (!imageResponse.IsSuccessStatusCode)
                {
                    logger.LogInformation("Failed to download image from {Url}. Status code: {ImageResponse}",
                        linkMeta.ImageUrl, imageResponse.StatusCode);
                    linkMeta.ImageUrl = null;
                }
                else
                {
                    var image = await imageResponse.Content.ReadAsByteArrayAsync();
                    var mimeType = imageResponse.Content.Headers.ContentType?.ToString();
                    if (string.IsNullOrEmpty(mimeType))
                        mimeType = "image/png"; // Force default the type to png

                    var imageUri = $"data:{mimeType};base64,{Convert.ToBase64String(image)}";
                    linkMeta.ImageUrl = imageUri;

                }

            }

            return linkMeta;

        }
        catch (OperationCanceledException)
        {
            // Operation was cancelled so ignore
            return null;
        }
        catch (HttpRequestException e)
        {
            logger.LogInformation("Something went wrong fetching information from {Url}. Error: {Error} StatusCode: {Status}", url, e.Message, e.StatusCode);
            throw new OdinClientException("Failed to fetch information from the url");
        }
       
    }
    
}