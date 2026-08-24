using NUnit.Framework.Legacy;

namespace Odin.Core.Cryptography.Tests
{
    using NUnit.Framework;
    using Odin.Core.Cryptography.Pgp;
    using Org.BouncyCastle.Bcpg;
    using Org.BouncyCastle.Bcpg.OpenPgp;
    using Org.BouncyCastle.Crypto.Parameters;
    using System;
    using System.IO;
    using System.Linq;
    using System.Text;

    [TestFixture]
    public class TestOpenPgpKeyManagement
    {
        private const string UserId = "frodo@frodo.dotyou.cloud";

        /// <summary>
        /// The property that makes client-supplied entropy safe to accept: the seed is MIXED into
        /// the generator's existing OS-seeded state, not substituted for it. If it were
        /// substituted, a caller sending the same bytes twice — or a hostile caller sending
        /// chosen bytes — would determine the key.
        ///
        /// Asserted behaviourally rather than by reading BouncyCastle's source, so it fails loudly
        /// if a future version changes what SetSeed does.
        /// </summary>
        [Test]
        public void SeedIsAdditiveNotDeterministic()
        {
            var seed = new byte[64];
            for (var i = 0; i < seed.Length; i++)
            {
                seed[i] = (byte)i;
            }

            var first = OpenPgpKeyManagement.GenerateP384KeyMaterial(UserId, seed);
            var second = OpenPgpKeyManagement.GenerateP384KeyMaterial(UserId, seed);

            ClassicAssert.AreNotEqual(
                first.FingerprintHex,
                second.FingerprintHex,
                "the same additional seed must NOT determine the key - it is mixed in, not substituted");
        }

        /// <summary>A seeded key is a normal key: publishable, and it round-trips.</summary>
        [Test]
        public void SeededKeyIsStillPublishableAndUsable()
        {
            var seed = System.Text.Encoding.UTF8.GetBytes("shake entropy from a phone, whitened");

            var material = OpenPgpKeyManagement.GenerateP384KeyMaterial(UserId, seed);

            // Throws if there is no usable encryption subkey - the publish path's own check.
            var spki = OpenPgpKeyManagement.GetEncryptionSubkeySpkiDer(material.PublicCertificateArmored);
            ClassicAssert.IsTrue(spki.Length > 0);

            var plaintext = Encoding.UTF8.GetBytes("hello from a shaken key");
            var encrypted = OpenPgpKeyManagement.Encrypt(plaintext, material.PublicCertificateArmored);
            var decrypted = OpenPgpKeyManagement.Decrypt(encrypted, material.SecretKeyArmored);

            ClassicAssert.AreEqual(plaintext, decrypted);
        }

        /// <summary>No entropy from the client (desktop, web, or a declined shake) still works.</summary>
        [Test]
        public void GenerationWithoutASeedStillWorks()
        {
            var material = OpenPgpKeyManagement.GenerateP384KeyMaterial(UserId, null);
            ClassicAssert.IsTrue(material.FingerprintHex.Length > 0);
        }

