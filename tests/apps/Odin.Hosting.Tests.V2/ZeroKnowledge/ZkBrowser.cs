#nullable enable
using System;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Services.Apps;
using Odin.Services.Peer.Encryption;
using Odin.Services.Security.ZeroKnowledge;
using AesGcm = Odin.Core.Cryptography.Crypto.AesGcm;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// The browser's zero-knowledge key maths, in C#, the way <c>OwnerPasswordFlow</c> copies its login
/// maths.  The odin-js code must produce values this opens, and open what this makes.
/// </summary>
/// <remarks>
/// A locked value is base64 of a 12-byte IV followed by the AES-GCM ciphertext and its 16-byte tag.
/// Keys are 16 bytes, like today's master key and drive keys.  A sealed value is
/// <see cref="ZkSeal"/>'s.
/// </remarks>
internal static class ZkBrowser
{
    private const int IvLength = 12;

    /// <summary>pbkdf2 with the same algorithm and iterations as today's KEK.</summary>
    public static byte[] SecondPasswordKey(string password, string salt64)
    {
        return KeyDerivation.Pbkdf2(password, salt64.FromBase64(), KeyDerivationPrf.HMACSHA256,
            CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE);
    }

    public static string Lock(byte[] data, byte[] key)
    {
        var iv = ByteArrayUtil.GetRndByteArray(IvLength);
        var cipher = AesGcm.Encrypt(data, key.ToSensitiveByteArray(), iv);
        return ByteArrayUtil.Combine(iv, cipher).ToBase64();
    }

    public static byte[] Open(string locked64, byte[] key)
    {
        var bytes = locked64.FromBase64();
        return AesGcm.Decrypt(bytes[IvLength..], key.ToSensitiveByteArray(), bytes[..IvLength]);
    }

    /// <summary>A P-384 key pair as the browser exports it: the private half PKCS8, the public half a JWK.</summary>
    public sealed record KeyPair(byte[] PrivateKeyPkcs8, string PublicKeyJwk);

