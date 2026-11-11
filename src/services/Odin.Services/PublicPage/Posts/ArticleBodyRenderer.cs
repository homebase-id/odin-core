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
    /// </summary>
    /// <param name="bodyJson">Raw JSON text of the stored body</param>
    /// <param name="bodyDoc">The post's bodyDoc as stored, in whatever shape</param>
    /// <param name="payloadUrl">Maps an image's payload key and fileId (null for the post's own file) to a URL</param>
    public static string? Render(string bodyJson, JsonElement? bodyDoc, Func<string, string?, string?>? payloadUrl = null)
    {
        if (TiptapDoc(bodyDoc, bodyJson) is { } doc)
        {
            var html = TiptapRichTextParser.Parse(doc, payloadUrl);
            if (html != null) return html;
        }

        return PlateRichTextParser.Parse(bodyJson, payloadUrl);
    }

    /// <summary>
    /// The post's description for meta tags, JSON-LD and RSS: its abstract, or when that is blank, an excerpt of the
    /// prose of a bodyDoc that still matches the body (code and live blocks left out), cut at a word boundary to the
    /// description length. The abstract stays whole: RSS and JSON-LD carry all of it, and meta tags cut their own.
    /// Null when neither has text.
    /// </summary>
    public static string? Description(PostContent? content)
    {
        const int maxLength = LinkPreviewDefaults.MaxDescriptionLength;
        if (content == null) return null;
        if (!string.IsNullOrWhiteSpace(content.Abstract)) return content.Abstract;

        var bodyJson = Convert.ToString(content.Body) ?? string.Empty;
        return TiptapDoc(content.BodyDoc, bodyJson) is { } doc ? TiptapRichTextParser.Excerpt(doc, maxLength) : null;
    }

    private static JsonElement? TiptapDoc(JsonElement? bodyDoc, string bodyJson)
    {
        if (bodyDoc is not { ValueKind: JsonValueKind.Object } d) return null;

        if (!d.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String ||
            format.GetString() != TiptapFormat)
        {
            return null;
        }

        if (!d.TryGetProperty("doc", out var doc) || doc.ValueKind != JsonValueKind.Object) return null;

        if (!d.TryGetProperty("slateHash", out var hash) || hash.ValueKind != JsonValueKind.String ||
            !string.Equals(hash.GetString(), Sha256Hex(bodyJson), StringComparison.Ordinal))
        {
            return null;
        }

        return doc;
    }

    private static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