        /// The secret key's checksum must actually validate.
        ///
        /// This is not theoretical: shipping <c>useSha1: true</c> alongside an unencrypted
        /// (SymmetricKeyAlgorithmTag.Null) keyring produced keys that GnuPG imported but whose
        /// material its agent then refused ("Checksum error"), and that Thunderbird's RNP refused
        /// outright -- i.e. keys no mail client could use, which is the entire point of generating
        /// them. The private key VALUE was intact throughout; only the trailing checksum was wrong.
        ///
        /// It has to be checked independently, because BouncyCastle reads its own malformed output
        /// back without complaint: ExtractPrivateKey succeeds on both the broken and the correct
        /// key, so any BC-only round-trip test passes while real clients reject the result.
        /// RFC 4880 5.5.3: with S2K usage 0, the two octets are the sum of the preceding secret
        /// material octets, mod 65536.
        /// </summary>
        [Test]
        public void SecretKeyChecksumValidatesPerRfc4880()
        {
            var material = OpenPgpKeyManagement.GenerateP384KeyMaterial(UserId);

            using var input = PgpUtilities.GetDecoderStream(
                new MemoryStream(Encoding.ASCII.GetBytes(material.SecretKeyArmored)));
            var ring = new PgpSecretKeyRing(input);

            var checkedKeys = 0;
            foreach (PgpSecretKey key in ring.GetSecretKeys())
            {
                var secretBody = FirstPacketBody(key.GetEncoded());
                var publicBody = FirstPacketBody(key.PublicKey.GetEncoded());

                // The secret packet is the public packet's fields, then the usage byte, then the
                // protected material -- so the public body's length locates the usage byte.
                var usage = secretBody[publicBody.Length];
                Assert.That(usage, Is.EqualTo(0),
                    "Unencrypted keyring must declare S2K usage 0");

                var material_ = secretBody[(publicBody.Length + 1)..];
                var stored = (material_[^2] << 8) | material_[^1];

                var computed = 0;
                for (var i = 0; i < material_.Length - 2; i++)
                {
                    computed += material_[i];
                }

                Assert.That(computed & 0xFFFF, Is.EqualTo(stored),
                    $"Secret key {key.KeyId:X} carries a checksum that does not validate -- " +
                    "GnuPG's agent and Thunderbird's RNP both reject such a key");
                checkedKeys++;
            }

            Assert.That(checkedKeys, Is.EqualTo(2), "Expected a primary key and an encryption subkey");
        }

        /// <summary>
        /// CANARY -- pins the BouncyCastle behaviour we steer around, so we find out if it changes.
        ///
        /// <see cref="OpenPgpKeyManagement.GenerateP384KeyMaterial"/> passes <c>useSha1: false</c>.
        /// That is the correct value for an unencrypted (Null-cipher) keyring rather than a hack,
        /// but the reason it MATTERS is a BouncyCastle defect: with <c>useSha1: true</c> BC still
        /// writes S2K usage 0 -- declaring a simple 2-octet checksum -- while storing the
        /// SHA-1-style value there. This test asserts that defect is still present.
        ///
        /// If this test FAILS, BouncyCastle has changed its behaviour. Nothing is broken by that,
        /// but go re-read the comment in GenerateP384KeyMaterial: its warning about useSha1: true
        /// may no longer be accurate, and this canary should be updated or deleted rather than
        /// silently "fixed". Verified against BouncyCastle 2.7 (2026-08-24).
        /// </summary>
        [Test]
        public void CanaryBouncyCastleStillMisframesTheChecksumWhenUseSha1IsSet()
        {
            var armored = GenerateSecretRingArmored(useSha1: true);

            using var input = PgpUtilities.GetDecoderStream(
                new MemoryStream(Encoding.ASCII.GetBytes(armored)));
            var ring = new PgpSecretKeyRing(input);

            var mismatches = 0;
            var total = 0;
            foreach (PgpSecretKey key in ring.GetSecretKeys())
            {
                var secretBody = FirstPacketBody(key.GetEncoded());
                var publicBody = FirstPacketBody(key.PublicKey.GetEncoded());
                if (secretBody[publicBody.Length] != 0)
                {
                    // BC now frames it as something other than "simple checksum" -- also a change
                    // worth noticing, and the assert below will report it.
                    continue;
                }

                var secretMaterial = secretBody[(publicBody.Length + 1)..];
                var stored = (secretMaterial[^2] << 8) | secretMaterial[^1];
                var computed = 0;
                for (var i = 0; i < secretMaterial.Length - 2; i++)
                {
                    computed += secretMaterial[i];
                }

                total++;
                if ((computed & 0xFFFF) != stored)
                {
                    mismatches++;
                }
            }

            Assert.That(mismatches, Is.EqualTo(total).And.GreaterThan(0),
                "BouncyCastle no longer misframes the secret-key checksum under useSha1: true. " +
                "Re-read the comment in GenerateP384KeyMaterial and update or delete this canary.");
        }

