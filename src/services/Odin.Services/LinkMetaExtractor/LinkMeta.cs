#nullable enable
using System;
using System.Collections.Generic;

namespace Odin.Services.LinkMetaExtractor;


public class LinkMeta
{
    
    public required string Title { get; set; }
    public string?  Description { get; set; }
    public string? ImageUrl { get; set; }
    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }
    public required string Url { get; set; }
    
    public string? Type { get; set; }
    
    public static LinkMeta FromMetaData(Dictionary<string, object> meta, string url)
    {
        return new LinkMeta
        {
            Title = GetTitle(meta),
            Description = GetDescription(meta),
            ImageUrl = GetImageUrl(meta),
            ImageWidth = meta.ContainsKey("og:image:width") ? int.Parse(meta["og:image:width"].ToString()!) : null,
            ImageHeight = meta.ContainsKey("og:image:height") ? int.Parse(meta["og:image:height"].ToString()!) : null,
            Url = url,
            Type = meta.ContainsKey("og:type") ? meta["og:type"].ToString() : null
        };
    }

    private static string GetTitle(Dictionary<string, object> meta)
    {
         var exception = new Exception("Title not found");
        if (meta.TryGetValue("title", out var value))
        {
            return value.ToString() ?? throw exception;
        }
        else if (meta.TryGetValue("og:title", out var value1))
        {
            return value1.ToString() ?? throw exception;
        }
        else if (meta.TryGetValue("twitter:title", out var value2))
        {
            return value2.ToString() ?? throw exception;
        }
        throw exception;
    }
    

    private static string? GetDescription(Dictionary<string, object> meta)
    {

        if (meta.TryGetValue("description", out var value))
        {
            return value.ToString();
        }
        else if (meta.TryGetValue("og:description", out var value1))
        {
            return value1.ToString();
        }
        else if (meta.TryGetValue("twitter:description", out var value2))
        {
            return value2.ToString();
        }

        return null;
    }
    
    private static string? GetImageUrl(Dictionary<string, object> meta)
    {
        if (meta.TryGetValue("og:image", out var value))
        {
            return value.ToString();
        }
        else if (meta.TryGetValue("twitter:image", out var value1))
        {
            return value1.ToString();
        }

        return null;
    }
    
  
    
}