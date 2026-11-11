#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
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

    private static readonly Regex LanguageRegex = new(@"[^a-z0-9_+#.-]", RegexOptions.CultureInvariant);

    // left is the default, so it needs no style
    private static readonly HashSet<string> TextAligns = ["center", "right", "justify"];
    private static readonly HashSet<string> ImageAligns = ["left", "right", "center"];
    private static readonly HashSet<string> GalleryLayouts = ["row", "grid", "masonry", "carousel"];

    private static readonly Dictionary<string, string> CalloutLabels = new()
    {
        ["info"] = "Info", ["warning"] = "Warning", ["tip"] = "Tip", ["error"] = "Error"
    };

    /// <summary>Code block languages that Journal runs as live blocks; here they only ever show as source</summary>
    private static readonly Dictionary<string, string> LiveBlockNames = new()
    {
        ["html"] = "HTML", ["react"] = "React", ["mermaid"] = "Mermaid", ["svg"] = "SVG"
    };

    /// <param name="json">The Tiptap doc JSON</param>
    /// <param name="payloadUrl">Maps an image fileKey to a URL for the post's payload, or null when unknown</param>
    /// <returns>The HTML, or null when the JSON is not a document this can read</returns>
    public static string? Parse(string json, Func<string, string?, string?>? payloadUrl = null) =>
        Guard(() => JsonNode.Parse(json) is JsonObject root ? new Renderer(payloadUrl).Render(root) : null);

    /// <inheritdoc cref="Parse(string, Func{string, string?, string?}?)"/>
    public static string? Parse(JsonElement doc, Func<string, string?, string?>? payloadUrl = null) =>
        Guard(() => JsonObject.Create(doc) is { } root ? new Renderer(payloadUrl).Render(root) : null);

    /// <summary>
    /// The document's prose as one line of plain text, cut at a word boundary to at most maxLength characters
    /// (with an ellipsis when cut), for a meta description, JSON-LD or RSS. Code blocks (live blocks included)
    /// and footnotes are left out. Null when there is no text or the JSON is unreadable.
    /// </summary>
    public static string? Excerpt(string json, int maxLength) =>
        Guard(() => JsonNode.Parse(json) is JsonObject root ? ExcerptOf(root, maxLength) : null);

    /// <inheritdoc cref="Excerpt(string, int)"/>
    public static string? Excerpt(JsonElement doc, int maxLength) =>
        Guard(() => JsonObject.Create(doc) is { } root ? ExcerptOf(root, maxLength) : null);

    /// <summary>Null for a document this can't read: malformed JSON, or a node with duplicate keys
    /// (JsonObject only finds out when it is read)</summary>
    private static string? Guard(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            return null;
        }
    }

    private static string? ExcerptOf(JsonObject root, int maxLength)
    {
        var sb = new StringBuilder();
        ExcerptText(root, sb, maxLength * 2);
        var text = WhitespaceRegex.Replace(sb.ToString(), " ").Trim();
        return text.Length == 0 ? null : Cut(text, maxLength);
    }

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.CultureInvariant);

    private static void ExcerptText(JsonNode? node, StringBuilder sb, int enough)
    {
        if (node is not JsonObject obj || sb.Length >= enough) return;
        var attrs = obj["attrs"] as JsonObject;
        switch (Str(obj["type"]))
        {
            // the rest without prose keep their data in attrs, which this never reads
            case "codeBlock" or "footnotes":
                return;
            case "hardBreak":
                sb.Append(' ');
                return;
            case "noteLink":
                sb.Append(Str(attrs?["label"]));
                return;
            case "mention":
                if (Str(attrs?["id"])?.Trim() is { Length: > 0 } id) sb.Append('@').Append(id);
                return;
            case "toggle":
                sb.Append(' ').Append(Str(attrs?["summary"])).Append(' ');
                break;
        }

        if (Str(obj["text"]) is { } text) sb.Append(text);
        if (obj["content"] is not JsonArray children) return;
        foreach (var child in children)
        {
            ExcerptText(child, sb, enough);
            // blocks are separate words; inline nodes run on
            if (child is JsonObject co && Str(co["type"]) is not ("text" or "noteLink" or "mention")) sb.Append(' ');
        }
    }

    internal static string Cut(string text, int maxLength)
    {
        if (maxLength <= 0 || text.Length <= maxLength) return text;
        var cut = text[..Math.Max(1, maxLength - 1)];
        // never split a surrogate pair
        if (char.IsHighSurrogate(cut[^1])) cut = cut[..^1];
        var space = cut.LastIndexOf(' ');
        if (space > maxLength / 2) cut = cut[..space];
        return cut.TrimEnd() + "…";
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
                    // Journal's images are inline; a <figure> cannot live inside <p>
                    return HasImage(obj)
                        ? $"<div{BlockStyle(attrs)}>{Kids(obj)}</div>\n"
                        : $"<p{BlockStyle(attrs)}>{Kids(obj)}</p>\n";
                case "heading":
                {
                    var level = Math.Clamp(Int(attrs?["level"]) ?? 1, 1, 6);
                    var inner = Kids(obj);
                    var id = Slug(PlainText(obj));
                    return $"<h{level} id=\"{Enc(id)}\"{BlockStyle(attrs)}>{inner}</h{level}>\n";
                }
                case "blockquote":
                    return $"<blockquote>{Kids(obj)}</blockquote>\n";
                case "codeBlock":
                    return RenderCodeBlock(obj, attrs);
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
                    return $"<ul class=\"task-list\">{Kids(obj)}</ul>\n";
                case "taskItem":
                {
                    var box = Bool(attrs?["checked"]) ? "&#9745; " : "&#9744; ";
                    return $"<li>{TaskItemContent(obj, box)}</li>\n";
                }
                case "table":
                    return $"<table><tbody>{Kids(obj)}</tbody></table>\n";
                case "tableRow":
                    return $"<tr>{Kids(obj)}</tr>\n";
                case "tableHeader":
                    return $"<th{Span(attrs)}>{Kids(obj)}</th>";
                case "tableCell":
                    return $"<td{Span(attrs)}>{Kids(obj)}</td>";
                case "horizontalRule":
                    return "<hr/>\n";
                case "hardBreak":
                    return "<br/>";
                case "image":
                    return RenderImage(attrs, inGallery: false);
                case "gallery":
                {
                    var layout = Pick(Str(attrs?["layout"]), GalleryLayouts) ?? "row";
                    // a gallery lays its images out itself
                    var images = new StringBuilder();
                    foreach (var child in obj["content"] as JsonArray ?? [])
                    {
                        images.Append(child is JsonObject co && Str(co["type"]) == "image"
                            ? RenderImage(co["attrs"] as JsonObject, inGallery: true)
                            : Render(child));
                    }

                    return $"<div class=\"gallery gallery-{layout}\">{images}</div>\n";
                }
                case "callout":
                {
                    var variant = Str(attrs?["variant"]) is { } v && CalloutLabels.ContainsKey(v) ? v : "info";
                    return $"<aside class=\"callout callout-{variant}\" role=\"note\" aria-label=\"{CalloutLabels[variant]}\">{Kids(obj)}</aside>\n";
                }
                case "toggle":
                {
                    var summary = Str(attrs?["summary"])?.Trim();
                    return $"<details><summary>{Enc(string.IsNullOrEmpty(summary) ? "Details" : summary)}</summary>{Kids(obj)}</details>\n";
                }
                case "linkPreview":
                    return RenderLinkPreview(attrs);
                case "footnoteReference":
                {
                    var id = IdSafe(Str(attrs?["id"]));
                    return $"<sup><a href=\"#fn-{id}\" id=\"fnref-{id}\">[{FootnoteNumber(id)}]</a></sup>";
                }
                case "footnotes":
                    return $"<hr/>\n<ol class=\"footnotes\">{Kids(obj)}</ol>\n";
                case "footnote":
                {
                    var id = IdSafe(Str(attrs?["id"]));
                    var back = _footnoteNumbers.ContainsKey(id) ? $" <a href=\"#fnref-{id}\" aria-label=\"Back to reference\">&#8617;</a>" : "";
                    return $"<li id=\"fn-{id}\">{Kids(obj)}{back}</li>\n";
                }
                case "inlineMath":
                    return $"<code class=\"math\">{Enc(Str(attrs?["latex"]))}</code>";
                case "blockMath":
                    return $"<pre class=\"math\">{Enc(Str(attrs?["latex"]))}</pre>\n";
                case "mention":
                    return Mention(Str(attrs?["id"]));
                case "noteLink":
                    // a link to one of the author's private notes: only its label means anything to a reader
                    return Enc(Str(attrs?["label"]));
                default:
                    // a node this renderer does not know: keep its text
                    return Str(obj["text"]) is { } text ? Enc(text) : Kids(obj);
            }
        }

        /// <summary>
        /// The language attr is the fence's whole info string ("html id=k3f9 wide"): its first word is the language.
        /// Live blocks (html, react, mermaid, svg) run in Journal; here they are escaped source with a label, never markup.
        /// </summary>
        private static string RenderCodeBlock(JsonObject obj, JsonObject? attrs)
        {
            var info = Str(attrs?["language"])?.Trim() ?? "";
            var first = info.Split((char[]?)null, 2)[0];
            var lang = LanguageRegex.Replace(first.ToLowerInvariant(), "");
            if (lang.Length > 32) lang = lang[..32];

            var cls = lang.Length == 0 ? "" : $" class=\"language-{lang}\"";
            var code = $"<pre><code{cls}>{Enc(PlainText(obj))}</code></pre>";
            if (!LiveBlockNames.TryGetValue(lang, out var name)) return code + "\n";

            return $"<figure class=\"live-block live-block-{lang}\"><figcaption>Interactive block ({name})</figcaption>{code}</figure>\n";
        }

        private static string RenderLinkPreview(JsonObject? attrs)
        {
            var href = SafeUrl(Str(attrs?["url"]));
            if (href == null) return "";
            var title = Str(attrs?["title"]);
            var label = string.IsNullOrWhiteSpace(title) ? Host(href) : title;
            var desc = Str(attrs?["description"]);
            var descHtml = string.IsNullOrWhiteSpace(desc) ? "" : $"<p>{Enc(desc)}</p>";
            return $"<div class=\"link-preview\"><a href=\"{Enc(href)}\">{Enc(label)}</a>{descHtml}</div>\n";
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

        /// <summary>The checkbox goes inside the item's first paragraph, so it sits on the same line as the text</summary>
        private string TaskItemContent(JsonObject item, string box)
        {
            if (item["content"] is not JsonArray children || children.Count == 0) return box;

            var sb = new StringBuilder();
            var first = children[0] as JsonObject;
            if (first != null && Str(first["type"]) == "paragraph" && !HasImage(first))
            {
                sb.Append($"<p>{box}{Kids(first)}</p>\n");
            }
            else
            {
                sb.Append(box);
                sb.Append(Render(children[0]));
            }

            for (var i = 1; i < children.Count; i++) sb.Append(Render(children[i]));
            return sb.ToString();
        }

        private string RenderImage(JsonObject? attrs, bool inGallery)
        {
            var key = Str(attrs?["fileKey"]);
            // an image copied from another post keeps pointing at that post's file
            var src = key == null ? null : payloadUrl?.Invoke(key, Str(attrs?["fileId"]));
            src ??= SafeUrl(Str(attrs?["src"]));
            // No resolvable URL: render nothing rather than a broken image
            if (src == null) return "";

            var alt = Str(attrs?["alt"]);
            // Journal: a null caption shows the alt text, "" shows none, anything else replaces the alt text
            var caption = attrs?["caption"] is null ? alt : Str(attrs["caption"]);
            var captionHtml = string.IsNullOrWhiteSpace(caption) ? "" : $"<figcaption>{Enc(caption)}</figcaption>";

            // a gallery lays its images out itself
            var width = inGallery ? null : Int(attrs?["width"]);
            var widthAttr = width is > 0 and <= 10000 ? $" width=\"{width}\"" : "";
            var align = inGallery ? null : Pick(Str(attrs?["align"]), ImageAligns);
            var cls = align == null ? "" : $" class=\"align-{align}\"";
            return $"<figure{cls}><img src=\"{Enc(src)}\" alt=\"{Enc(alt)}\"{widthAttr} />{captionHtml}</figure>\n";
        }

        /// <summary>
        /// The heading id rule of the JS viewer, which is the reference: NFKD, drop combining marks, lowercase,
        /// every run of characters that are not a Unicode letter or number (\p{L}\p{N}) becomes '-', '-' trimmed
        /// at both ends, 'section' when nothing is left, and repeats in one document get -2, -3, ...
        /// </summary>
        private string Slug(string text)
        {
            var slug = BaseSlug(text);
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

    internal static string BaseSlug(string text)
    {
        var sb = new StringBuilder(text.Length);
        var dash = false;
        string normalized;
        try
        {
            normalized = text.Normalize(NormalizationForm.FormKD);
        }
        catch (ArgumentException)
        {
            // a lone surrogate; it becomes U+FFFD below, which is not a letter, so a '-' just like in JS
            normalized = text;
        }

        foreach (var rune in normalized.EnumerateRunes())
        {
            if (IsMark(Rune.GetUnicodeCategory(rune))) continue;

            var lower = Rune.ToLowerInvariant(rune);
            if (IsLetterOrNumber(Rune.GetUnicodeCategory(lower)))
            {
                if (dash && sb.Length > 0) sb.Append('-');
                dash = false;
                sb.Append(lower.ToString());
            }
            else
            {
                dash = true;
            }
        }

        // a leading run is dropped above and a trailing one is never written, which is the trim
        return sb.Length == 0 ? "section" : sb.ToString();
    }

    private static bool IsMark(UnicodeCategory c) =>
        c is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    private static bool IsLetterOrNumber(UnicodeCategory c) =>
        c is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber;

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
        switch (Str(obj["type"]))
        {
            case "hardBreak":
                return "\n";
            case "noteLink":
                // as the JS viewer, which turns it into its label, so heading ids match
                return Str((obj["attrs"] as JsonObject)?["label"]) ?? "";
        }

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

    /// <summary>text-align and Journal's indent (2rem a step), from allow-listed values only</summary>
    private static string BlockStyle(JsonObject? attrs)
    {
        if (attrs == null) return "";
        var align = Pick(Str(attrs["textAlign"]), TextAligns);
        var indent = Math.Clamp(Int(attrs["indent"]) ?? 0, 0, 20);
        if (align == null && indent == 0) return "";
        var alignCss = align == null ? "" : $"text-align:{align};";
        var indentCss = indent == 0 ? "" : $"padding-left:{indent * 2}rem;";
        return $" style=\"{alignCss}{indentCss}\"";
    }

    private static string Span(JsonObject? attrs)
    {
        if (attrs == null) return "";
        var colspan = Int(attrs["colspan"]);
        var rowspan = Int(attrs["rowspan"]);
        return (colspan is > 1 and <= 1000 ? $" colspan=\"{colspan}\"" : "") +
               (rowspan is > 1 and <= 1000 ? $" rowspan=\"{rowspan}\"" : "");
    }

    /// <summary>The viewer's footnote id rule: only word characters and '-'</summary>
    private static string IdSafe(string? id) => IdUnsafeRegex.Replace(id ?? "", "");

    private static readonly Regex IdUnsafeRegex = new(@"[^\w-]", RegexOptions.CultureInvariant);

    /// <summary>The value when it is one of the allowed ones, else null</summary>
    private static string? Pick(string? value, HashSet<string> allowed) =>
        value != null && allowed.Contains(value) ? value : null;

    private static string Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

    private static bool Bool(JsonNode? node) => node is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static int? Int(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<double>(out var d) && !double.IsNaN(d) && Math.Abs(d) < int.MaxValue
            ? (int)d
            : null;

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static string Enc(string? text) => WebUtility.HtmlEncode(text ?? "");
}