        /// <summary>
        /// The keyring construction from <see cref="OpenPgpKeyManagement.GenerateP384KeyMaterial"/>,
        /// with <c>useSha1</c> left open so the canary above can exercise the other branch. Kept
        /// deliberately close to production; if that generator changes shape, change this too.
        /// </summary>
        private static string GenerateSecretRingArmored(bool useSha1)
        {
            var random = new Org.BouncyCastle.Security.SecureRandom();
            var created = DateTime.UtcNow;

            var generator = new Org.BouncyCastle.Crypto.Generators.ECKeyPairGenerator();
            generator.Init(new ECKeyGenerationParameters(
                Org.BouncyCastle.Asn1.Sec.SecObjectIdentifiers.SecP384r1, random));
            var primary = generator.GenerateKeyPair();
            var encryption = generator.GenerateKeyPair();

            var primaryKeyPair = new PgpKeyPair(PublicKeyAlgorithmTag.ECDsa, primary, created);
            var encryptionKeyPair = new PgpKeyPair(PublicKeyAlgorithmTag.ECDH, encryption, created);

            var primarySubpackets = new PgpSignatureSubpacketGenerator();
            primarySubpackets.SetKeyFlags(false, PgpKeyFlags.CanCertify | PgpKeyFlags.CanSign);
            primarySubpackets.SetPreferredSymmetricAlgorithms(false, [(int)SymmetricKeyAlgorithmTag.Aes256]);
            primarySubpackets.SetPreferredHashAlgorithms(false, [(int)HashAlgorithmTag.Sha384]);

            var encryptionSubpackets = new PgpSignatureSubpacketGenerator();
            encryptionSubpackets.SetKeyFlags(false,
                PgpKeyFlags.CanEncryptCommunications | PgpKeyFlags.CanEncryptStorage);

            var keyRingGenerator = new PgpKeyRingGenerator(
                PgpSignature.PositiveCertification,
                primaryKeyPair,
                UserId,
                SymmetricKeyAlgorithmTag.Null,
                HashAlgorithmTag.Sha384,
                Array.Empty<char>(),
                useSha1,
                primarySubpackets.Generate(),
                null,
                random);
            keyRingGenerator.AddSubKey(
                encryptionKeyPair, encryptionSubpackets.Generate(), null, HashAlgorithmTag.Sha384);

            using var output = new MemoryStream();
            using (var armoredOut = new ArmoredOutputStream(output))
            {
                keyRingGenerator.GenerateSecretKeyRing().Encode(armoredOut);
            }

            return Encoding.ASCII.GetString(output.ToArray());
        }

