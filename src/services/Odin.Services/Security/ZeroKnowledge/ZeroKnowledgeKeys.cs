using System;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Login;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Util;

namespace Odin.Services.Security.ZeroKnowledge;

/// <summary>
/// The owner's zero-knowledge keys, as stored (docs/zero-knowledge-keys-plan.md and
/// docs/zero-knowledge-recovery-plan.md).  Every value is made and opened in the browser; the
/// server only stores them.
/// </summary>
/// <remarks>
/// One record, so a reset with today's master key, which forgets it, forgets every copy tied to the
/// lost zero-knowledge master key with it.
/// </remarks>
public sealed record ZkKeyRecord
{
    /// <summary>The salt for the second password key: pbkdf2(password, salt), as today's KEK.</summary>
    public string ZkSaltKek64 { get; init; }

    /// <summary>The zero-knowledge master key, locked by the second password key.</summary>
    public string ZkKekEncryptedZkMasterKey { get; init; }

    /// <summary>The zero-knowledge master key, locked by the recovery phrase.  Null until the takeover.</summary>
    public string PhraseKeyEncryptedZkMasterKey { get; init; }

    /// <summary>The recovery phrase, locked by the zero-knowledge master key, for showing it again.</summary>
    public string ZkMasterKeyEncryptedPhraseKey { get; init; }

    /// <summary>
    /// The public half of the phrase's key pair (P-384, a JWK), made by the browser at the takeover.
    /// The browser seals the password to it at a password login, which needs no phrase; only the
    /// phrase opens what is sealed.
    /// </summary>
    public string PhrasePublicKeyJwk { get; init; }

    /// <summary>The private half of the phrase's key pair (PKCS8), locked by the recovery phrase.</summary>
    public string PhraseKeyEncryptedPhrasePrivateKey { get; init; }

    /// <summary>
    /// The owner's password, sealed to <see cref="PhrasePublicKeyJwk"/>: what recovery with the phrase
    /// gives back.  Sealed, not locked by the zero-knowledge master key, so someone who once had the
    /// password can't read the next one.  Null until a password login or change writes it.
    /// </summary>
    public string PhrasePublicKeyEncryptedPassword { get; init; }

    /// <summary>
    /// Today's master key, locked by the reset key (derived one way from the recovery phrase), so
    /// recovery with the phrase can always reset the password, even when someone with the password
    /// sealed junk as the password copy.  The server locks it itself, so it is correct by
    /// construction.
    /// </summary>
    public SymmetricKeyEncryptedAes ResetKeyEncryptedMasterKey { get; init; }

    public UnixTimeUtc Created { get; init; }

    public UnixTimeUtc Updated { get; init; }

    /// <summary>
    /// Whether the phrase copies are here.  Once the phrase is taken over, their absence means a reset
    /// with today's master key forgot them, and the owner types the phrase in again.
    /// </summary>
    public bool HasPhraseCopies() => PhraseKeyEncryptedZkMasterKey != null && ZkMasterKeyEncryptedPhraseKey != null &&
                                     PhrasePublicKeyJwk != null && PhraseKeyEncryptedPhrasePrivateKey != null &&
                                     ResetKeyEncryptedMasterKey != null;
}

/// <summary>
/// The browser's keys, sent when it first makes them and on every password change: a password change
/// re-locks the master key.
/// </summary>
public sealed class ZkKeysRequest
{
    public string ZkSaltKek64 { get; set; }
    public string ZkKekEncryptedZkMasterKey { get; set; }

    /// <summary>
    /// The new password sealed to the phrase's public key, once the phrase was taken over.  Without
    /// it a password change forgets the copy, and the next password login writes it.
    /// </summary>
    public string PhrasePublicKeyEncryptedPassword { get; set; }

    public void Validate()
    {
        ZkBlob.AssertBase64(ZkSaltKek64, nameof(ZkSaltKek64), exactLength: 16);
        ZkBlob.AssertLocked(ZkKekEncryptedZkMasterKey, nameof(ZkKekEncryptedZkMasterKey));
        if (PhrasePublicKeyEncryptedPassword != null)
        {
            ZkBlob.AssertSealed(PhrasePublicKeyEncryptedPassword, nameof(PhrasePublicKeyEncryptedPassword));
        }
    }
}

/// <summary>
/// The password sealed to the phrase's public key, as a password login writes it when the copy is
/// missing, with proof of the password itself (against an authentication nonce, as a password change
/// proves it).
/// </summary>
public sealed class ZkPasswordRequest
{
    public string PhrasePublicKeyEncryptedPassword { get; set; }

    public PasswordReply CurrentAuthenticationPasswordReply { get; set; }

    public void Validate()
    {
        ZkBlob.AssertSealed(PhrasePublicKeyEncryptedPassword, nameof(PhrasePublicKeyEncryptedPassword));
        OdinValidationUtils.AssertNotNull(CurrentAuthenticationPasswordReply, nameof(CurrentAuthenticationPasswordReply));
    }
}

