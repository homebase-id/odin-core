#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;
using static Odin.Services.PublicPage.Posts.HtmlSafe;

namespace Odin.Services.PublicPage.Posts;

/// <summary>
/// Renders a Plate/Slate JSON body to HTML for server-side rendered pages.
/// Never throws: unknown nodes render their children or text, and a body it can't read renders as null.
/// </summary>
public static class PlateRichTextParser
{
    /// <summary>Nodes that are their children in one tag; block tags end the line</summary>
    private static readonly Dictionary<string, (string Tag, bool Block)> SimpleTags = new()
    {
        ["p"] = ("p", true), ["paragraph"] = ("p", true),
        ["h1"] = ("h1", true), ["h2"] = ("h2", true), ["h3"] = ("h3", true),
        ["h4"] = ("h4", true), ["h5"] = ("h5", true), ["h6"] = ("h6", true),
        ["blockquote"] = ("blockquote", true), ["ul"] = ("ul", true), ["ol"] = ("ol", true), ["li"] = ("li", true),
        ["div"] = ("div", true), ["figure"] = ("figure", true), ["figcaption"] = ("figcaption", true),
        ["strong"] = ("strong", false), ["em"] = ("em", false), ["code"] = ("code", false)
    };

    /// <param name="root">The body: an array of nodes; a single node object is tolerated</param>
    /// <param name="payloadUrl">Resolves a local_image's fileKey and fileId to a URL</param>
    /// <returns>The HTML, or null when there is no body or it can't be read</returns>
    public static string? Parse(JsonNode? root, PayloadUrlResolver? payloadUrl = null)
    {
        if (root == null) return null;
        try
        {
            if (root is not JsonArray array) return ParseNode(root, payloadUrl);
            var sb = new StringBuilder();
            foreach (var node in array) sb.Append(ParseNode(node, payloadUrl));
            return sb.ToString();
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or FormatException)
        {
            return null; // e.g. a node with duplicate keys, which JsonObject only finds out when it is read
        }
    }

    private static string ParseNode(JsonNode? node, PayloadUrlResolver? payloadUrl)
    {
        if (node is not JsonObject obj) return "";

        var type = Str(obj["type"])?.ToLowerInvariant() ?? "";
        if (type == "" && obj.ContainsKey("text")) return ParseLeaf(obj);

        string Kids() => ParseChildren(obj, payloadUrl);

        if (SimpleTags.TryGetValue(type, out var simple))
        {
            return $"<{simple.Tag}>{Kids()}</{simple.Tag}>{(simple.Block ? "\n" : "")}";
        }

        switch (type)
        {
            case "lic":
                return Kids();
            case "code_block":
                return $"<pre><code>{CodeLines(obj)}</code></pre>\n";
            case "a":
                return Anchor(Str(obj["url"]), Kids());
            case "link":
                return Anchor(Str(obj["linkTarget"]), Enc(Str(obj["linkText"])));
            case "mention":
                return Mention(Str(obj["value"]));
            case "image":
            {
                var src = SafeUrl(Str(obj["url"]));
                return src == null ? "" : $"<img src=\"{Enc(src)}\" width=\"620\" height=\"350\" />\n";
            }
            case "local_image":
            {
                var key = Str(obj["fileKey"]);
                // an image copied from another post keeps pointing at that post's file
                var src = key == null ? null : payloadUrl?.Invoke(key, Str(obj["fileId"]));
                // No payload URL available: render nothing rather than a broken image
                return src == null ? "" : $"<img src=\"{Enc(src)}\" alt=\"\" />\n";
            }
            default:
                return obj["children"] is JsonArray ? Kids() : Enc(Str(obj["text"]));
        }
    }

    private static string ParseChildren(JsonObject obj, PayloadUrlResolver? payloadUrl)
    {
        if (obj["children"] is not JsonArray children)
        {
            // e.g. an 'a' node that carries its text directly
            return Enc(Str(obj["text"]));
        }

        var sb = new StringBuilder();
        foreach (var child in children)
        {
            sb.Append(ParseNode(child, payloadUrl));
        }

        return sb.ToString();
    }

    private static string ParseLeaf(JsonObject leaf) =>
        WrapMarks(Enc(Str(leaf["text"])).Replace("\n", "<br/>"), mark => Bool(leaf[mark]));

    private static string CodeLines(JsonObject block)
    {
        if (block["children"] is not JsonArray lines) return Enc(Str(block["text"]));

        var sb = new StringBuilder();
        var first = true;
        foreach (var line in lines)
        {
            if (line is not JsonObject) continue;
            if (!first) sb.Append('\n');
            first = false;
            sb.Append(Enc(PlainText(line, "children")));
        }

        return sb.ToString();
    }

    private static string Anchor(string? url, string inner)
    {
        var href = SafeUrl(url, allowMailto: true);
        return href == null ? inner : $"<a href=\"{Enc(href)}\">{inner}</a>";
    }
}
