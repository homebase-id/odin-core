using System;
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
        client.DefaultRequestHeaders.Add("User-Agent", "Chrome/114.0.5735.134");
        var response = await client.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();

        var meta = Parser.Parse(content);
        if(meta.Count == 0)
            throw new Exception("No meta tags found");
        return LinkMeta.fromMetaData(meta, url);
    }
    
}