/// <summary>Asks for the recovery phrase for the takeover, with proof of the password itself.</summary>
public sealed class ZkPhraseHandoverRequest
{
    public PasswordReply CurrentAuthenticationPasswordReply { get; set; }

    public void Validate() =>
        OdinValidationUtils.AssertNotNull(CurrentAuthenticationPasswordReply, nameof(CurrentAuthenticationPasswordReply));
}

/// <summary>
/// What the browser makes from the recovery phrase: the zero-knowledge master key locked by it, the
/// phrase locked by the zero-knowledge master key, the phrase's key pair, and the reset key.
/// </summary>
public class ZkPhraseCopiesRequest
{
    public string PhraseKeyEncryptedZkMasterKey { get; set; }
    public string ZkMasterKeyEncryptedPhraseKey { get; set; }

    /// <summary>A new key pair: see <see cref="ZkKeyRecord.PhrasePublicKeyJwk"/>.</summary>
    public string PhrasePublicKeyJwk { get; set; }

    public string PhraseKeyEncryptedPhrasePrivateKey { get; set; }

    /// <summary>
    /// The reset key: hkdf(recovery phrase), one way.  The server locks today's master key with it
    /// and keeps nothing else of it.
    /// </summary>
    public string ResetKey64 { get; set; }

    /// <summary>
    /// The phrase's check value (<see cref="RecoveryPhraseCheck"/>), so the server refuses copies made
    /// with any other phrase: one the browser got wrong at the takeover, or one mistyped later.
    /// </summary>
    public string PhraseCheck64 { get; set; }

    public virtual void Validate()
    {
        ZkBlob.AssertLocked(PhraseKeyEncryptedZkMasterKey, nameof(PhraseKeyEncryptedZkMasterKey));
        ZkBlob.AssertLocked(ZkMasterKeyEncryptedPhraseKey, nameof(ZkMasterKeyEncryptedPhraseKey));
        ZkBlob.AssertPublicKey(PhrasePublicKeyJwk, nameof(PhrasePublicKeyJwk));
        ZkBlob.AssertLocked(PhraseKeyEncryptedPhrasePrivateKey, nameof(PhraseKeyEncryptedPhrasePrivateKey));
        ZkBlob.AssertBase64(PhraseCheck64, nameof(PhraseCheck64), exactLength: RecoveryPhraseCheck.Length);
        ZkBlob.AssertBase64(ResetKey64, nameof(ResetKey64), exactLength: ZkBlob.KeyLength);
    }
}

/// <summary>
/// The takeover: the phrase copies and the password sealed to the new public key, with proof of the
/// password itself.
/// </summary>
public sealed class ZkPhraseTakeoverRequest : ZkPhraseCopiesRequest
{
    public string PhrasePublicKeyEncryptedPassword { get; set; }

    public PasswordReply CurrentAuthenticationPasswordReply { get; set; }

    public override void Validate()
    {
        base.Validate();
        ZkBlob.AssertSealed(PhrasePublicKeyEncryptedPassword, nameof(PhrasePublicKeyEncryptedPassword));
        OdinValidationUtils.AssertNotNull(CurrentAuthenticationPasswordReply, nameof(CurrentAuthenticationPasswordReply));
    }
}

/// <summary>
/// Recovery with the phrase when the password copy doesn't log in: the reset key, a new password, and
/// the recovered zero-knowledge master key locked by the new password.
/// </summary>
public sealed class ZkPhraseResetRequest
{
    /// <summary>
    /// The reset key's base64, encrypted to the host's offline key as <c>resetpasswdrk</c> sends the
    /// phrase: it outlives any password, so it never travels readable past TLS.
    /// </summary>
    public EccEncryptedPayload EncryptedResetKey { get; set; }

    /// <summary>The new password, as a password is set (against fresh salts).</summary>
    public PasswordReply PasswordReply { get; set; }

    public ZkKeysRequest ZkKeys { get; set; }

    public void Validate()
    {
        OdinValidationUtils.AssertNotNull(EncryptedResetKey, nameof(EncryptedResetKey));
        if (EncryptedResetKey.KeyType != PublicPrivateKeyType.OfflineKey ||
            !(EncryptedResetKey.Iv?.Length > 0 && EncryptedResetKey.Salt?.Length > 0 && EncryptedResetKey.EncryptedData?.Length > 0))
        {
            throw new OdinClientException("EncryptedResetKey must be encrypted to the offline key", OdinClientErrorCode.ArgumentError);
        }

        ZkBlob.AssertPublicKey(EncryptedResetKey.RemotePublicKeyJwk, nameof(EncryptedResetKey.RemotePublicKeyJwk));
        OdinValidationUtils.AssertNotNull(PasswordReply, nameof(PasswordReply));
        OdinValidationUtils.AssertNotNull(ZkKeys, nameof(ZkKeys));
        ZkKeys.Validate();
    }
}

