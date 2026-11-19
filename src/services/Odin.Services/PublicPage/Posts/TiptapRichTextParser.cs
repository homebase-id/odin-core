#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static Odin.Services.PublicPage.Posts.HtmlSafe;

namespace Odin.Services.PublicPage.Posts;

/// <summary>
/// Renders a Tiptap JSON document (the shared rich-text schema) to semantic HTML for server-side rendered pages.
/// Never throws: unknown nodes render their children, and a document it can't read renders as null.
/// Math is emitted as plain text (no KaTeX here).
/// </summary>
public static class TiptapRichTextParser
{
    private static readonly Regex LanguageRegex = new(@"[^a-z0-9_+#.-]", RegexOptions.CultureInvariant);

    private static readonly Regex IdUnsafeRegex = new(@"[^\w-]", RegexOptions.CultureInvariant);

    /// <summary>Nodes that are their children in one tag; block tags end the line</summary>
    private static readonly Dictionary<string, (string Tag, bool Block)> SimpleTags = new()
    {
        ["blockquote"] = ("blockquote", true), ["bulletList"] = ("ul", true), ["listItem"] = ("li", true),
        ["tableRow"] = ("tr", true)
    };

    private static readonly Dictionary<string, string> CalloutLabels = new()
    {
        ["info"] = "Info", ["warning"] = "Warning", ["tip"] = "Tip", ["error"] = "Error"
    };

    /// <param name="doc">The Tiptap doc</param>
    /// <param name="payloadUrl">Resolves an image's fileKey and fileId to a URL</param>
    /// <returns>The HTML, or null when the JSON is not a document this can read</returns>
    public static string? Parse(JsonElement doc, PayloadUrlResolver? payloadUrl = null) =>
        Guard(() => new Renderer(payloadUrl).Render(JsonObject.Create(doc)));

    /// <summary>
    /// The document's prose as one line of plain text, cut at a word boundary to at most maxLength characters
    /// (with an ellipsis when cut), for a meta description, JSON-LD or RSS. Code blocks (live blocks included)
    /// and footnotes are left out. Null when there is no text or the document can't be read.
    /// </summary>
    public static string? Excerpt(JsonElement doc, int maxLength) =>
        Guard(() => JsonObject.Create(doc) is { } root ? ExcerptOf(root, maxLength) : null);

