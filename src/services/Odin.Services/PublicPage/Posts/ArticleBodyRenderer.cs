#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;

namespace Odin.Services.PublicPage.Posts;

public static class ArticleBodyRenderer
{
    public const string TiptapFormat = "tiptap-v1";

    /// <summary>
    /// Renders bodyDoc when it is a known format and its hash matches the stored body; otherwise the Slate body.
    /// </summary>
    /// <param name="bodyJson">Raw JSON text of the stored body</param>
    public static string? Render(string bodyJson, BodyDoc? bodyDoc, Func<string, string?, string?>? payloadUrl = null)
    {
        if (bodyDoc is { Format: TiptapFormat, Doc: { } doc } &&
            string.Equals(bodyDoc.SlateHash, Sha256Hex(bodyJson), StringComparison.Ordinal))
        {
            var html = TiptapRichTextParser.Parse(doc.GetRawText(), payloadUrl);
            if (html != null) return html;
        }

        return PlateRichTextParser.Parse(bodyJson, payloadUrl);
    }

    private static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
