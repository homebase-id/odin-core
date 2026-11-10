#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Odin.Services.PublicPage.Posts;

/// <summary>
/// Renders a Tiptap JSON document (the shared rich-text schema) to semantic HTML for server-side rendered pages.
/// Never throws on unexpected shapes: unknown nodes render their children. Math is emitted as plain text (no KaTeX here).
/// </summary>
public static class TiptapRichTextParser
{
    private static readonly Regex DomainRegex = new(
        @"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <param name="json">The Tiptap doc JSON</param>
    /// <param name="payloadUrl">Maps an image fileKey to a URL for the post's payload, or null when unknown</param>
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

        return root is JsonObject ? new Renderer(payloadUrl).Render(root) : null;
    }

    private sealed class Renderer(Func<string, string?, string?>? payloadUrl)
    {
        private readonly HashSet<string> _usedSlugs = [];
        private readonly Dictionary<string, int> _footnoteNumbers = [];

        public string Render(JsonNode? node) => node is JsonObject obj ? RenderNode(obj) : "";

        private string Kids(JsonObject obj)
        {
            if (obj["content"] is not JsonArray children) return "";
            var sb = new StringBuilder();
            foreach (var child in children) sb.Append(Render(child));
            return sb.ToString();
        }

        private string RenderNode(JsonObject obj)
        {
            var attrs = obj["attrs"] as JsonObject;
            switch (Str(obj["type"]))
            {
                case "doc":
                    return Kids(obj);
                case "text":
                    return RenderText(obj);
                case "paragraph":
                    // images render as <figure>, which cannot live inside <p>
                    return HasImage(obj) ? Kids(obj) : $"<p>{Kids(obj)}</p>\n";
                case "heading":
                {
                    var level = Math.Clamp(Int(attrs?["level"]) ?? 1, 1, 6);
                    var inner = Kids(obj);
                    var id = Slug(PlainText(obj));
                    var idAttr = id == "" ? "" : $" id=\"{Enc(id)}\"";
                    return $"<h{level}{idAttr}>{inner}</h{level}>\n";
                }
                case "blockquote":
                    return $"<blockquote>{Kids(obj)}</blockquote>\n";
                case "codeBlock":
                {
                    var lang = Str(attrs?["language"]);
                    var cls = string.IsNullOrWhiteSpace(lang) ? "" : $" class=\"language-{Enc(lang)}\"";
                    return $"<pre><code{cls}>{Enc(PlainText(obj))}</code></pre>\n";
                }
                case "bulletList":
                    return $"<ul>{Kids(obj)}</ul>\n";
                case "orderedList":
                {
                    var start = Int(attrs?["start"]);
                    var startAttr = start is null or 1 ? "" : $" start=\"{start}\"";
                    return $"<ol{startAttr}>{Kids(obj)}</ol>\n";
                }
                case "listItem":
                    return $"<li>{Kids(obj)}</li>\n";
                case "taskList":
                    return $"<ul>{Kids(obj)}</ul>\n";
                case "taskItem":
                {
                    var box = Bool(attrs?["checked"]) ? "&#9745; " : "&#9744; ";
                    return $"<li>{box}{Kids(obj)}</li>\n";
                }
                case "table":
                    return $"<table><tbody>{Kids(obj)}</tbody></table>\n";
                case "tableRow":
                    return $"<tr>{Kids(obj)}</tr>\n";
                case "tableHeader":
                    return $"<th>{Kids(obj)}</th>";
                case "tableCell":
                    return $"<td>{Kids(obj)}</td>";
                case "horizontalRule":
                    return "<hr/>\n";
                case "hardBreak":
                    return "<br/>";
                case "image":
                    return RenderImage(attrs);
                case "gallery":
                    return $"<div class=\"gallery gallery-{Enc(Str(attrs?["layout"]) ?? "grid")}\">{Kids(obj)}</div>\n";
                case "callout":
                    return $"<aside class=\"callout callout-{Enc(Str(attrs?["variant"]) ?? "info")}\">{Kids(obj)}</aside>\n";
                case "toggle":
                    return $"<details><summary>{Enc(Str(attrs?["summary"]))}</summary>{Kids(obj)}</details>\n";
                case "linkPreview":
                {
                    var url = Str(attrs?["url"]);
                    var href = SafeUrl(url);
                    if (href == null) return "";
                    var title = Str(attrs?["title"]);
                    var label = string.IsNullOrWhiteSpace(title) ? href : title;
                    var desc = Str(attrs?["description"]);
                    var descHtml = string.IsNullOrWhiteSpace(desc) ? "" : $"<p>{Enc(desc)}</p>";
                    return $"<div class=\"link-preview\"><a href=\"{Enc(href)}\">{Enc(label)}</a>{descHtml}</div>\n";
                }
                case "footnoteReference":
                {
                    var id = Str(attrs?["id"]) ?? "";
                    return $"<sup><a href=\"#fn-{Enc(id)}\" id=\"fnref-{Enc(id)}\">[{FootnoteNumber(id)}]</a></sup>";
                }
                case "footnotes":
                    return $"<hr/>\n<ol class=\"footnotes\">{Kids(obj)}</ol>\n";
                case "footnote":
                {
                    var id = Str(attrs?["id"]) ?? "";
                    return $"<li id=\"fn-{Enc(id)}\">{Kids(obj)}</li>\n";
                }
                case "inlineMath":
                    return $"<code>{Enc(Str(attrs?["latex"]))}</code>";
                case "blockMath":
                    return $"<pre>{Enc(Str(attrs?["latex"]))}</pre>\n";
                case "mention":
                    return Mention(Str(attrs?["id"]));
                default:
                    return Kids(obj);
            }
        }

