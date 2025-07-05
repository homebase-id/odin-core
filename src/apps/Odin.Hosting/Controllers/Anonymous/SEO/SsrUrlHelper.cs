using System;
using Odin.Services.LinkPreview;

namespace Odin.Hosting.Controllers.Anonymous.SEO;

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
}