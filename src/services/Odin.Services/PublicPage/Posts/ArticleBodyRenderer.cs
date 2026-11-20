#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Odin.Services.PublicPage.Posts;

public static class ArticleBodyRenderer
{
    public const string TiptapFormat = "tiptap-v1";

    /// <summary>
    /// Renders bodyDoc when it is an object whose format is the string tiptap-v1, whose doc is an object and whose
    /// slateHash is a string equal to the hash of the stored body; anything else renders the Slate body.
    /// Null when neither can be read.
    /// </summary>
    /// <param name="bodyJson">Raw JSON text of the stored body</param>
    /// <param name="bodyDoc">The post's bodyDoc as stored, in whatever shape</param>
    /// <param name="payloadUrl">Resolves an image's payload key and fileId (null for the post's own file) to a URL</param>
    public static string? Render(string bodyJson, JsonElement? bodyDoc, PayloadUrlResolver? payloadUrl = null)
    {
        if (TiptapDoc(bodyDoc, bodyJson) is { } doc && TiptapRichTextParser.Parse(doc, payloadUrl) is { } html) return html;
        return PlateRichTextParser.Parse(HtmlSafe.ParseJson(bodyJson), payloadUrl);
    }

    /// <summary>
    /// The post's description for meta tags, JSON-LD and RSS: its abstract, or when that is blank, an excerpt of the
    /// prose of a bodyDoc that still matches the body (code and live blocks left out), cut at a word boundary to the
    /// description length. The abstract stays whole: RSS and JSON-LD carry all of it, and meta tags cut their own.
    /// Null when neither has text.
    /// </summary>
    public static string? Description(PostContent? content)
    {
        if (content == null) return null;
        if (!string.IsNullOrWhiteSpace(content.Abstract)) return content.Abstract;

        var bodyJson = Convert.ToString(content.Body) ?? string.Empty;
        return TiptapDoc(content.BodyDoc, bodyJson) is { } doc
            ? TiptapRichTextParser.Excerpt(doc, LinkPreviewDefaults.MaxDescriptionLength)
            : null;
    }

    private static JsonElement? TiptapDoc(JsonElement? bodyDoc, string bodyJson)
    {
        try
        {
            return bodyDoc is { ValueKind: JsonValueKind.Object } d &&
                   d.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.String &&
                   format.ValueEquals(TiptapFormat) &&
                   d.TryGetProperty("doc", out var doc) && doc.ValueKind == JsonValueKind.Object &&
                   d.TryGetProperty("slateHash", out var hash) && hash.ValueKind == JsonValueKind.String &&
                   hash.ValueEquals(Sha256Hex(bodyJson))
                ? doc
                : null;
        }
        catch (InvalidOperationException)
        {
            return null; // a string that is not valid UTF-16
        }
    }

    private static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
