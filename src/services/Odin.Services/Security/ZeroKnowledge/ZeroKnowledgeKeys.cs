using System;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Services.Util;

namespace Odin.Services.Security.ZeroKnowledge;

/// <summary>
/// The owner's zero-knowledge keys, as stored (docs/zero-knowledge-keys-plan.md).  Every value is
/// made and opened in the browser; the server only stores them.
/// </summary>
public sealed class ZkKeyRecord
{
    /// <summary>The salt for the second password key: pbkdf2(password, salt), as today's KEK.</summary>
    public string ZkSaltKek64 { get; init; }

    /// <summary>The zero-knowledge master key, locked by the second password key.</summary>
    public string ZkKekEncryptedZkMasterKey { get; init; }

    /// <summary>The login keys (password hash and KEK, as login sends them), locked by the zero-knowledge master key.</summary>
    public string ZkMasterKeyEncryptedLoginKeys { get; init; }

    public UnixTimeUtc Created { get; init; }

    public UnixTimeUtc Updated { get; init; }
}

/// <summary>
/// The browser's keys, sent when it first makes them and on every password change.  All three are
/// required each time: a password change re-locks the master key and changes the login keys.
/// </summary>
public sealed class ZkKeysRequest
{
    public string ZkSaltKek64 { get; set; }
    public string ZkKekEncryptedZkMasterKey { get; set; }
    public string ZkMasterKeyEncryptedLoginKeys { get; set; }

    public void Validate()
    {
        ZkBlob.AssertBase64(ZkSaltKek64, nameof(ZkSaltKek64), exactLength: 16);
        ZkBlob.AssertLocked(ZkKekEncryptedZkMasterKey, nameof(ZkKekEncryptedZkMasterKey));
        ZkBlob.AssertLocked(ZkMasterKeyEncryptedLoginKeys, nameof(ZkMasterKeyEncryptedLoginKeys));
    }
}

/// <summary>What the browser needs after login to open its zero-knowledge master key.</summary>
public sealed class ZkStatusResponse
{
    /// <summary>Null when the browser has not made the keys yet.</summary>
    public string ZkSaltKek64 { get; init; }

    public string ZkKekEncryptedZkMasterKey { get; init; }

    /// <summary>
    /// The Shard Recovery drive's key, locked by the zero-knowledge master key; null until the drive
    /// is converted.  The browser converts the drive again when this no longer opens: after a reset
    /// with the recovery key the master key is new.
    /// </summary>
    public string ShardRecoveryDriveZkMasterKeyEncryptedStorageKey { get; init; }
}

/// <summary>
/// A drive's new keys, made in the browser.  The server never sees the drive key: it gets the key
/// locked by the zero-knowledge master key, the drive id encrypted with it (so a stale key is still
/// refused), and the new write-only key pair.
/// </summary>
public sealed class ZkConvertDriveRequest
{
    public string ZkMasterKeyEncryptedStorageKey { get; set; }

    /// <summary>AES-CBC of the drive id with the new drive key, as drive creation makes it.</summary>
    public string EncryptedIdIv64 { get; set; }

    public string EncryptedIdValue64 { get; set; }

    /// <summary>The write-only public key, as the browser exports ECC public keys.</summary>
    public string WriteOnlyPublicKeyJwk { get; set; }

    /// <summary>The write-only private key (PKCS8), locked by the new drive key.</summary>
    public string ZkStorageKeyEncryptedWriteOnlyPrivateKey { get; set; }

    public void Validate()
    {
        // The limits are the Drives columns'
        ZkBlob.AssertLocked(ZkMasterKeyEncryptedStorageKey, nameof(ZkMasterKeyEncryptedStorageKey), maxLength: 1024);
        ZkBlob.AssertBase64(EncryptedIdIv64, nameof(EncryptedIdIv64), exactLength: 16);
        ZkBlob.AssertBase64(EncryptedIdValue64, nameof(EncryptedIdValue64), maxLength: 1024);
        ZkBlob.AssertLocked(ZkStorageKeyEncryptedWriteOnlyPrivateKey, nameof(ZkStorageKeyEncryptedWriteOnlyPrivateKey), maxLength: 4096);
        OdinValidationUtils.AssertNotNullOrEmpty(WriteOnlyPublicKeyJwk, nameof(WriteOnlyPublicKeyJwk));
    }
}

/// <summary>
/// The browser's locked values are opaque here; only their shape is checked.  A locked value is
/// base64 of a 12-byte IV, the AES-GCM ciphertext and its 16-byte tag.
/// </summary>
internal static class ZkBlob
{
    private const int MinLockedLength = 12 + 1 + 16;
    private const int DefaultMaxLength = 4096;

    public static void AssertLocked(string value64, string name, int maxLength = DefaultMaxLength)
    {
        var bytes = AssertBase64(value64, name, maxLength: maxLength);
        if (bytes.Length < MinLockedLength)
        {
            throw new OdinClientException($"{name} is too short to be a locked value", OdinClientErrorCode.ArgumentError);
        }
    }

    public static byte[] AssertBase64(string value64, string name, int? exactLength = null, int maxLength = DefaultMaxLength)
    {
        OdinValidationUtils.AssertNotNullOrEmpty(value64, name);
        if (value64.Length > maxLength)
        {
            throw new OdinClientException($"{name} is longer than {maxLength}", OdinClientErrorCode.ArgumentError);
        }

        byte[] bytes;
        try
        {
            bytes = value64.FromBase64();
        }
        catch (FormatException)
        {
            throw new OdinClientException($"{name} is not base64", OdinClientErrorCode.ArgumentError);
        }

        if (exactLength.HasValue && bytes.Length != exactLength.Value)
        {
            throw new OdinClientException($"{name} must be {exactLength} bytes", OdinClientErrorCode.ArgumentError);
        }

        return bytes;
    }
}
