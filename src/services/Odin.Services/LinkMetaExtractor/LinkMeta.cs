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
    const int MaxDataUriSize = 2 * 1024 * 1024; // 2 MB max image URI

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

    /// <summary>
    /// Extracts the image URL or embedded image data from metadata.
    /// The function prioritizes the keys "og:image" and "twitter:image" to locate the image.
    /// If a valid URL is found, it is returned. If the value contains embedded image data
    /// (e.g., a "data:image" URI), it validates the MIME type and limits the size.
    /// Returns null if no valid image URL or embedded data is found.
    /// </summary>
    /// <param name="meta">A dictionary of metadata keys and values.</param>
    /// <returns>A string containing the image URL or sanitized embedded image data, or null if none is found.</returns>
    public static string? GetImageUrl(Dictionary<string, object> meta)
    {
        var candidates = new[] { "og:image", "twitter:image" };

        foreach (var key in candidates)
        {
            if (meta.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value?.ToString()))
            {
                var imageData = value.ToString();

                if (imageData == null)
                    continue;

                // Find an embedded image
                if (IsValidEmbeddedImage(imageData))
                    return imageData;

                // Otherwise, validate it as a safe URL
                if (LinkMetaExtractor.IsUrlSafe(imageData))
                    return imageData;
            }
        }

        return null;
    }

    /// <summary>
    /// Validates whether the given data URI is a valid embedded image.
    /// Checks the MIME type and ensures the size is within limits.
    /// </summary>
    /// <param name="dataUri">The data URI to validate.</param>
    /// <param name="maxSize">The maximum allowed size of the embedded image.</param>
    /// <returns>True if the data URI is a valid image, otherwise false.</returns>
    public static bool IsValidEmbeddedImage(string dataUri)
    {
        if (!dataUri.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
            return false;

        var validMimeTypes = new[] { "image/png", "image/jpeg", "image/gif" };

        try
        {
            // Extract MIME type
            var mimeTypeEnd = dataUri.IndexOf(';');
            if (mimeTypeEnd < 0) return false;

            var mimeType = dataUri.Substring(5, mimeTypeEnd - 5); // Extract MIME type
            if (!Array.Exists(validMimeTypes, type => type.Equals(mimeType, StringComparison.OrdinalIgnoreCase)))
                return false;

            // Check size
            var base64Data = dataUri.Substring(dataUri.IndexOf(",") + 1);
            if (base64Data.Length > MaxDataUriSize)
                return false;

            return true;
        }
        catch
        {
            // Handle malformed data URIs
            return false;
        }
    }
}
