using System;
using System.IO;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;

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

    public void AssertMatchesEncryption(string payloadKey, bool isEncrypted)
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
    /// All upload-time rules for one newly written payload, once the file's encryption and drive are known
    /// </summary>
    public static void AssertUploadRules(string payloadKey, PayloadHash hash, bool isEncrypted, bool driveRequiresHashes)
    {
        if (hash == null)
        {
            if (driveRequiresHashes)
            {
                throw new OdinClientException($"Payload {payloadKey}: this drive requires payload hashes",
                    OdinClientErrorCode.PayloadHashRequired);
            }

            return;
        }

        hash.AssertIsWellFormed(payloadKey);
        hash.AssertMatchesEncryption(payloadKey, isEncrypted);
    }

    private static OdinClientException Invalid(string payloadKey, string reason)
    {
        return new OdinClientException($"Payload {payloadKey}: invalid hash, {reason}", OdinClientErrorCode.InvalidPayloadHash);
    }
}