/// <summary>Where the owner's recovery phrase is (docs/zero-knowledge-recovery-plan.md, decision 3).</summary>
public enum RecoveryPhraseState
{
    /// <summary>No phrase was ever made.</summary>
    None = 0,

    /// <summary>The server still holds the phrase; the next password login takes it over.</summary>
    OnServer = 1,

    /// <summary>The browser holds it; the server can open none of its copies.</summary>
    TakenOver = 2,

    /// <summary>
    /// Taken over, but a reset with today's master key forgot its copies.  The owner types it in
    /// once to keep it.
    /// </summary>
    NeedsReentry = 3
}

/// <summary>What the browser needs after login to open its zero-knowledge master key.</summary>
public sealed class ZkStatusResponse
{
    /// <summary>Null when the browser has not made the keys yet.</summary>
    public string ZkSaltKek64 { get; init; }

    public string ZkKekEncryptedZkMasterKey { get; init; }

    public RecoveryPhraseState RecoveryPhrase { get; init; }

    /// <summary>
    /// The phrase's public key once the phrase is taken over (null before, and after a reset with
    /// today's master key until it is typed in again): a password login or change seals the password to it.
    /// </summary>
    public string PhrasePublicKeyJwk { get; init; }

    /// <summary>
    /// Whether the password copy exists.  The browser can't open it (only the phrase does), so it only
    /// writes one when it is missing.
    /// </summary>
    public bool HasPasswordCopy { get; init; }
}

/// <summary>The recovery phrase, handed over once for the takeover.</summary>
public sealed class ZkPhraseHandoverResponse
{
    /// <summary>The 12 BIP39 words.</summary>
    public string Phrase { get; init; }
}

/// <summary>
/// What recovery with the phrase fetches, with no login: all of it is useless without the phrase.
/// </summary>
public sealed class ZkPhraseRecoveryResponse
{
    public string PhraseKeyEncryptedZkMasterKey { get; init; }
    public string PhraseKeyEncryptedPhrasePrivateKey { get; init; }

    /// <summary>For the reset: the browser seals the new password to it.</summary>
    public string PhrasePublicKeyJwk { get; init; }

    /// <summary>Null when no password login wrote it yet; the page then offers the reset.</summary>
    public string PhrasePublicKeyEncryptedPassword { get; init; }
}

/// <summary>The owner session's half of the browser's stored zero-knowledge master key (decision 8).</summary>
public sealed class ZkSessionKeyResponse
{
    public string SessionKey64 { get; init; }
}

/// <summary>
/// The check value of a recovery phrase: a hash of its 16 bytes, so a phrase typed in later can be
/// checked without the server seeing it.  The key is random, so the hash can't be reversed.  The
/// browser computes the same value.
/// </summary>
public static class RecoveryPhraseCheck
{
    private static readonly byte[] Prefix = "homebase-recovery-phrase-check".ToUtf8ByteArray();

    /// <summary>A SHA-256, in bytes.</summary>
    public const int Length = 32;

    public static string Compute(byte[] phraseKey) =>
        System.Security.Cryptography.SHA256.HashData(ByteArrayUtil.Combine(Prefix, phraseKey)).ToBase64();
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
        ZkBlob.AssertPublicKey(WriteOnlyPublicKeyJwk, nameof(WriteOnlyPublicKeyJwk));
    }
}

/// <summary>
/// The browser's locked values are opaque here; only their shape is checked.  A locked value is
/// base64 of a 12-byte IV, the AES-GCM ciphertext and its 16-byte tag.  A sealed value is base64 of
/// a throwaway P-384 public key (97 bytes, uncompressed) and a locked value, locked by
/// hkdf(ECDH(throwaway, recipient)).
/// </summary>
internal static class ZkBlob
{
    public const int KeyLength = 16;
    private const int MinLockedLength = 12 + 1 + 16;
    private const int SealPublicKeyLength = 97;
    private const int DefaultMaxLength = 4096;

    public static void AssertSealed(string value64, string name)
    {
        var bytes = AssertBase64(value64, name);
        if (bytes.Length < SealPublicKeyLength + MinLockedLength || bytes[0] != 0x04)
        {
            throw new OdinClientException($"{name} is not a sealed value", OdinClientErrorCode.ArgumentError);
        }
    }

    /// <summary>A P-384 public key as a JWK, the only curve the browser makes and seals with.</summary>
    public static void AssertPublicKey(string jwk, string name)
    {
        OdinValidationUtils.AssertNotNullOrEmpty(jwk, name);
        try
        {
            using var parsed = System.Text.Json.JsonDocument.Parse(jwk);
            if (parsed.RootElement.GetProperty("crv").GetString() == "P-384")
            {
                EccPublicKeyData.FromJwkPublicKey(jwk, 1);
                return;
            }
        }
        catch (Exception e) when (e is not OdinClientException)
        {
            // falls through
        }

        throw new OdinClientException($"{name} is not a P-384 public key", OdinClientErrorCode.ArgumentError);
    }

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
