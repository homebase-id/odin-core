#nullable enable
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Odin.Core;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.AppNotifications.Push;

/// <summary>
/// The header of an incoming file, decrypted on the recipient's server and sealed to each device
/// that registered a preview key, so a push handler that cannot reach the file yet (it is still
/// in the transit inbox) can show it. Only the fields a handler needs to recognise the file.
/// </summary>
public sealed class PushPreview
{
    public Guid? UniqueId { get; set; }
    public Guid? GroupId { get; set; }
    public int FileType { get; set; }
    public int DataType { get; set; }

    /// <summary>The decrypted AppData.Content, shortened by <see cref="PushPreviewSealer.FitToBudget"/>; null when it could not be made to fit.</summary>
    public string? Content { get; set; }

    /// <summary>True when <see cref="Content"/> is shorter than the file's, or was dropped.</summary>
    public bool ContentTruncated { get; set; }
}

/// <summary>
/// Wire format: base64(nonce[12] || AES-128-GCM ciphertext || tag[16]) of the UTF-8 JSON of a
/// <see cref="PushPreview"/>, under the 16-byte key the device registered with its subscription.
/// </summary>
public static class PushPreviewSealer
{
    public const int PreviewKeyLength = 16;

    /// <summary>
    /// The sealed form is base64 of this plus 28 bytes, about 2.1 KB, which leaves room in APNs'
    /// 4 KB for the alert and the rest of the payload.
    /// </summary>
    public const int MaxPlaintextBytes = 1536;

    private const int NonceLength = 12;

    // Relaxed escaping: the default escapes every non-ASCII character, six bytes each, which
    // spends the budget on encoding. The JSON is never embedded in HTML.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <param name="decryptKeyHeader">Called only for an encrypted file.</param>
    public static PushPreview FromIncomingFile(FileMetadata metadata, Func<KeyHeader> decryptKeyHeader)
    {
        var appData = metadata.AppData;
        var content = appData.Content;
        if (metadata.IsEncrypted && !string.IsNullOrEmpty(content))
        {
            content = decryptKeyHeader().Decrypt(Convert.FromBase64String(content)).ToStringFromUtf8Bytes();
        }

        return new PushPreview
        {
            UniqueId = appData.UniqueId,
            GroupId = appData.GroupId,
            FileType = appData.FileType,
            DataType = appData.DataType,
            Content = string.IsNullOrEmpty(content) ? null : content,
        };
    }

    public static bool IsValidKey(string? base64Key) =>
        !string.IsNullOrWhiteSpace(base64Key) && TryDecodeKey(base64Key, out _);

    public static string Seal(PushPreview preview, string base64Key)
    {
        if (!TryDecodeKey(base64Key, out var key))
        {
            throw new ArgumentException("Preview key must be 16 bytes of base64", nameof(base64Key));
        }

        var plaintext = Serialize(FitToBudget(preview));
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var sealedBytes = Odin.Core.Cryptography.Crypto.AesGcm.Encrypt(plaintext, key.ToSensitiveByteArray(), nonce);
        return Convert.ToBase64String(ByteArrayUtil.Combine(nonce, sealedBytes));
    }

    public static PushPreview Open(string sealedPreview, string base64Key)
    {
        if (!TryDecodeKey(base64Key, out var key))
        {
            throw new ArgumentException("Preview key must be 16 bytes of base64", nameof(base64Key));
        }

        var bytes = Convert.FromBase64String(sealedPreview);
        var nonce = bytes[..NonceLength];
        var plaintext = Odin.Core.Cryptography.Crypto.AesGcm.Decrypt(bytes[NonceLength..], key.ToSensitiveByteArray(), nonce);
        return JsonSerializer.Deserialize<PushPreview>(plaintext, JsonOptions)!;
    }

    /// <summary>
    /// Returns the preview unchanged when it fits in <see cref="MaxPlaintextBytes"/>. Otherwise
    /// shortens the content: a JSON object keeps its shape and loses the tail of its string values,
    /// longest first; other content is cut directly. Cuts fall between code points. Content that
    /// still does not fit (a large array, a deep object) is dropped and the handler fetches instead.
    /// </summary>
    public static PushPreview FitToBudget(PushPreview preview)
    {
        if (Fits(preview) || preview.Content == null)
        {
            return preview;
        }

        var node = TryParseObject(preview.Content);
        if (node == null)
        {
            var cut = LongestPrefixThatFits(preview.Content, prefix => Copy(preview, prefix));
            return Copy(preview, cut);
        }

        // Escaping is not proportional (an emoji is twelve bytes once nested), so each string is
        // cut by search, longest first, until the whole preview fits.
        var strings = node
            .Where(p => p.Value is JsonValue v && v.GetValueKind() == JsonValueKind.String)
            .Select(p => (p.Key, Text: p.Value!.GetValue<string>()))
            .OrderByDescending(p => p.Text.Length)
            .ToList();
        foreach (var (key, text) in strings)
        {
            var cut = LongestPrefixThatFits(text, prefix =>
            {
                node[key] = prefix;
                return Copy(preview, node.ToJsonString(JsonOptions));
            });
            node[key] = cut;
            var shortened = Copy(preview, node.ToJsonString(JsonOptions));
            if (Fits(shortened))
            {
                return shortened;
            }
        }

        return Copy(preview, null);
    }

    private static bool Fits(PushPreview preview) => Serialize(preview).Length <= MaxPlaintextBytes;

    /// <summary>The longest prefix of whole code points for which <paramref name="build"/> fits; empty when none does.</summary>
    private static string LongestPrefixThatFits(string text, Func<string, PushPreview> build)
    {
        var runes = text.EnumerateRunes().ToArray();
        int low = 0, high = runes.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (Fits(build(Join(runes, mid))))
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return Join(runes, low);
    }

    private static string Join(Rune[] runes, int count)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            builder.Append(runes[i].ToString());
        }

        return builder.ToString();
    }

    private static PushPreview Copy(PushPreview preview, string? content) => new()
    {
        UniqueId = preview.UniqueId,
        GroupId = preview.GroupId,
        FileType = preview.FileType,
        DataType = preview.DataType,
        Content = content,
        ContentTruncated = true,
    };

    private static byte[] Serialize(PushPreview preview) => JsonSerializer.SerializeToUtf8Bytes(preview, JsonOptions);

    private static JsonObject? TryParseObject(string content)
    {
        try
        {
            return JsonNode.Parse(content) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryDecodeKey(string? base64Key, out byte[] key)
    {
        key = [];
        if (string.IsNullOrWhiteSpace(base64Key))
        {
            return false;
        }

        var buffer = new byte[PreviewKeyLength + 3];
        if (!Convert.TryFromBase64String(base64Key, buffer, out var written) || written != PreviewKeyLength)
        {
            return false;
        }

        key = buffer[..PreviewKeyLength];
        return true;
    }
}
