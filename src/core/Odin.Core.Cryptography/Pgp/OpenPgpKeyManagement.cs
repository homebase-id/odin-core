using System;
using System.IO;
using System.Text;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace Odin.Core.Cryptography.Pgp;

/// <summary>
/// Minimal OpenPGP packaging for the tenant E2E email keypair (docs/email-keys-plan.md).
/// P-384 (secp384r1) throughout - the platform's existing ECC curve (EccKeyData), also
/// covered by OpenPGP RFC 6637 - as one ECDSA primary key (certify/sign) plus one ECDH
/// encryption subkey, a single user id, and no extra packets.
///
/// Consumers: WKD and DID keyAgreement publish the public certificate; the mail server
/// receives it for encryption-at-rest; the encrypt/decrypt pair below is the primitive
/// behind the owner-console round-trip check.
/// </summary>
public static class OpenPgpKeyManagement
{
    private const string CurveName = "secp384r1";

    /// <summary>
    /// Generates a fresh P-384 keypair packaged as a minimal OpenPGP certificate.
    /// </summary>
    /// <param name="userId">
    /// The OpenPGP user id. Must contain the tenant's email address for WKD lookups to
    /// match, e.g. "michael@michael.seifert.page".
    /// </param>
    public static OpenPgpKeyMaterial GenerateP384KeyMaterial(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId, nameof(userId));

        var random = new SecureRandom();
        var created = DateTime.UtcNow;

        var generator = new ECKeyPairGenerator();
        generator.Init(new ECKeyGenerationParameters(SecObjectIdentifiers.SecP384r1, random));
        var primary = generator.GenerateKeyPair();
        var encryption = generator.GenerateKeyPair();

        var primaryKeyPair = new PgpKeyPair(PublicKeyAlgorithmTag.ECDsa, primary, created);
        var encryptionKeyPair = new PgpKeyPair(PublicKeyAlgorithmTag.ECDH, encryption, created);

        var primarySubpackets = new PgpSignatureSubpacketGenerator();
        primarySubpackets.SetKeyFlags(false, PgpKeyFlags.CanCertify | PgpKeyFlags.CanSign);
        primarySubpackets.SetPreferredSymmetricAlgorithms(false, [(int)SymmetricKeyAlgorithmTag.Aes256]);
        primarySubpackets.SetPreferredHashAlgorithms(false, [(int)HashAlgorithmTag.Sha384]);

        var encryptionSubpackets = new PgpSignatureSubpacketGenerator();
        encryptionSubpackets.SetKeyFlags(false, PgpKeyFlags.CanEncryptCommunications | PgpKeyFlags.CanEncryptStorage);

        // SymmetricKeyAlgorithmTag.Null: the secret keyring is stored on the encrypted
        // email drive, which is the protection layer - see OpenPgpKeyMaterial.
        // HashAlgorithmTag.Sha384 for the self/binding signatures: BC's hash-less
        // ctor falls back to SHA-1, which modern policy engines (sequoia's
        // StandardPolicy, live-verified via Stalwart 0.16) reject wholesale -
        // the certificate parses but "has no suitable keys"
        var keyRingGenerator = new PgpKeyRingGenerator(
            PgpSignature.PositiveCertification,
            primaryKeyPair,
            userId,
            SymmetricKeyAlgorithmTag.Null,
            HashAlgorithmTag.Sha384,
            Array.Empty<char>(),
            // useSha1: MUST be false while the cipher is Null. BouncyCastle writes the S2K usage
            // byte as 0 either way -- which per RFC 4880 5.5.3 declares "simple 2-octet checksum"
            // -- but with useSha1: true it stores the SHA-1-style value there instead. The result
            // is a secret key whose checksum does not validate: GnuPG imports it leniently but its
            // agent then refuses the material ("Checksum error"), and Thunderbird's RNP rejects the
            // import outright. BouncyCastle round-trips its own output happily, so only an
            // independent check catches this -- see SecretKeyChecksumValidatesPerRfc4880.
            //
            // Unrelated to the SHA-384 signature hashes above: that is the self/binding signature
            // digest (a policy concern), this is the secret-key-material checksum.
            false,
            primarySubpackets.Generate(),
            null,
            random);

        // The 3-arg AddSubKey overload hardcodes SHA-1 for the binding signature
        // (BC 2.7) - same policy rejection as above, so the hash is explicit here too
        keyRingGenerator.AddSubKey(encryptionKeyPair, encryptionSubpackets.Generate(), null, HashAlgorithmTag.Sha384);

        var publicRing = keyRingGenerator.GeneratePublicKeyRing();
        var secretRing = keyRingGenerator.GenerateSecretKeyRing();