    public static KeyPair MakeKeyPair()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
        var q = ecdh.ExportParameters(false).Q;
        var jwk = JsonSerializer.Serialize(new
        {
            kty = "EC", crv = "P-384", x = Base64UrlEncoder.Encode(q.X!), y = Base64UrlEncoder.Encode(q.Y!)
        });
        return new KeyPair(ecdh.ExportPkcs8PrivateKey(), jwk);
    }

    /// <summary>Seals <paramref name="data"/> to a public key (<see cref="ZkSeal"/>): only its private half opens it.</summary>
    public static string Seal(byte[] data, string publicKeyJwk) => ZkSeal.Seal(data, publicKeyJwk).ToBase64();

    /// <summary>Opens a value <see cref="ZkSeal"/> made, as the browser does; the server never opens one.</summary>
    public static byte[] Unseal(string sealed64, byte[] privateKeyPkcs8) => Unseal(sealed64.FromBase64(), privateKeyPkcs8);

    public static byte[] Unseal(byte[] sealedValue, byte[] privateKeyPkcs8)
    {
        Assert.That(ZkSeal.IsSealed(sealedValue), Is.True, "not a version 1 sealed value");
        using var me = ECDiffieHellman.Create();
        me.ImportPkcs8PrivateKey(privateKeyPkcs8, out _);
        const int x = ZkSeal.PublicKeyOffset + 1;
        const int y = x + ZkSeal.CoordinateLength;
        using var throwaway = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP384,
            Q = new ECPoint { X = sealedValue[x..y], Y = sealedValue[y..(y + ZkSeal.CoordinateLength)] }
        });
        var locked = sealedValue[(ZkSeal.PublicKeyOffset + ZkSeal.PublicKeyLength)..];
        return Open(locked.ToBase64(), ZkSeal.DeriveKey(me, throwaway.PublicKey));
    }

    public sealed record Keys(byte[] MasterKey, ZkKeysRequest Request);

    /// <summary>
    /// Makes the keys at first login, or re-locks <paramref name="masterKey"/> for a password change,
    /// sealing the password to the phrase's and the friends' public keys once there are any.
    /// </summary>
    public static Keys MakeKeys(string password, byte[]? masterKey = null, string? phrasePublicKeyJwk = null,
        string? shamirPublicKeyJwk = null)
    {
        var salt64 = ByteArrayUtil.GetRndByteArray(16).ToBase64();
        masterKey ??= ByteArrayUtil.GetRndByteArray(16);

        return new Keys(masterKey, new ZkKeysRequest
        {
            ZkSaltKek64 = salt64,
            ZkKekEncryptedZkMasterKey = Lock(masterKey, SecondPasswordKey(password, salt64)),
            PhrasePublicKeyEncryptedPassword = phrasePublicKeyJwk == null ? null : SealPassword(password, phrasePublicKeyJwk),
            ShamirPublicKeyEncryptedPassword = shamirPublicKeyJwk == null ? null : SealPassword(password, shamirPublicKeyJwk)
        });
    }

    public static string SealPassword(string password, string phrasePublicKeyJwk) =>
        Seal(password.ToUtf8ByteArray(), phrasePublicKeyJwk);

    /// <summary>The recovery phrase's 16 bytes, from its 12 words.</summary>
    public static byte[] PhraseKey(string phrase) => BIP39Util.DecodeBIP39(phrase).GetKey();

    /// <summary>The reset key: hkdf-sha256 of the recovery phrase's 16 bytes, no salt, 16 bytes.</summary>
    public static byte[] ResetKey(byte[] phraseKey) => Hkdf16(phraseKey, "homebase-recovery-reset-key");

    /// <summary>hkdf-sha256, no salt, 16 bytes: how the browser derives a reset key from a secret.</summary>
    public static byte[] Hkdf16(byte[] secret, string info) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 16, [], info.ToUtf8ByteArray());

    /// <summary>
    /// What the browser makes from the phrase: the zero-knowledge master key locked by the phrase, the
    /// phrase locked by the zero-knowledge master key, a new key pair with its private half locked by
    /// the phrase, the phrase's check value and the reset key.
    /// </summary>
    public static T MakePhraseCopies<T>(string phrase, byte[] zkMasterKey) where T : ZkPhraseCopiesRequest, new()
    {
        var phraseKey = PhraseKey(phrase);
        var pair = MakeKeyPair();
        return new T
        {
            PhraseKeyEncryptedZkMasterKey = Lock(zkMasterKey, phraseKey),
            ZkMasterKeyEncryptedPhraseKey = Lock(phraseKey, zkMasterKey),
            PhrasePublicKeyJwk = pair.PublicKeyJwk,
            PhraseKeyEncryptedPhrasePrivateKey = Lock(pair.PrivateKeyPkcs8, phraseKey),
            PhraseCheck64 = RecoveryPhraseCheck.Compute(phraseKey),
            ResetKey64 = ResetKey(phraseKey).ToBase64()
        };
    }

    public static ZkPhraseCopiesRequest MakePhraseCopies(string phrase, byte[] zkMasterKey) =>
        MakePhraseCopies<ZkPhraseCopiesRequest>(phrase, zkMasterKey);

    /// <summary>What the forgot-password page gets out of the phrase: the zero-knowledge master key.</summary>
    public static byte[] RecoverZkMasterKey(string phrase, ZkPhraseRecoveryResponse recovery) =>
        Open(recovery.PhraseKeyEncryptedZkMasterKey, PhraseKey(phrase));

    /// <summary>
    /// What the forgot-password page does with the phrase: the phrase's private key, then the
    /// password sealed to it.
    /// </summary>
    public static string RecoverPassword(string phrase, ZkPhraseRecoveryResponse recovery)
    {
        var privateKey = Open(recovery.PhraseKeyEncryptedPhrasePrivateKey, PhraseKey(phrase));
        return Unseal(recovery.PhrasePublicKeyEncryptedPassword, privateKey).ToStringFromUtf8Bytes();
    }

    public static byte[] OpenMasterKey(string password, ZkStatusResponse status)
    {
        return Open(status.ZkKekEncryptedZkMasterKey, SecondPasswordKey(password, status.ZkSaltKek64));
    }

    public sealed record DriveKeys(byte[] DriveKey, byte[] WriteOnlyPrivateKey, string WriteOnlyPublicKeyJwk,
        ZkConvertDriveRequest Request);

    /// <summary>A new drive key and write-only key pair, as the browser makes them to convert a drive.</summary>
    public static DriveKeys MakeDriveKeys(Guid driveId, byte[] zkMasterKey)
    {
        var driveKey = ByteArrayUtil.GetRndByteArray(16);
        var (idIv, idCipher) = AesCbc.Encrypt(driveId.ToByteArray(), driveKey.ToSensitiveByteArray());
        var pair = MakeKeyPair();

        return new DriveKeys(driveKey, pair.PrivateKeyPkcs8, pair.PublicKeyJwk, new ZkConvertDriveRequest
        {
            ZkMasterKeyEncryptedStorageKey = Lock(driveKey, zkMasterKey),
            EncryptedIdIv64 = idIv.ToBase64(),
            EncryptedIdValue64 = idCipher.ToBase64(),
            WriteOnlyPublicKeyJwk = pair.PublicKeyJwk,
            ZkStorageKeyEncryptedWriteOnlyPrivateKey = Lock(pair.PrivateKeyPkcs8, driveKey)
        });
    }

    /// <summary>
    /// A drive's write-only private key, opened as the browser opens it: the zero-knowledge master key
    /// opens the drive key, which opens the private key.
    /// </summary>
    public static byte[] OpenWriteOnlyPrivateKey(string zkMasterKeyEncryptedStorageKey, string zkStorageKeyEncryptedWriteOnlyPrivateKey,
        byte[] zkMasterKey) =>
        Open(zkStorageKeyEncryptedWriteOnlyPrivateKey, Open(zkMasterKeyEncryptedStorageKey, zkMasterKey));

    /// <summary>A new file key, sealed to <paramref name="publicKeyJwk"/> as a deposit's key header.</summary>
    public static (KeyHeader FileKey, EncryptedKeyHeader Sealed) SealNewFileKey(string publicKeyJwk)
    {
        var fileKey = KeyHeader.NewRandom16();
        return (fileKey, EncryptedKeyHeader.Sealed(fileKey.Iv, ZkSeal.Seal(fileKey.Combine().GetKey(), publicKeyJwk)));
    }

    /// <summary>
    /// A deposit's content as the drive owner's browser opens it: the key header came back sealed, as
    /// stored, and the drive's write-only private key opens it.
    /// </summary>
    public static byte[] OpenDeposit(SharedSecretEncryptedFileHeader header, byte[] writeOnlyPrivateKey)
    {
        Assert.That(header.FileMetadata.IsEncrypted, Is.True, "a deposit is encrypted");
        Assert.That(header.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Sealed),
            "a deposit's key header comes back sealed, as stored");
        var fileKey = KeyHeader.FromCombinedBytes(Unseal(header.SharedSecretEncryptedKeyHeader.EncryptedAesKey, writeOnlyPrivateKey));
        return fileKey.Decrypt(header.FileMetadata.AppData.Content.FromBase64());
    }
}
