using System.Collections.Generic;

namespace Odin.Services.LinkMetaExtractor;

public class LinkMeta
{
    
    public string Title { get; init; }
    public string  Description { get; init; }
    public string ImageUrl { get; set; }
    public int ? ImageWidth { get; set; }
    public int ? ImageHeight { get; set; }
    public string Url { get; set; }
    
    public string Type { get; set; }

    public static string GetTitle(Dictionary<string, object> meta)
    {
        if (meta.TryGetValue("title", out object value))
        {
            return value.ToString();
        }
        else if (meta.TryGetValue("og:title", out var value1))
        {
            return value1.ToString();
        }
        else if (meta.TryGetValue("twitter:title", out var value2))
        {
            return value2.ToString();
        }
        throw new System.Exception("Title not found");
    }

    public static string GetDescription(Dictionary<string, object> meta)
    {

        if (meta.TryGetValue("description", out object value))
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
    
    public static string GetImageUrl(Dictionary<string, object> meta)
    {
        if (meta.TryGetValue("og:image", out object value))
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