        /// <summary>Body of the first (old-format) packet in <paramref name="encoded"/>.</summary>
        private static byte[] FirstPacketBody(byte[] encoded)
        {
            var lengthType = encoded[0] & 0x03;
            var headerLength = lengthType switch
            {
                0 => 2,
                1 => 3,
                2 => 5,
                _ => throw new InvalidOperationException("Indeterminate packet length is not expected here"),
            };
            var bodyLength = lengthType switch
            {
                0 => encoded[1],
                1 => (encoded[1] << 8) | encoded[2],
                _ => (encoded[1] << 24) | (encoded[2] << 16) | (encoded[3] << 8) | encoded[4],
            };
            return encoded[headerLength..(headerLength + bodyLength)];
        }
        [Test]
        public void GeneratedCertificateIsMinimalP384WithEncryptionSubkey()
        {
            var material = OpenPgpKeyManagement.GenerateP384KeyMaterial(UserId);

            ClassicAssert.IsTrue(material.PublicCertificateArmored.StartsWith("-----BEGIN PGP PUBLIC KEY BLOCK-----"));
            ClassicAssert.IsTrue(material.SecretKeyArmored.StartsWith("-----BEGIN PGP PRIVATE KEY BLOCK-----"));

            using var stream = new MemoryStream(Encoding.ASCII.GetBytes(material.PublicCertificateArmored));
            var ring = new PgpPublicKeyRing(PgpUtilities.GetDecoderStream(stream));

            var keys = ring.GetPublicKeys().Cast<PgpPublicKey>().ToList();
            ClassicAssert.AreEqual(2, keys.Count, "exactly primary + encryption subkey");

            var primary = keys.Single(k => k.IsMasterKey);
            var subkey = keys.Single(k => !k.IsMasterKey);

            ClassicAssert.AreEqual(PublicKeyAlgorithmTag.ECDsa, primary.Algorithm);
            ClassicAssert.AreEqual(PublicKeyAlgorithmTag.ECDH, subkey.Algorithm);
            ClassicAssert.IsTrue(subkey.IsEncryptionKey);
            ClassicAssert.IsFalse(primary.IsEncryptionKey);

            // Both keys are on P-384
            foreach (var key in keys)
            {
                var publicKeyParams = (ECPublicKeyParameters)key.GetKey();
                ClassicAssert.AreEqual(384, publicKeyParams.Parameters.Curve.FieldSize);
            }

            ClassicAssert.AreEqual(UserId, primary.GetUserIds().Cast<string>().Single());
            ClassicAssert.AreEqual(material.FingerprintHex, Convert.ToHexString(primary.GetFingerprint()));
        }

        [Test]
        public void EncryptDecryptRoundTripsThroughTheCertificate()
        {
            var material = OpenPgpKeyManagement.GenerateP384KeyMaterial(UserId);
            var plaintext = Encoding.UTF8.GetBytes("the eagles are coming");

            var message = OpenPgpKeyManagement.Encrypt(plaintext, material.PublicCertificateArmored);
            CollectionAssert.AreNotEqual(plaintext, message);

            var decrypted = OpenPgpKeyManagement.Decrypt(message, material.SecretKeyArmored);
            CollectionAssert.AreEqual(plaintext, decrypted);
        }

        [Test]
        public void DecryptWithWrongKeyThrows()
        {
            var alice = OpenPgpKeyManagement.GenerateP384KeyMaterial("alice@example.com");
            var bob = OpenPgpKeyManagement.GenerateP384KeyMaterial("bob@example.com");

            var message = OpenPgpKeyManagement.Encrypt([1, 2, 3], alice.PublicCertificateArmored);

            Assert.Throws<ArgumentException>(() => OpenPgpKeyManagement.Decrypt(message, bob.SecretKeyArmored));
        }

        [Test]
        public void TamperedMessageFailsIntegrityVerification()
        {
            var material = OpenPgpKeyManagement.GenerateP384KeyMaterial(UserId);
            var message = OpenPgpKeyManagement.Encrypt(Encoding.UTF8.GetBytes("precious"), material.PublicCertificateArmored);

            // Flip a bit inside the encrypted payload (past the session-key packet)
            message[^10] ^= 0x01;

            Assert.Catch<Exception>(() => OpenPgpKeyManagement.Decrypt(message, material.SecretKeyArmored));
        }

        [Test]
        public void BinaryFormRoundTripsThroughArmor()
        {
            var material = OpenPgpKeyManagement.GenerateP384KeyMaterial(UserId);

            var binary = OpenPgpKeyManagement.GetPublicCertificateBinary(material.PublicCertificateArmored);
            ClassicAssert.IsTrue(binary.Length > 0);

            // The binary form parses as the same ring
            var ring = new PgpPublicKeyRing(new MemoryStream(binary));
            ClassicAssert.AreEqual(material.FingerprintHex, Convert.ToHexString(ring.GetPublicKey().GetFingerprint()));
            ClassicAssert.AreEqual(material.FingerprintHex, OpenPgpKeyManagement.GetFingerprintHex(material.PublicCertificateArmored));
        }

