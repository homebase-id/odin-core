#nullable enable
using System;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Services.Security.ZeroKnowledge;
using AesGcm = Odin.Core.Cryptography.Crypto.AesGcm;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// The browser's zero-knowledge key maths, in C#, the way <c>OwnerPasswordFlow</c> copies its login
/// maths.  The odin-js code must produce values this opens, and open what this makes.
/// </summary>
/// <remarks>
/// A locked value is base64 of a 12-byte IV followed by the AES-GCM ciphertext and its 16-byte tag.
/// Keys are 16 bytes, like today's master key and drive keys.  A sealed value is base64 of a
/// throwaway P-384 public key (97 bytes, uncompressed) and a value locked by
/// hkdf-sha256(ECDH(throwaway, recipient), "homebase-zk-seal").
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

    private const int SealPublicKeyLength = 97;
    private static readonly byte[] SealInfo = "homebase-zk-seal".ToUtf8ByteArray();

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

    /// <summary>Seals <paramref name="data"/> to a public key: only its private half opens it.</summary>
    public static string Seal(byte[] data, string publicKeyJwk)
    {
        using var jwk = JsonDocument.Parse(publicKeyJwk);
        using var recipient = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP384,
            Q = new ECPoint
            {
                X = Base64UrlEncoder.Decode(jwk.RootElement.GetProperty("x").GetString()!),
                Y = Base64UrlEncoder.Decode(jwk.RootElement.GetProperty("y").GetString()!)
            }
        });
        using var throwaway = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
        var q = throwaway.ExportParameters(false).Q;
        var locked = Lock(data, SealKey(throwaway, recipient.PublicKey)).FromBase64();
        return ByteArrayUtil.Combine([0x04], q.X!, q.Y!, locked).ToBase64();
    }

    public static byte[] Unseal(string sealed64, byte[] privateKeyPkcs8)
    {
        var bytes = sealed64.FromBase64();
        using var me = ECDiffieHellman.Create();
        me.ImportPkcs8PrivateKey(privateKeyPkcs8, out _);
        using var throwaway = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP384,
            Q = new ECPoint { X = bytes[1..49], Y = bytes[49..SealPublicKeyLength] }
        });
        return Open(bytes[SealPublicKeyLength..].ToBase64(), SealKey(me, throwaway.PublicKey));
    }

    private static byte[] SealKey(ECDiffieHellman mine, ECDiffieHellmanPublicKey theirs) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, mine.DeriveRawSecretAgreement(theirs), 16, [], SealInfo);

    public sealed record Keys(byte[] MasterKey, ZkKeysRequest Request);

    /// <summary>
    /// Makes the keys at first login, or re-locks <paramref name="masterKey"/> for a password change,
    /// sealing the password to the phrase's public key once there is one.
    /// </summary>
    public static Keys MakeKeys(string password, byte[]? masterKey = null, string? phrasePublicKeyJwk = null)
    {
        var salt64 = ByteArrayUtil.GetRndByteArray(16).ToBase64();
        masterKey ??= ByteArrayUtil.GetRndByteArray(16);

        return new Keys(masterKey, new ZkKeysRequest
        {
            ZkSaltKek64 = salt64,
            ZkKekEncryptedZkMasterKey = Lock(masterKey, SecondPasswordKey(password, salt64)),
            PhrasePublicKeyEncryptedPassword = phrasePublicKeyJwk == null ? null : SealPassword(password, phrasePublicKeyJwk)
        });
    }

    public static string SealPassword(string password, string phrasePublicKeyJwk) =>
        Seal(password.ToUtf8ByteArray(), phrasePublicKeyJwk);

    /// <summary>The recovery phrase's 16 bytes, from its 12 words.</summary>
    public static byte[] PhraseKey(string phrase) => BIP39Util.DecodeBIP39(phrase).GetKey();

    /// <summary>The reset key: hkdf-sha256 of the recovery phrase's 16 bytes, no salt, 16 bytes.</summary>
    public static byte[] ResetKey(byte[] phraseKey) => HKDF.DeriveKey(HashAlgorithmName.SHA256, phraseKey, 16, [],
        "homebase-recovery-reset-key".ToUtf8ByteArray());

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

        // Stands in for the browser's P-384 pair: its PKCS8 private key and its public JWK.
        var pairKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
        var pair = new EccFullKeyData(pairKey, EccKeySize.P384, 1);
        var privateKey = pair.privateDerBase64(pairKey).FromBase64();

        return new DriveKeys(driveKey, privateKey, pair.PublicKeyJwk(), new ZkConvertDriveRequest
        {
            ZkMasterKeyEncryptedStorageKey = Lock(driveKey, zkMasterKey),
            EncryptedIdIv64 = idIv.ToBase64(),
            EncryptedIdValue64 = idCipher.ToBase64(),
            WriteOnlyPublicKeyJwk = pair.PublicKeyJwk(),
            ZkStorageKeyEncryptedWriteOnlyPrivateKey = Lock(privateKey, driveKey)
        });
    }
}
