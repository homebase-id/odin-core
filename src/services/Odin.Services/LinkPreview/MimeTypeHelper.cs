using System;
using System.Collections.Generic;

namespace Odin.Services.LinkPreview;

// via grok
public static class MimeTypeHelper
{
    // Dictionary mapping MIME type subtypes to file extensions
    public static readonly Dictionary<string, string> SubtypeToExtension = new(StringComparer.InvariantCultureIgnoreCase)
    {
        // JPEG formats
        { "jpeg", ".jpg" },
        { "pjpeg", ".jpg" }, // progressive JPEG

        // PNG and GIF formats
        { "png", ".png" },
        { "gif", ".gif" },

        // Bitmap formats
        { "bmp", ".bmp" },

        // TIFF formats
        { "tiff", ".tiff" },
        { "tif", ".tiff" },

        // SVG (Scalable Vector Graphics)
        { "svg+xml", ".svg" },

        // WebP format
        { "webp", ".webp" },

        // High Efficiency Image File Formats
        { "heif", ".heif" },
        { "heic", ".heic" },

        // AVIF format
        { "avif", ".avif" },

        // JPEG 2000 formats
        { "jp2", ".jp2" },
        { "jpx", ".jp2" }, // alternative subtype for JPEG 2000

        // JPEG XR format
        { "jxr", ".jxr" },

        // Icon formats
        { "vnd.microsoft.icon", ".ico" },
        { "x-icon", ".ico" }
    };

    /// <summary>
    /// Extracts the graphics file extension from a MIME type.
    /// </summary>
    /// <param name="mimeType">The MIME type (e.g., "image/jpeg").</param>
    /// <returns>The file extension (e.g., ".jpg"), or null if invalid or unsupported.</returns>
    public static string GetFileExtensionFromMimeType(string mimeType)
    {
        // Handle null or empty input
        if (string.IsNullOrWhiteSpace(mimeType))
        {
            return null;
        }

        // Split by ';' to ignore parameters (e.g., "image/jpeg; charset=utf-8" -> "image/jpeg")
        string[] parts = mimeType.Split(';');
        string typePart = parts[0].Trim();

        // Split by '/' to separate type and subtype (e.g., "image/jpeg" -> ["image", "jpeg"])
        string[] typeSubtype = typePart.Split('/');
        if (typeSubtype.Length != 2)
        {
            return null; // Invalid MIME type (no subtype)
        }

        // Extract subtype and normalize it
        string subtype = typeSubtype[1].Trim().ToLower();

        // Look up the extension in the dictionary
        return SubtypeToExtension.GetValueOrDefault(subtype, null);

        // Return null if the subtype isn't recognized
    }
}