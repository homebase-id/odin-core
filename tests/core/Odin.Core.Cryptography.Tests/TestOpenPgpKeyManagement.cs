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