        return new OpenPgpKeyMaterial
        {
            PublicCertificateArmored = Armor(publicRing.GetEncoded()),
            SecretKeyArmored = Armor(secretRing.GetEncoded()),
            FingerprintHex = Convert.ToHexString(publicRing.GetPublicKey().GetFingerprint()),
            CreatedUtc = created,
        };
    }

    /// <summary>
    /// Encrypts to the certificate's encryption subkey (AES-256, integrity-protected).
    /// This is what the mail server does with arriving plaintext, and one half of the
    /// owner-console round-trip check.
    /// </summary>
    public static byte[] Encrypt(byte[] plaintext, string publicCertificateArmored)
    {
        ArgumentNullException.ThrowIfNull(plaintext, nameof(plaintext));

        var encryptionKey = FindEncryptionKey(publicCertificateArmored);

        using var literalStream = new MemoryStream();
        var literalGenerator = new PgpLiteralDataGenerator();
        using (var output = literalGenerator.Open(literalStream, PgpLiteralData.Binary, "", plaintext.Length, DateTime.UtcNow))
        {
            output.Write(plaintext, 0, plaintext.Length);
        }

        var encryptedGenerator = new PgpEncryptedDataGenerator(SymmetricKeyAlgorithmTag.Aes256, withIntegrityPacket: true, new SecureRandom());
        encryptedGenerator.AddMethod(encryptionKey);

        using var messageStream = new MemoryStream();
        var literalBytes = literalStream.ToArray();
        using (var encryptedOut = encryptedGenerator.Open(messageStream, literalBytes.Length))
        {
            encryptedOut.Write(literalBytes, 0, literalBytes.Length);
        }

        return messageStream.ToArray();
    }

    /// <summary>
    /// Decrypts a message produced by <see cref="Encrypt"/> with the secret keyring.
    /// Client-side only in production (the server never holds the secret keyring) -
    /// the other half of the owner-console round-trip check.
    /// </summary>
    public static byte[] Decrypt(byte[] pgpMessage, string secretKeyArmored)
    {
        ArgumentNullException.ThrowIfNull(pgpMessage, nameof(pgpMessage));

        var secretRing = ParseSecretKeyRing(secretKeyArmored);

        using var messageStream = new MemoryStream(pgpMessage);
        var objectFactory = new PgpObjectFactory(PgpUtilities.GetDecoderStream(messageStream));

        var firstObject = objectFactory.NextPgpObject();
        var encryptedList = firstObject as PgpEncryptedDataList ?? objectFactory.NextPgpObject() as PgpEncryptedDataList;
        if (encryptedList == null)
        {
            throw new ArgumentException("Not an OpenPGP encrypted message", nameof(pgpMessage));
        }

        foreach (var encryptedObject in encryptedList.GetEncryptedDataObjects())
        {
            if (encryptedObject is not PgpPublicKeyEncryptedData encryptedData)
            {
                continue;
            }

            var secretKey = secretRing.GetSecretKey(encryptedData.KeyId);
            if (secretKey == null)
            {
                continue;
            }

            var privateKey = secretKey.ExtractPrivateKey(Array.Empty<char>());
            using var clearStream = encryptedData.GetDataStream(privateKey);
            var clearFactory = new PgpObjectFactory(clearStream);
            var literal = clearFactory.NextPgpObject() as PgpLiteralData
                          ?? throw new ArgumentException("Encrypted message does not contain literal data", nameof(pgpMessage));

            using var plaintextStream = new MemoryStream();
            literal.GetInputStream().CopyTo(plaintextStream);
            var plaintext = plaintextStream.ToArray();

            if (encryptedData.IsIntegrityProtected() && !encryptedData.Verify())
            {
                throw new InvalidOperationException("OpenPGP message failed integrity verification");
            }

            return plaintext;
        }

        throw new ArgumentException("No encrypted session key matches the secret keyring", nameof(pgpMessage));
    }

    /// <summary>
    /// The binary (de-armored) form of the public certificate - WKD serves this.
    /// </summary>
    public static byte[] GetPublicCertificateBinary(string publicCertificateArmored)
    {
        return ParsePublicKeyRing(publicCertificateArmored).GetEncoded();
    }

    /// <summary>
    /// The primary key fingerprint of an armored public certificate, uppercase hex.
    /// </summary>
    public static string GetFingerprintHex(string publicCertificateArmored)
    {
        return Convert.ToHexString(ParsePublicKeyRing(publicCertificateArmored).GetPublicKey().GetFingerprint());
    }

    /// <summary>
    /// The encryption subkey as a standard SubjectPublicKeyInfo DER (P-384 point) -
    /// the form non-OpenPGP consumers want, e.g. the DID document's keyAgreement JWK.
    /// </summary>
    public static byte[] GetEncryptionSubkeySpkiDer(string publicCertificateArmored)
    {
        var encryptionKey = FindEncryptionKey(publicCertificateArmored);
        return SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(encryptionKey.GetKey()).GetDerEncoded();
    }

    //

    private static PgpPublicKey FindEncryptionKey(string publicCertificateArmored)
    {
        var publicRing = ParsePublicKeyRing(publicCertificateArmored);
        foreach (PgpPublicKey key in publicRing.GetPublicKeys())
        {
            if (key.IsEncryptionKey && !key.IsMasterKey)
            {
                return key;
            }
        }

        throw new ArgumentException("Certificate has no encryption subkey", nameof(publicCertificateArmored));
    }

    private static PgpPublicKeyRing ParsePublicKeyRing(string armored)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(armored, nameof(armored));
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(armored));
        return new PgpPublicKeyRing(PgpUtilities.GetDecoderStream(stream));
    }

    private static PgpSecretKeyRing ParseSecretKeyRing(string armored)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(armored, nameof(armored));
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(armored));
        return new PgpSecretKeyRing(PgpUtilities.GetDecoderStream(stream));
    }

    private static string Armor(byte[] encoded)
    {
        using var stream = new MemoryStream();
        // ClearHeaders drops the default "Version: BCPG ..." armor header: modern
        // OpenPGP practice omits it, and at least one consumer (Stalwart 0.16's
        // certificate parser, live-verified) treats header lines as base64 and
        // rejects the armor
        using (var armoredStream = ArmoredOutputStream.Build().ClearHeaders().Build(stream))
        {
            armoredStream.Write(encoded, 0, encoded.Length);
        }

        return Encoding.ASCII.GetString(stream.ToArray());
    }
}