    /// <summary>Null for a document this can't read: not an object, or a node with duplicate keys
    /// (JsonObject only finds out when it is read)</summary>
    private static string? Guard(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or FormatException)
        {
            return null;
        }
    }

    private static string? ExcerptOf(JsonObject root, int maxLength)
    {
        var sb = new StringBuilder();
        ExcerptText(root, sb, maxLength * 2);
        var text = sb.ToString().Trim();
        return text.Length == 0 ? null : Cut(text, maxLength);
    }

    /// <summary>Appends text with every run of whitespace as one space, so the budget counts what is kept</summary>
    private static void Add(StringBuilder sb, string? text)
    {
        foreach (var c in text ?? "")
        {
            if (!char.IsWhiteSpace(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
        }
    }

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
                Add(sb, " ");
                return;
            case "noteLink":
                Add(sb, Str(attrs?["label"]));
                return;
            case "mention":
                if (Str(attrs?["id"])?.Trim() is { Length: > 0 } id) Add(sb, "@" + id);
                return;
            case "toggle":
                Add(sb, $" {Str(attrs?["summary"])} ");
                break;
        }

        Add(sb, Str(obj["text"]));
        if (obj["content"] is not JsonArray children) return;
        foreach (var child in children)
        {
            ExcerptText(child, sb, enough);
            // blocks are separate words; inline nodes run on
            if (child is JsonObject co && !IsInline(Str(co["type"]))) Add(sb, " ");
        }
    }

    private static bool IsInline(string? type) =>
        type is "text" or "noteLink" or "mention" or "footnoteReference" or "inlineMath" or "image" or "hardBreak";

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

    private sealed class Renderer(PayloadUrlResolver? payloadUrl)
    {
        private readonly HashSet<string> _usedSlugs = [];
        private readonly Dictionary<string, int> _nextSuffix = [];
        private readonly Dictionary<string, int> _footnoteNumbers = [];
        private readonly Dictionary<JsonObject, string> _unnamedFootnotes = new(ReferenceEqualityComparer.Instance);

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
            var type = Str(obj["type"]);
            if (type != null && SimpleTags.TryGetValue(type, out var simple))
            {
                return $"<{simple.Tag}>{Kids(obj)}</{simple.Tag}>{(simple.Block ? "\n" : "")}";
            }

            switch (type)
            {
                case "doc":
                    NumberFootnotes(obj);
                    return Kids(obj);
                case "text":
                    return RenderText(obj);
                case "paragraph":
                    // Journal's images are inline; a <figure> cannot live inside <p>
                    return HasImage(obj) ? $"<div>{Kids(obj)}</div>\n" : $"<p>{Kids(obj)}</p>\n";
                case "heading":
                {
                    var level = Math.Clamp(Int(attrs?["level"]) ?? 1, 1, 6);
                    var inner = Kids(obj);
                    var id = Slug(PlainText(obj, "content"));
                    return $"<h{level} id=\"{Enc(id)}\">{inner}</h{level}>\n";
                }
                case "codeBlock":
                    return RenderCodeBlock(obj, attrs);
                case "orderedList":
                {
                    var start = Int(attrs?["start"]);
                    var startAttr = start is null or 1 ? "" : $" start=\"{start}\"";
                    return $"<ol{startAttr}>{Kids(obj)}</ol>\n";
                }
                case "taskList":
                    return $"<ul class=\"task-list\">{Kids(obj)}</ul>\n";
                case "taskItem":
                {
                    var box = Bool(attrs?["checked"]) ? "&#9745; " : "&#9744; ";
                    return $"<li>{TaskItemContent(obj, box)}</li>\n";
                }
                case "table":
                    return $"<table><tbody>{Kids(obj)}</tbody></table>\n";
                case "tableHeader":
                    return $"<th{Span(attrs)}>{Kids(obj)}</th>";
                case "tableCell":
                    return $"<td{Span(attrs)}>{Kids(obj)}</td>";
                case "horizontalRule":
                    return "<hr/>\n";
                case "hardBreak":
                    return "<br/>";
                case "image":
                    return RenderImage(attrs);
                case "gallery":
                    return $"<div class=\"gallery\">{Kids(obj)}</div>\n";
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
                    var id = FootnoteId(obj);
                    return $"<sup><a href=\"#fn_{id}\" id=\"fnref_{id}\">[{FootnoteNumber(id)}]</a></sup>";
                }
                case "footnotes":
                    return $"<hr/>\n<ol class=\"footnotes\">{Kids(obj)}</ol>\n";
                case "footnote":
                {
                    var id = FootnoteId(obj);
                    if (!_footnoteNumbers.TryGetValue(id, out var n)) return $"<li id=\"fn_{id}\">{Kids(obj)}</li>\n";
                    var back = $" <a href=\"#fnref_{id}\" aria-label=\"Back to reference\">&#8617;</a>";
                    return $"<li id=\"fn_{id}\" value=\"{n}\">{Kids(obj)}{back}</li>\n";
                }
                case "inlineMath":
                    return $"<code class=\"math\">{Enc(Str(attrs?["latex"]))}</code>";
                case "blockMath":
                    return $"<pre class=\"math\">{Enc(Str(attrs?["latex"]))}</pre>\n";
                case "mention":
                    return Mention(Str(attrs?["id"]));
                case "noteLink":
                    // a link to one of the author's private notes: only its label means anything to a reader.
                    // Its marks stay, as in the JS viewer
                    return RenderText(new JsonObject
                    {
                        ["text"] = Str(attrs?["label"]) ?? "", ["marks"] = obj["marks"]?.DeepClone()
                    });
                default:
                    // a node this renderer does not know: keep its text
                    return Str(obj["text"]) is { } text ? Enc(text) : Kids(obj);
            }
        }

        /// <summary>
        /// The language attr is the fence's whole info string ("html id=k3f9 wide"): its first word is the language.
        /// Live blocks (html, react, mermaid, svg) run in Journal; here they are escaped source like any code.
        /// </summary>
        private static string RenderCodeBlock(JsonObject obj, JsonObject? attrs)
        {
            var info = Str(attrs?["language"])?.Trim() ?? "";
            var first = info.Split((char[]?)null, 2)[0];
            var lang = LanguageRegex.Replace(first.ToLowerInvariant(), "");
            if (lang.Length > 32) lang = lang[..32];

            var cls = lang.Length == 0 ? "" : $" class=\"language-{lang}\"";
            return $"<pre><code{cls}>{Enc(PlainText(obj, "content"))}</code></pre>\n";
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

            var types = new Dictionary<string, JsonObject>();
            foreach (var m in marks)
            {
                if (m is JsonObject mo && Str(mo["type"]) is { } t) types[t] = mo;
            }

            html = WrapMarks(html, types.ContainsKey);
            if (types.TryGetValue("link", out var link))
            {
                var href = SafeUrl(Str((link["attrs"] as JsonObject)?["href"]), allowMailto: true, allowRelative: true);
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

        private string RenderImage(JsonObject? attrs)
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

            var width = Int(attrs?["width"]);
            var widthAttr = width is > 0 and <= 10000 ? $" width=\"{width}\"" : "";
            var align = Str(attrs?["align"]);
            var cls = align is "left" or "right" or "center" ? $" class=\"align-{align}\"" : "";
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
            // the next suffix per base slug keeps N equal headings linear, not quadratic
            var n = _nextSuffix.GetValueOrDefault(slug, 2);
            while (!_usedSlugs.Add(candidate)) candidate = $"{slug}-{n++}";
            _nextSuffix[slug] = n;
            return candidate;
        }

        /// <summary>
        /// The footnote's id by the viewer's rule (only word characters and '-'); one without an id gets its own,
        /// with a '.' that no real id keeps. Anchors are fn_ and fnref_ plus the id: a heading id is only letters,
        /// numbers and '-', so it never takes a footnote's anchor.
        /// </summary>
        private string FootnoteId(JsonObject node)
        {
            var id = IdUnsafeRegex.Replace(Str((node["attrs"] as JsonObject)?["id"]) ?? "", "");
            if (id.Length > 0) return id;
            if (!_unnamedFootnotes.TryGetValue(node, out var own))
            {
                own = $"noid.{_unnamedFootnotes.Count + 1}";
                _unnamedFootnotes[node] = own;
            }

            return own;
        }

        /// <summary>As the JS viewer: references numbered in document order before anything renders, so a
        /// footnotes section anywhere gets the same numbers and back links</summary>
        private void NumberFootnotes(JsonNode? node)
        {
            if (node is not JsonObject obj) return;
            if (Str(obj["type"]) == "footnoteReference")
            {
                FootnoteNumber(FootnoteId(obj));
                return;
            }

            if (obj["content"] is JsonArray children)
            {
                foreach (var child in children) NumberFootnotes(child);
            }
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

    private static string Span(JsonObject? attrs)
    {
        if (attrs == null) return "";
        var colspan = Int(attrs["colspan"]);
        var rowspan = Int(attrs["rowspan"]);
        return (colspan is > 1 and <= 1000 ? $" colspan=\"{colspan}\"" : "") +
               (rowspan is > 1 and <= 1000 ? $" rowspan=\"{rowspan}\"" : "");
    }

    private static string Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

    private static int? Int(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<double>(out var d) && !double.IsNaN(d) && Math.Abs(d) < int.MaxValue
            ? (int)d
            : null;
}
