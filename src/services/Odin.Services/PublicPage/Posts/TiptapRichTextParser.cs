#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Odin.Services.PublicPage.Posts.HtmlSafe;

namespace Odin.Services.PublicPage.Posts;

/// <summary>
/// Renders a Tiptap JSON document (the shared rich-text schema) to semantic HTML for server-side rendered pages.
/// Never throws: unknown nodes render their children, and a document it can't read renders as null.
/// Math is emitted as plain text (no KaTeX here).
/// </summary>
public static class TiptapRichTextParser
{
    /// <summary>Nodes that are their children in one tag; block tags end the line</summary>
    private static readonly Dictionary<string, (string Tag, bool Block)> SimpleTags = new()
    {
        ["blockquote"] = ("blockquote", true), ["bulletList"] = ("ul", true), ["listItem"] = ("li", true),
        ["taskList"] = ("ul", true), ["tableRow"] = ("tr", true),
        ["tableHeader"] = ("th", false), ["tableCell"] = ("td", false)
    };

    /// <param name="doc">The Tiptap doc</param>
    /// <param name="payloadUrl">Resolves an image's fileKey and fileId to a URL</param>
    /// <returns>The HTML, or null when the JSON is not a document this can read</returns>
    public static string? Parse(JsonElement doc, PayloadUrlResolver? payloadUrl = null)
    {
        try
        {
            return new Renderer(payloadUrl).Render(JsonObject.Create(doc));
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or FormatException)
        {
            return null; // not an object, or a node with duplicate keys, which JsonObject only finds out when it is read
        }
    }

    private sealed class Renderer(PayloadUrlResolver? payloadUrl)
    {
        private readonly HashSet<string> _usedSlugs = [];
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
                    var id = Slug(PlainText(obj, "content"));
                    return $"<h{level} id=\"{Enc(id)}\">{inner}</h{level}>\n";
                }
                case "codeBlock":
                {
                    var lang = Str(attrs?["language"]);
                    var cls = string.IsNullOrWhiteSpace(lang) ? "" : $" class=\"language-{Enc(lang)}\"";
                    return $"<pre><code{cls}>{Enc(PlainText(obj, "content"))}</code></pre>\n";
                }
                case "orderedList":
                {
                    var start = Int(attrs?["start"]);
                    var startAttr = start is null or 1 ? "" : $" start=\"{start}\"";
                    return $"<ol{startAttr}>{Kids(obj)}</ol>\n";
                }
                case "taskItem":
                {
                    var box = Bool(attrs?["checked"]) ? "&#9745; " : "&#9744; ";
                    return $"<li>{TaskItemContent(obj, box)}</li>\n";
                }
                case "table":
                    return $"<table><tbody>{Kids(obj)}</tbody></table>\n";
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
                    var id = FootnoteId(obj);
                    return $"<sup><a href=\"#fn_{Enc(id)}\" id=\"fnref_{Enc(id)}\">[{FootnoteNumber(id)}]</a></sup>";
                }
                case "footnotes":
                    return $"<hr/>\n<ol class=\"footnotes\">{Kids(obj)}</ol>\n";
                case "footnote":
                    return $"<li id=\"fn_{Enc(FootnoteId(obj))}\">{Kids(obj)}</li>\n";
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

            var alt = Enc(Str(attrs?["alt"]));
            var width = Int(attrs?["width"]);
            var widthAttr = width is > 0 ? $" width=\"{width}\"" : "";
            var align = Str(attrs?["align"]);
            var cls = align is "left" or "right" or "center" ? $" class=\"align-{align}\"" : "";
            var caption = Str(attrs?["caption"]);
            var captionHtml = string.IsNullOrWhiteSpace(caption) ? "" : $"<figcaption>{Enc(caption)}</figcaption>";
            return $"<figure{cls}><img src=\"{Enc(src)}\" alt=\"{alt}\"{widthAttr} />{captionHtml}</figure>\n";
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

        /// <summary>
        /// The footnote's id; one without an id gets its own, so no two share an anchor. Anchors are fn_ and fnref_
        /// plus the id: a heading id is only letters, numbers and '-', so it never takes a footnote's anchor.
        /// </summary>
        private string FootnoteId(JsonObject node)
        {
            if (Str((node["attrs"] as JsonObject)?["id"]) is { Length: > 0 } id) return id;
            if (!_unnamedFootnotes.TryGetValue(node, out var own))
            {
                own = $"noid.{_unnamedFootnotes.Count + 1}";
                _unnamedFootnotes[node] = own;
            }

            return own;
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

    private static int? Int(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<double>(out var d) ? (int)d : null;
}
