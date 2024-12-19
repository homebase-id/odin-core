#nullable enable
using Odin.Services.DataSubscription.SendingHost;
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
            Title = MaxString(GetTitle(meta), 160) ?? string.Empty,
            Description = MaxString(GetDescription(meta), 400),
            ImageUrl = GetImageUrl(meta),
            ImageWidth = meta.ContainsKey("og:image:width") ? int.Parse(meta["og:image:width"].ToString()!) : null,
            ImageHeight = meta.ContainsKey("og:image:height") ? int.Parse(meta["og:image:height"].ToString()!) : null,
            Url = url,
            Type = meta.ContainsKey("og:type") ? meta["og:type"].ToString() : null
        };
    }

    private static string? MaxString(string? s, int maxLength)
    {
        if (s != null && s.Length > maxLength)
            s = s.Substring(0, maxLength - 3) + "...";

        return s;
    }

    private static string? GetTitle(Dictionary<string, object> meta)
    {
        foreach (var key in new[] { "title", "og:title", "twitter:title" })
        {
            if (meta.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value?.ToString()))
            {
                return value.ToString();
            }
        }

        return null;
    }


    public static string? GetDescription(Dictionary<string, object> meta)
    {
        var keys = new[] { "description", "og:description", "twitter:description" };

        foreach (var key in keys)
        {
            if (meta.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value?.ToString()))
            {
                return value.ToString();
            }
        }

        return null;
    }


    private static string? GetImageUrl(Dictionary<string, object> meta)
    {
        var candidates = new[] { "og:image", "twitter:image" };

        foreach (var key in candidates)
        {
            if (meta.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value?.ToString()))
            {
                var url = value.ToString();
                if (LinkMetaExtractor.IsUrlSafe(url))
                    return url;
            }
        }

        return null;
    }
}