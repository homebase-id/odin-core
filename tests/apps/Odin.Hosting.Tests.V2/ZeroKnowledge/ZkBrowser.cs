#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Services.Security.ZeroKnowledge;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// The browser's zero-knowledge key maths, in C#, the way <c>OwnerPasswordFlow</c> copies its login
/// maths.  The odin-js code must produce values this opens, and open what this makes.
/// </summary>
/// <remarks>
/// A locked value is base64 of a 12-byte IV followed by the AES-GCM ciphertext and its 16-byte tag.
/// Keys are 16 bytes, like today's master key and drive keys.
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

    public sealed record Keys(byte[] MasterKey, ZkKeysRequest Request);

    /// <summary>
    /// Makes the keys at first login, or re-locks <paramref name="masterKey"/> for a password change.
    /// </summary>
    public static Keys MakeKeys(string password, byte[]? masterKey = null)
    {
        var salt64 = ByteArrayUtil.GetRndByteArray(16).ToBase64();
        masterKey ??= ByteArrayUtil.GetRndByteArray(16);

        return new Keys(masterKey, new ZkKeysRequest
        {
            ZkSaltKek64 = salt64,
            ZkKekEncryptedZkMasterKey = Lock(masterKey, SecondPasswordKey(password, salt64)),
            ZkMasterKeyEncryptedPassword = LockPassword(password, masterKey)
        });
    }

    public static string LockPassword(string password, byte[] zkMasterKey) => Lock(password.ToUtf8ByteArray(), zkMasterKey);

    public static string OpenPassword(string locked64, byte[] zkMasterKey) => Open(locked64, zkMasterKey).ToStringFromUtf8Bytes();

    /// <summary>The recovery phrase's 16 bytes, from its 12 words.</summary>
    public static byte[] PhraseKey(string phrase) => BIP39Util.DecodeBIP39(phrase).GetKey();

    /// <summary>
    /// The browser's two phrase copies: the zero-knowledge master key locked by the phrase, and the
    /// phrase locked by the zero-knowledge master key.  With the check value when it's typed in again.
    /// </summary>
    public static ZkPhraseCopiesRequest MakePhraseCopies(string phrase, byte[] zkMasterKey, bool withCheck = false)
    {
        var phraseKey = PhraseKey(phrase);
        return new ZkPhraseCopiesRequest
        {
            PhraseKeyEncryptedZkMasterKey = Lock(zkMasterKey, phraseKey),
            ZkMasterKeyEncryptedPhraseKey = Lock(phraseKey, zkMasterKey),
            PhraseCheck64 = withCheck ? RecoveryPhraseCheck.Compute(phraseKey) : null
        };
    }

    /// <summary>What the forgot-password page does with the phrase: the zero-knowledge master key, then the password.</summary>
    public static string RecoverPassword(string phrase, ZkPhraseRecoveryResponse recovery)
    {
        var zkMasterKey = Open(recovery.PhraseKeyEncryptedZkMasterKey, PhraseKey(phrase));
        return OpenPassword(recovery.ZkMasterKeyEncryptedPassword, zkMasterKey);
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