        private string RenderText(JsonObject node)
        {
            var html = Enc(Str(node["text"])).Replace("\n", "<br/>");
            if (node["marks"] is not JsonArray marks) return html;

            // fixed nesting order so the tags always nest correctly, whatever order the marks are stored in
            var types = new Dictionary<string, JsonObject>();
            foreach (var m in marks)
            {
                if (m is JsonObject mo && Str(mo["type"]) is { } t) types[t] = mo;
            }

            if (types.ContainsKey("kbd")) html = $"<kbd>{html}</kbd>";
            if (types.ContainsKey("code")) html = $"<code>{html}</code>";
            if (types.ContainsKey("subscript")) html = $"<sub>{html}</sub>";
            if (types.ContainsKey("superscript")) html = $"<sup>{html}</sup>";
            if (types.ContainsKey("bold")) html = $"<strong>{html}</strong>";
            if (types.ContainsKey("italic")) html = $"<em>{html}</em>";
            if (types.ContainsKey("underline")) html = $"<u>{html}</u>";
            if (types.ContainsKey("strike")) html = $"<s>{html}</s>";
            if (types.TryGetValue("link", out var link))
            {
                var href = SafeUrl(Str((link["attrs"] as JsonObject)?["href"]), allowMailto: true);
                if (href != null) html = $"<a href=\"{Enc(href)}\">{html}</a>";
            }

            return html;
        }

        private string RenderImage(JsonObject? attrs)
        {
            var key = Str(attrs?["fileKey"]);
            var src = key == null ? null : payloadUrl?.Invoke(key, null);
            src ??= SafeUrl(Str(attrs?["src"]));
            // No resolvable URL: render nothing rather than a broken image
            if (src == null) return "";

            var alt = Enc(Str(attrs?["alt"]));
            var width = Int(attrs?["width"]);
            var widthAttr = width is > 0 ? $" width=\"{width}\"" : "";
            var align = Str(attrs?["align"]);
            var cls = align is "left" or "right" or "center" ? $" class=\"align-{align}\"" : "";
            var caption = Str(attrs?["caption"]);
            var captionHtml = string.IsNullOrWhiteSpace(caption) ? "" : $"<figcaption>{Enc(caption)}</figcaption>";
            return $"<figure{cls}><img src=\"{Enc(src)}\" alt=\"{alt}\"{widthAttr} />{captionHtml}</figure>\n";
        }

        private string Slug(string text)
        {
            var slug = Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            if (slug == "") return "";
            var candidate = slug;
            for (var n = 2; !_usedSlugs.Add(candidate); n++) candidate = $"{slug}-{n}";
            return candidate;
        }

        private int FootnoteNumber(string id)
        {
            if (!_footnoteNumbers.TryGetValue(id, out var n))
            {
                n = _footnoteNumbers.Count + 1;
                _footnoteNumbers[id] = n;
            }

            return n;
        }
    }

    private static bool HasImage(JsonObject obj)
    {
        if (obj["content"] is not JsonArray children) return false;
        foreach (var c in children)
        {
            if (c is JsonObject co && Str(co["type"]) == "image") return true;
        }

        return false;
    }

    private static string PlainText(JsonNode? node)
    {
        if (node is not JsonObject obj) return "";
        if (Str(obj["type"]) == "hardBreak") return "\n";
        if (Str(obj["text"]) is { } text) return text;
        if (obj["content"] is not JsonArray children) return "";
        var sb = new StringBuilder();
        foreach (var child in children) sb.Append(PlainText(child));
        return sb.ToString();
    }

    private static string Mention(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        value = value.Trim();
        return DomainRegex.IsMatch(value)
            ? $"<a href=\"https://{Enc(value)}\">@{Enc(value)}</a>"
            : $"@{Enc(value)}";
    }

    private static string? SafeUrl(string? url, bool allowMailto = false)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) return url;
        return allowMailto && uri.Scheme == Uri.UriSchemeMailto ? url : null;
    }

    private static bool Bool(JsonNode? node) => node is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static int? Int(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<double>(out var d) ? (int)d : null;

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static string Enc(string? text) => WebUtility.HtmlEncode(text ?? "");
}
