#nullable enable
using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Odin.Services.PublicPage.Posts;

/// <summary>
/// Renders a Plate/Slate JSON body to HTML for server-side rendered pages.
/// Never throws on unexpected shapes: unknown nodes render their children or text.
/// </summary>
public static class PlateRichTextParser
{
    private static readonly Regex DomainRegex = new(
        @"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <param name="json">The body JSON (an array of nodes; a single node object is tolerated)</param>
    /// <param name="payloadUrl">
    /// Maps a payload key (local_image fileKey) and the file it lives on (local_image fileId; null means the post's own file)
    /// to a URL for that payload, or null when unknown
    /// </param>
    public static string? Parse(string json, Func<string, string?, string?>? payloadUrl = null)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (root == null) return null;

        var sb = new StringBuilder();
        if (root is JsonArray array)
        {
            foreach (var node in array)
            {
                sb.Append(ParseNode(node, payloadUrl));
            }
        }
        else
        {
            sb.Append(ParseNode(root, payloadUrl));
        }

        return sb.ToString();
    }

    private static string ParseNode(JsonNode? node, Func<string, string?, string?>? payloadUrl)
    {
        if (node is not JsonObject obj) return "";

        var type = Str(obj["type"])?.ToLowerInvariant() ?? "";
        if (type == "" && obj.ContainsKey("text")) return ParseLeaf(obj);

        string Kids() => ParseChildren(obj, payloadUrl);

        switch (type)
        {
            case "p":
            case "paragraph":
                return $"<p>{Kids()}</p>\n";
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                return $"<{type}>{Kids()}</{type}>\n";
            case "blockquote":
                return $"<blockquote>{Kids()}</blockquote>\n";
            case "ul" or "ol":
                return $"<{type}>{Kids()}</{type}>\n";
            case "li":
                return $"<li>{Kids()}</li>\n";
            case "lic":
                return Kids();
            case "div":
                return $"<div>{Kids()}</div>\n";
            case "figure":
                return $"<figure>{Kids()}</figure>\n";
            case "figcaption":
                return $"<figcaption>{Kids()}</figcaption>\n";
            case "strong":
                return $"<strong>{Kids()}</strong>";
            case "em":
                return $"<em>{Kids()}</em>";
            case "code":
                return $"<code>{Kids()}</code>";
            case "code_block":
                return $"<pre><code>{CodeLines(obj)}</code></pre>\n";
            case "a":
                return Anchor(Str(obj["url"]), Kids());
            case "link":
                return Anchor(Str(obj["linkTarget"]), HtmlText(Str(obj["linkText"])));
            case "mention":
                return Mention(Str(obj["value"]));
            case "image":
            {
                var src = SafeUrl(Str(obj["url"]));
                return src == null ? "" : $"<img src=\"{HtmlText(src)}\" width=\"620\" height=\"350\" />\n";
            }
            case "local_image":
            {
                var key = Str(obj["fileKey"]);
                // an image copied from another post keeps pointing at that post's file
                var src = key == null ? null : payloadUrl?.Invoke(key, Str(obj["fileId"]));
                // No payload URL available: an empty figure keeps the layout without a broken image
                return src == null ? "<figure></figure>\n" : $"<img src=\"{HtmlText(src)}\" alt=\"\" />\n";
            }
            default:
                return obj["children"] is JsonArray ? Kids() : HtmlText(Str(obj["text"]));
        }
    }

    private static string ParseChildren(JsonObject obj, Func<string, string?, string?>? payloadUrl)
    {
        if (obj["children"] is not JsonArray children)
        {
            // e.g. an 'a' node that carries its text directly
            return HtmlText(Str(obj["text"]));
        }

        var sb = new StringBuilder();
        foreach (var child in children)
        {
            sb.Append(ParseNode(child, payloadUrl));
        }

        return sb.ToString();
    }

    private static string ParseLeaf(JsonObject leaf)
    {
        var html = HtmlText(Str(leaf["text"])).Replace("\n", "<br/>");

        // innermost first so the tags always nest correctly
        if (Flag(leaf, "kbd")) html = $"<kbd>{html}</kbd>";
        if (Flag(leaf, "code")) html = $"<code>{html}</code>";
        if (Flag(leaf, "bold")) html = $"<strong>{html}</strong>";
        if (Flag(leaf, "italic")) html = $"<em>{html}</em>";
        if (Flag(leaf, "underline")) html = $"<u>{html}</u>";
        if (Flag(leaf, "strikethrough")) html = $"<s>{html}</s>";
        return html;
    }

    private static string CodeLines(JsonObject block)
    {
        if (block["children"] is not JsonArray lines) return HtmlText(Str(block["text"]));

        var sb = new StringBuilder();
        var first = true;
        foreach (var line in lines)
        {
            if (line is not JsonObject lineObj) continue;
            if (!first) sb.Append('\n');
            first = false;
            sb.Append(CodeText(lineObj));
        }

        return sb.ToString();
    }

    private static string CodeText(JsonObject node)
    {
        if (node["children"] is not JsonArray children) return HtmlText(Str(node["text"]));

        var sb = new StringBuilder();
        foreach (var child in children)
        {
            if (child is JsonObject c) sb.Append(CodeText(c));
        }

        return sb.ToString();
    }

    private static string Anchor(string? url, string inner)
    {
        var href = SafeUrl(url, allowMailto: true);
        return href == null ? inner : $"<a href=\"{HtmlText(href)}\">{inner}</a>";
    }

    private static string Mention(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        value = value.Trim();
        return DomainRegex.IsMatch(value)
            ? $"<a href=\"https://{HtmlText(value)}\">@{HtmlText(value)}</a>"
            : $"@{HtmlText(value)}";
    }

    private static string? SafeUrl(string? url, bool allowMailto = false)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) return url;
        return allowMailto && uri.Scheme == Uri.UriSchemeMailto ? url : null;
    }

    private static bool Flag(JsonObject obj, string name) =>
        obj[name] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static string HtmlText(string? text) => WebUtility.HtmlEncode(text ?? "");
}
