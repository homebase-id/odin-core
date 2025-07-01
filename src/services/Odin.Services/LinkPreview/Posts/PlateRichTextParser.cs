#nullable enable
using System;
using System.Text;
using System.Text.Json.Nodes;

namespace Odin.Services.LinkPreview.Posts;

public static class PlateRichTextParser
{
    public static string? Parse(string json)
    {
        var root = JsonNode.Parse(json)?.AsArray();
        if (root == null) return null;

        var sb = new StringBuilder();
        foreach (var node in root)
        {
            sb.Append(ParseNode(node));
        }

        return sb.ToString();
    }

    private static string ParseNode(JsonNode? node)
    {
        if (node == null) return "";

        string type = node["type"]?.ToString().ToLowerInvariant() ?? "";
        var children = node["children"]?.AsArray();

        return type switch
        {
            "p" => $"<p>{ParseChildren(children)}</p>\n",
            "a" => $"<a href=\"{HtmlAttr(node["url"])}\">{ParseChildren(children)}</a>",
            "strong" => $"<strong>{ParseChildren(children)}</strong>",
            "em" => $"<em>{ParseChildren(children)}</em>",
            "image" => $"<img src=\"{HtmlAttr(node["url"])}\" width=\"620\" height=\"350\" />\n",
            "div" => $"<div>{ParseChildren(children)}</div>\n",
            "blockquote" => $"<blockquote>{ParseChildren(children)}</blockquote>\n",
            "code" => $"<code>{ParseChildren(children)}</code>",
            "ul" => $"<ul>{ParseChildren(children)}</ul>\n",
            "li" => $"<li>{ParseChildren(children)}</li>\n",
            "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => $"<{type}>{ParseChildren(children)}</{type}>\n",
            _ => ParseChildren(children) // fallback for unknown nodes
        };
    }

    private static string ParseChildren(JsonArray? children)
    {
        if (children == null) return "";

        var sb = new StringBuilder();
        foreach (var child in children)
        {
            if (child?["text"] != null)
            {
                sb.Append(HtmlText(child["text"]?.ToString()));
            }
            else
            {
                sb.Append(ParseNode(child));
            }
        }

        return sb.ToString();
    }

    private static string HtmlText(string? text) =>
        System.Net.WebUtility.HtmlEncode(text ?? "");

    private static string HtmlAttr(JsonNode? attr) =>
        System.Net.WebUtility.HtmlEncode(attr?.ToString() ?? "");
}