        [Test]
        public void EncryptionSubkeyExtractsAsP384SubjectPublicKeyInfo()
        {
            var material = OpenPgpKeyManagement.GenerateP384KeyMaterial(UserId);

            var spkiDer = OpenPgpKeyManagement.GetEncryptionSubkeySpkiDer(material.PublicCertificateArmored);
            var publicKeyParams = (ECPublicKeyParameters)Org.BouncyCastle.Security.PublicKeyFactory.CreateKey(spkiDer);

            ClassicAssert.AreEqual(384, publicKeyParams.Parameters.Curve.FieldSize);

            // And it is the subkey, not the primary: match against the ring's encryption key
            using var stream = new MemoryStream(Encoding.ASCII.GetBytes(material.PublicCertificateArmored));
            var ring = new PgpPublicKeyRing(PgpUtilities.GetDecoderStream(stream));
            var subkey = ring.GetPublicKeys().Cast<PgpPublicKey>().Single(k => !k.IsMasterKey);
            var subkeyParams = (ECPublicKeyParameters)subkey.GetKey();
            ClassicAssert.AreEqual(subkeyParams.Q, publicKeyParams.Q);
        }

        [Test]
        public void CertificateSatisfiesModernPolicyEngines()
        {
            // Live-verified against Stalwart 0.16 (sequoia-pgp): SHA-1-bound keys are
            // discarded wholesale and armor header lines break its parser. BC defaults
            // to both (hash-less ctor and 3-arg AddSubKey sign with SHA-1;
            // ArmoredOutputStream emits a Version header) - this pins the fixes.
            var material = OpenPgpKeyManagement.GenerateP384KeyMaterial(UserId);

            ClassicAssert.IsFalse(material.PublicCertificateArmored.Contains("Version:"),
                "armor must carry no headers");
            ClassicAssert.IsFalse(material.SecretKeyArmored.Contains("Version:"));

            using var stream = new MemoryStream(Encoding.ASCII.GetBytes(material.PublicCertificateArmored));
            var ring = new PgpPublicKeyRing(PgpUtilities.GetDecoderStream(stream));

            var primary = ring.GetPublicKeys().Cast<PgpPublicKey>().Single(k => k.IsMasterKey);
            var subkey = ring.GetPublicKeys().Cast<PgpPublicKey>().Single(k => !k.IsMasterKey);

            var certification = primary.GetSignatures().Cast<PgpSignature>().First();
            ClassicAssert.AreEqual(HashAlgorithmTag.Sha384, certification.HashAlgorithm,
                "user-id certification must not fall back to BC's SHA-1 default");

            var binding = subkey.GetSignatures().Cast<PgpSignature>().First();
            ClassicAssert.AreEqual(HashAlgorithmTag.Sha384, binding.HashAlgorithm,
                "subkey binding must not fall back to BC's SHA-1 default");
        }

        [Test]
        public void TwoGenerationsProduceDistinctKeys()
        {
            var first = OpenPgpKeyManagement.GenerateP384KeyMaterial(UserId);
            var second = OpenPgpKeyManagement.GenerateP384KeyMaterial(UserId);

            ClassicAssert.AreNotEqual(first.FingerprintHex, second.FingerprintHex);
        }

        [Test]
        public void SecretRingExtractsPrivateKeyWithoutPassphrase()
        {
            var material = OpenPgpKeyManagement.GenerateP384KeyMaterial(UserId);

            using var stream = new MemoryStream(Encoding.ASCII.GetBytes(material.SecretKeyArmored));
            var secretRing = new PgpSecretKeyRing(PgpUtilities.GetDecoderStream(stream));

            foreach (PgpSecretKey secretKey in secretRing.GetSecretKeys())
            {
                var privateKey = secretKey.ExtractPrivateKey([]);
                ClassicAssert.IsNotNull(privateKey);
            }
        }
    }
}
