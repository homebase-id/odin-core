using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using AesGcm = Odin.Core.Cryptography.Crypto.AesGcm;

namespace Odin.Services.Security.ZeroKnowledge;

/// <summary>
/// Sealing to a public key: anyone with the public key seals, only its private half opens
/// (docs/owner-keys.md, <c>seal</c>).  The browser seals and opens; the server only seals (the
/// recovery bundle) and reads which key a sealed value is for.
/// </summary>
/// <remarks>
/// <para>
/// Version 1, base64 of:
/// <c>0x01 ‖ key id (4 bytes, big-endian) ‖ throwaway public key (97 bytes) ‖ 12-byte IV ‖ AES-GCM ciphertext and its 16-byte tag</c>.
/// The AES key is hkdf-sha256(ECDH(throwaway, recipient), no salt, "homebase-zk-seal"), 16 bytes.
/// Public keys are P-384, uncompressed: <c>0x04 ‖ x ‖ y</c>.
/// </para>
/// <para>
/// The key id is <see cref="CRC32C.CalculateCRC32C"/> of the recipient's uncompressed public key, so a
/// holder can tell a value sealed to a key it no longer has.  Not the DER that
/// <c>EccPublicKeyData.crc32c</c> hashes: the same key has a different DER depending on where it came
/// from (BouncyCastle writes the curve's parameters out for a key read from a JWK), and the browser has
/// the raw point, not that DER.  Despite its name, <c>CalculateCRC32C</c> is the IEEE CRC-32 that zlib
/// computes; the browser computes the same.
/// </para>
/// </remarks>
public static class ZkSeal
{
    public const byte Version = 1;
    public const int PublicKeyLength = 97;
    public const int CoordinateLength = 48;
    private const int IvLength = 12;
    private const int TagLength = 16;

    /// <summary>Where the throwaway public key starts: after the version and the key id.</summary>
    public const int PublicKeyOffset = 1 + 4;

    private const int HeaderLength = PublicKeyOffset + PublicKeyLength;

    /// <summary>The shortest sealed value: an empty one.</summary>
    public const int MinLength = HeaderLength + IvLength + TagLength;

    private static readonly byte[] Info = "homebase-zk-seal".ToUtf8ByteArray();

    /// <summary>Seals <paramref name="data"/> to the P-384 public key <paramref name="publicKeyJwk"/>.</summary>
    public static byte[] Seal(byte[] data, string publicKeyJwk)
    {
        var recipientPoint = RawPublicKey(publicKeyJwk);
        using var recipient = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP384,
            Q = new ECPoint { X = recipientPoint[1..(1 + CoordinateLength)], Y = recipientPoint[(1 + CoordinateLength)..] }
        });
        using var throwaway = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
        var q = throwaway.ExportParameters(false).Q;

        using var key = DeriveKey(throwaway, recipient.PublicKey).ToSensitiveByteArray();
        var iv = ByteArrayUtil.GetRndByteArray(IvLength);
        var cipher = AesGcm.Encrypt(data, key, iv);

        var keyId = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(keyId, KeyId(recipientPoint));
        return ByteArrayUtil.Combine([Version], keyId, [0x04], q.X!, q.Y!, iv, cipher);
    }

    /// <summary>The key id of <paramref name="publicKeyJwk"/>, as a value sealed to it carries.</summary>
    public static uint KeyId(string publicKeyJwk) => KeyId(RawPublicKey(publicKeyJwk));

    private static uint KeyId(byte[] rawPublicKey) => CRC32C.CalculateCRC32C(0, rawPublicKey);

    /// <summary>Whether <paramref name="sealedValue"/> has the shape of a version 1 sealed value.</summary>
    public static bool IsSealed(byte[] sealedValue) =>
        sealedValue is { Length: >= MinLength } && sealedValue[0] == Version && sealedValue[5] == 0x04;

    /// <summary>Whether <paramref name="sealedValue"/> is a sealed value, sealed to <paramref name="publicKeyJwk"/>.</summary>
    public static bool IsSealedTo(byte[] sealedValue, string publicKeyJwk) =>
        IsSealed(sealedValue) && ReadKeyId(sealedValue) == KeyId(publicKeyJwk);

    /// <summary>Which public key <paramref name="sealedValue"/> was sealed to.</summary>
    public static uint ReadKeyId(byte[] sealedValue)
    {
        if (!IsSealed(sealedValue))
        {
            throw new OdinClientException("Not a sealed value", OdinClientErrorCode.ArgumentError);
        }

        return BinaryPrimitives.ReadUInt32BigEndian(sealedValue.AsSpan(1, 4));
    }

    /// <summary>The hkdf key both sides derive; public so test code opens what this seals.</summary>
    public static byte[] DeriveKey(ECDiffieHellman mine, ECDiffieHellmanPublicKey theirs) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, mine.DeriveRawSecretAgreement(theirs), 16, [], Info);

    /// <summary>The uncompressed point of a P-384 public key given as a JWK.</summary>
    public static byte[] RawPublicKey(string publicKeyJwk)
    {
        try
        {
            using var jwk = JsonDocument.Parse(publicKeyJwk);
            var root = jwk.RootElement;
            var x = Base64UrlEncoder.Decode(root.GetProperty("x").GetString()!);
            var y = Base64UrlEncoder.Decode(root.GetProperty("y").GetString()!);
            if (root.GetProperty("crv").GetString() == "P-384" && x.Length == CoordinateLength && y.Length == CoordinateLength)
            {
                return ByteArrayUtil.Combine([0x04], x, y);
            }
        }
        catch (Exception e) when (e is not OdinClientException)
        {
            // falls through: anything malformed is the caller's
        }

        throw new OdinClientException("Not a P-384 public key", OdinClientErrorCode.ArgumentError);
    }
}
