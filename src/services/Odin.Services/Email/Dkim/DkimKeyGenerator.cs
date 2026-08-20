using System;
using System.Collections.Generic;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace Odin.Services.Email.Dkim;

/// <summary>
/// Generates and exercises tenant DKIM keypairs. Selector layout is fixed by
/// docs/email-dns-plan.md: s1 = ed25519 (RFC 8463), s2 = rsa-2048 (legacy
/// receivers). Sign/Verify are the pair-proof primitives: the store uses them
/// to assert a decrypted private key still matches its public half, and the
/// monthly check signs a test vector against the live DNS TXT value.
/// </summary>
public static class DkimKeyGenerator
{
    public const string Ed25519Selector = "s1";
    public const string RsaSelector = "s2";

    public const int RsaKeySizeBits = 2048;

    /// <summary>Generates the full per-tenant selector set: s1 (ed25519) + s2 (rsa-2048).</summary>
    public static List<DkimKey> GenerateKeys()
    {
        return [GenerateEd25519Key(Ed25519Selector), GenerateRsaKey(RsaSelector)];
    }

    public static DkimKey GenerateEd25519Key(string selector)
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var keyPair = generator.GenerateKeyPair();

        return new DkimKey
        {
            Selector = selector,
            Algorithm = DkimAlgorithm.Ed25519,
            // RFC 8463: p= carries the raw public key, not a SubjectPublicKeyInfo
            PublicKey = ((Ed25519PublicKeyParameters)keyPair.Public).GetEncoded(),
            PrivateKeyPkcs8 = PrivateKeyInfoFactory.CreatePrivateKeyInfo(keyPair.Private).GetDerEncoded(),
        };
    }

    public static DkimKey GenerateRsaKey(string selector)
    {
        var generator = new RsaKeyPairGenerator();
        generator.Init(new KeyGenerationParameters(new SecureRandom(), RsaKeySizeBits));
        var keyPair = generator.GenerateKeyPair();

        return new DkimKey
        {
            Selector = selector,
            Algorithm = DkimAlgorithm.Rsa,
            // RFC 6376: p= carries the SubjectPublicKeyInfo DER
            PublicKey = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(keyPair.Public).GetDerEncoded(),
            PrivateKeyPkcs8 = PrivateKeyInfoFactory.CreatePrivateKeyInfo(keyPair.Private).GetDerEncoded(),
        };
    }

    /// <summary>Signs data with the key's private half (ed25519 or rsa-sha256 per the algorithm).</summary>
    public static byte[] Sign(DkimKey key, byte[] data)
    {
        var privateKey = PrivateKeyFactory.CreateKey(key.PrivateKeyPkcs8);
        var signer = CreateSigner(key.Algorithm);
        signer.Init(true, privateKey);
        signer.BlockUpdate(data, 0, data.Length);
        return signer.GenerateSignature();
    }

    /// <summary>Verifies a signature against a public key in DNS p= form (see <see cref="DkimKey.PublicKey"/>).</summary>
    public static bool Verify(DkimAlgorithm algorithm, byte[] publicKey, byte[] data, byte[] signature)
    {
        AsymmetricKeyParameter publicKeyParameters = algorithm switch
        {
            DkimAlgorithm.Ed25519 => new Ed25519PublicKeyParameters(publicKey),
            _ => PublicKeyFactory.CreateKey(publicKey),
        };

        var signer = CreateSigner(algorithm);
        signer.Init(false, publicKeyParameters);
        signer.BlockUpdate(data, 0, data.Length);
        return signer.VerifySignature(signature);
    }

    private static ISigner CreateSigner(DkimAlgorithm algorithm)
    {
        return algorithm switch
        {
            DkimAlgorithm.Ed25519 => SignerUtilities.GetSigner("Ed25519"),
            DkimAlgorithm.Rsa => SignerUtilities.GetSigner("SHA-256withRSA"),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
        };
    }
}
