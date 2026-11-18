#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Odin.Services.PublicPage.Posts;

/// <summary>Maps a payload key and the file it lives on (null: the post's own file) to a URL, or null when unknown</summary>
public delegate string? PayloadUrlResolver(string key, string? fileId);

/// <summary>Encoding, URL and JSON reading shared by the rich-text renderers</summary>
internal static class HtmlSafe
{
    private static readonly Regex DomainRegex = new(
        @"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Text marks and their tags, innermost first, so the tags nest the same whatever order the marks are in</summary>
    private static readonly (string Mark, string Tag)[] MarkTags =
    [
        ("kbd", "kbd"), ("code", "code"), ("subscript", "sub"), ("superscript", "sup"),
        ("bold", "strong"), ("italic", "em"), ("underline", "u"), ("strikethrough", "s"), ("strike", "s")
    ];

    internal static string WrapMarks(string html, Func<string, bool> hasMark)
    {
        foreach (var (mark, tag) in MarkTags)
        {
            if (hasMark(mark)) html = $"<{tag}>{html}</{tag}>";
        }

        return html;
    }

    /// <summary>The parsed JSON, or null when it is missing or malformed</summary>
    internal static JsonNode? ParseJson(string? json)
    {
        try
        {
            return string.IsNullOrEmpty(json) ? null : JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string Enc(string? text) => WebUtility.HtmlEncode(text ?? "");

    /// <param name="url">The URL as written</param>
    /// <param name="allowMailto">Whether mailto: is allowed besides http and https</param>
    /// <param name="allowRelative">Whether an in-page #anchor or a path on this site is allowed</param>
    internal static string? SafeUrl(string? url, bool allowMailto = false, bool allowRelative = false)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();
        if (allowRelative && IsRelative(url)) return url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) return url;
        return allowMailto && uri.Scheme == Uri.UriSchemeMailto ? url : null;
    }

    /// <summary>
    /// No scheme before the first '/', '?' or '#', and not //host or \\host, which browsers read as another site;
    /// a control character could hide either, so none is allowed
    /// </summary>
    private static bool IsRelative(string url)
    {
        if (url.Any(char.IsControl)) return false;
        var u = url.Replace('\\', '/');
        if (u.StartsWith("//", StringComparison.Ordinal)) return false;
        var colon = u.IndexOf(':');
        var end = u.IndexOfAny(['/', '?', '#']);
        return colon < 0 || (end >= 0 && end < colon);
    }

    internal static string Mention(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        value = value.Trim();
        return DomainRegex.IsMatch(value)
            ? $"<a href=\"https://{Enc(value)}\">@{Enc(value)}</a>"
            : $"@{Enc(value)}";
    }

    /// <summary>The text of a node and its descendants under <paramref name="childrenKey"/>, unencoded</summary>
    internal static string PlainText(JsonNode? node, string childrenKey)
    {
        if (node is not JsonObject obj) return "";
        if (Str(obj["type"]) == "hardBreak") return "\n";
        if (Str(obj["text"]) is { } text) return text;
        if (obj[childrenKey] is not JsonArray children) return "";
        var sb = new StringBuilder();
        foreach (var child in children) sb.Append(PlainText(child, childrenKey));
        return sb.ToString();
    }

    internal static bool Bool(JsonNode? node) => node is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    /// <summary>Null for a string that is not valid UTF-16 (a lone surrogate, e.g. an emoji cut in half),
    /// so one bad string drops itself and not the whole document</summary>
    internal static string? Str(JsonNode? node)
    {
        try
        {
            return node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
