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
        _client.DefaultRequestHeaders.Add("User-Agent", "Chrome/114.0.5735.134");
        _client.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<LinkMeta> ExtractAsync(string url)
    {
        var response = await _client.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();

        var meta = Parser.Parse(content);
        if(meta.Count == 0)
            throw new Exception("No meta tags found");
        return new LinkMeta
        {
            Title = LinkMeta.GetTitle(meta),
            Description = LinkMeta.GetDescription(meta),
            ImageUrl = LinkMeta.GetImageUrl(meta),
            ImageWidth = meta.ContainsKey("og:image:width") ? int.Parse(meta["og:image:width"].ToString()!) : null,
            ImageHeight = meta.ContainsKey("og:image:height") ? int.Parse(meta["og:image:height"].ToString()!) : null,
            Url = url,
            Type = meta.ContainsKey("og:type") ? meta["og:type"].ToString() : null
            
        };
    }
    
}