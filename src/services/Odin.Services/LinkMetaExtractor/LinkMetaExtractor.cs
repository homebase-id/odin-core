using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Odin.Services.LinkMetaExtractor;

public class LinkMetaExtractor
{
    private readonly HttpClient _client;

    public LinkMetaExtractor()
    {
        _client = new HttpClient();
        _client.DefaultRequestHeaders.Add("Accept", "text/html");
        _client.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<LinkMeta> ExtractAsync(string url)
    {
        
        var response = await _client.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();

        var meta = Parser.Parse(content);
        
        return new LinkMeta
        {
            Title = meta.ContainsKey("og:title") ? meta["og:title"].ToString() : meta.ContainsKey("twitter:title") ? meta["twitter:title"].ToString() : null,
            Description = meta.ContainsKey("og:description") ? meta["og:description"].ToString() : meta.ContainsKey("twitter:description") ? meta["twitter:description"].ToString() : null,
            ImageUrl = meta.ContainsKey("og:image") ? meta["og:image"].ToString() : meta.ContainsKey("twitter:image") ? meta["twitter:image"].ToString() : null,
            Url = url
        };
    }
    
}