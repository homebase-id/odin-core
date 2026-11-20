
using System;

namespace Odin.Services.PublicPage;

public static class SsrUrlHelper
{
    public static string ToSsrUrl(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return "/" + LinkPreviewDefaults.SsrPath;

        var trimmed = relativePath.Trim();

        // Ensure it starts with a slash
        if (!trimmed.StartsWith("/"))
            trimmed = "/" + trimmed;

        var prefix = "/" + LinkPreviewDefaults.SsrPath;

        // Already under /ssr
        if (trimmed.StartsWith(prefix + "/") || trimmed.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            return trimmed;

        return prefix + trimmed;
    }

    /// <summary>The path without a leading /ssr segment: /ssr/posts/blog/ssr-tips is /posts/blog/ssr-tips</summary>
    public static string FromSsrPath(string path)
    {
        var prefix = "/" + LinkPreviewDefaults.SsrPath;
        if (string.IsNullOrEmpty(path) || path.Equals(prefix, StringComparison.OrdinalIgnoreCase)) return "/";
        return path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase) ? path[prefix.Length..] : path;
    }
}