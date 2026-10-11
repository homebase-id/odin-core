using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Services.Drives.DriveCore.Storage;

/// <summary>
/// Optional client-computed hashes of a payload (#1895). Both hashes use one algorithm and are
/// present together or not at all.
/// <para>StoredHash is over the bytes as uploaded and stored (the ciphertext when encrypted); the
/// server verifies it while the bytes stream in, here and on a receiving peer.</para>
/// <para>ContentHash is over the plaintext. On an encrypted payload it is encrypted by the client with
/// the file's key and IV = payload IV XOR 0xAA..AA, so the server only sees an opaque blob; on an
/// unencrypted payload it must equal StoredHash.</para>
/// </summary>
public class PayloadHash
{
    /// <summary>
    /// A 32-byte hash encrypted with AES-CBC (PKCS7 adds a full block) or AES-GCM (16-byte tag) is 48 bytes
    /// </summary>
    public const int EncryptedContentHashLength = 48;

    /// <summary>
    /// XOR mask that turns the payload IV into the content-hash IV. Never equals the payload IV, since the mask is non-zero.
    /// </summary>
    private const byte ContentHashIvMask = 0xAA;

    public ContentHashAlgorithm Algorithm { get; set; }

    public byte[] StoredHash { get; set; }

    public byte[] ContentHash { get; set; }

    /// <summary>
    /// Checks the rules that do not depend on whether the file is encrypted, so the hash can be
    /// verified while the payload streams in, before the file metadata has been read.
    /// </summary>
    public void AssertIsWellFormed(string payloadKey)
    {
        if (!Enum.IsDefined(Algorithm))
        {
            throw Invalid(payloadKey, $"unknown algorithm {(int)Algorithm}");
        }

        if (StoredHash?.Length != IncrementalContentHash.HashLength)
        {
            throw Invalid(payloadKey, $"storedHash must be {IncrementalContentHash.HashLength} bytes, was {StoredHash?.Length ?? 0}");
        }

        if (ContentHash == null)
        {
            throw Invalid(payloadKey, "storedHash and contentHash must be set together");
        }
    }

    private void AssertMatchesEncryption(string payloadKey, bool isEncrypted)
    {
        if (isEncrypted)
        {
            if (ContentHash.Length != EncryptedContentHashLength)
            {
                throw Invalid(payloadKey,
                    $"an encrypted contentHash must be {EncryptedContentHashLength} bytes, was {ContentHash.Length}");
            }

            return;
        }

        if (!ContentHash.AsSpan().SequenceEqual(StoredHash))
        {
            throw Invalid(payloadKey, "contentHash must equal storedHash when the payload is not encrypted");
        }
    }

    /// <summary>
    /// Wraps the incoming bytes so <see cref="AssertStoredHashMatches"/> can check them once written
    /// </summary>
    public HashingReadStream Verifying(string payloadKey, Stream data)
    {
        AssertIsWellFormed(payloadKey);
        return new HashingReadStream(data, Algorithm);
    }

    public void AssertStoredHashMatches(string payloadKey, HashingReadStream written)
    {
        var actual = written.GetHash();
        if (!actual.AsSpan().SequenceEqual(StoredHash))
        {
            throw new OdinClientException(
                $"Payload {payloadKey}: the received bytes do not match storedHash " +
                $"(expected {Convert.ToBase64String(StoredHash)}, got {Convert.ToBase64String(actual)})",
                OdinClientErrorCode.PayloadHashMismatch);
        }
    }

    /// <summary>
    /// The IV a client encrypts ContentHash with: the payload IV XOR 0xAA..AA. The encrypted hash is therefore bound to
    /// the payload IV, and a different hash must never be encrypted under an unchanged payload IV (that would reuse a
    /// GCM nonce); a hash only changes together with a payload upload, which rotates the IV.
    /// </summary>
    public static byte[] ContentHashIv(byte[] payloadIv)
    {
        var mask = new byte[payloadIv.Length];
        Array.Fill(mask, ContentHashIvMask);
        return ByteArrayUtil.EquiByteArrayXor(payloadIv, mask);
    }

    /// <summary>
    /// The drive's requirePayloadHashes rule. Checked per payload before any of its bytes are read, so a client
    /// without hashes is refused before it streams a large payload for nothing.
    /// </summary>
    public static void AssertRequired(string payloadKey, PayloadHash hash, bool driveRequiresHashes)
    {
        if (hash == null && driveRequiresHashes)
        {
            throw new OdinClientException($"Payload {payloadKey}: this drive requires payload hashes",
                OdinClientErrorCode.PayloadHashRequired);
        }
    }

    /// <summary>
    /// Shape and encryption rules for an optional hash, once the file's encryption is known. Also applied to
    /// descriptors of remote payloads, whose bytes never pass through this server.
    /// </summary>
    public static void AssertValid(string payloadKey, PayloadHash hash, bool isEncrypted)
    {
        if (hash == null)
        {
            return;
        }

        hash.AssertIsWellFormed(payloadKey);
        hash.AssertMatchesEncryption(payloadKey, isEncrypted);
    }

    public static void AssertValid(IEnumerable<PayloadDescriptor> payloads, bool isEncrypted)
    {
        foreach (var payload in payloads ?? [])
        {
            AssertValid(payload.Key, payload.Hash, isEncrypted);
        }
    }

    /// <summary>
    /// <see cref="AssertIvRotated"/> for every uploaded payload. The existing header is only loaded when the rule can
    /// apply: an encrypted file with at least one hashed payload.
    /// </summary>
    public static async Task AssertIvsRotatedAsync(IEnumerable<PackagePayloadDescriptor> uploaded, bool isEncrypted,
        Func<Task<ServerFileHeader>> loadExistingHeader)
    {
        var hashed = uploaded.Where(p => p.Hash != null).ToList();
        if (!isEncrypted || hashed.Count == 0)
        {
            return;
        }

        var existing = (await loadExistingHeader())?.FileMetadata.Payloads;
        foreach (var payload in hashed)
        {
            AssertIvRotated(payload.PayloadKey, payload.Hash, payload.Iv, isEncrypted, existing);
        }
    }

    /// <summary>
    /// The binding rule: a hashed payload that overwrites an existing payload key on an encrypted file must use a new
    /// payload IV, otherwise its encrypted content hash would reuse the previous one's IV (see <see cref="ContentHashIv"/>).
    /// </summary>
    public static void AssertIvRotated(string payloadKey, PayloadHash hash, byte[] payloadIv, bool isEncrypted,
        IEnumerable<PayloadDescriptor> existingPayloads)
    {
        if (hash == null || !isEncrypted)
        {
            return;
        }

        var previous = existingPayloads?.FirstOrDefault(p => p.KeyEquals(payloadKey));
        if (previous?.Iv != null && payloadIv != null && previous.Iv.AsSpan().SequenceEqual(payloadIv))
        {
            throw Invalid(payloadKey, "an encrypted payload with a hash must use a new payload IV when it overwrites an existing payload");
        }
    }

    private static OdinClientException Invalid(string payloadKey, string reason)
    {
        return new OdinClientException($"Payload {payloadKey}: invalid hash, {reason}", OdinClientErrorCode.InvalidPayloadHash);
    }
}
