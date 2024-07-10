using System;
using System.Net;
using System.Threading.Tasks;
using HttpClientFactoryLite;

namespace Odin.Services.LinkMetaExtractor;


public class LinkMetaExtractor(IHttpClientFactory clientFactory) : ILinkMetaExtractor
{
    public async Task<LinkMeta> ExtractAsync(string url)
    {
        var client = clientFactory.CreateClient<LinkMetaExtractor>();
        // These Headers are needed for request to be received as text/html
        // Some sites like Instagram does not return the meta data if no user agent specified
        client.DefaultRequestHeaders.Add("Accept", "text/html");
        client.DefaultRequestHeaders.Add("User-Agent", "googlebot|bingbot|msnbot|yahoo|Baidu|aolbuild|facebookexternalhit|iaskspider|DuckDuckBot|Applebot|Almaden|iarchive|archive.org_bot");
        var response = await client.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();
        
        // Decode the html content. May contain & as &amp; which breaks the url
        var decodedHtml = WebUtility.HtmlDecode(content);
        var meta = Parser.Parse(decodedHtml);
        if (meta.Count == 0)
            return null;

        var linkMeta =  LinkMeta.FromMetaData(meta, url);
        if (linkMeta.ImageUrl != null)
        {
            // Download the image and convert it into base64
            var image = await client.GetByteArrayAsync(linkMeta.ImageUrl);
            linkMeta.ImageUrl = Convert.ToBase64String(image);
        }

        return linkMeta;
    }
    
}