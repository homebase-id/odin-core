using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Odin.Core.Cryptography.Tests
{
    using NUnit.Framework;
    using Odin.Core.Cryptography.Data;
    using Org.BouncyCastle.Crypto;
    using Org.BouncyCastle.Crypto.Generators;
    using Org.BouncyCastle.Security;
    using Org.BouncyCastle.X509;
    using System;

    [TestFixture]
    public class TestEccKeyData
    {
        private SensitiveByteArray key;
        private byte[] testMessage;

        [SetUp]
        public void SetUp()
        {
            key = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            testMessage = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xaa };
        }

        [Test]
        public void EccPublicKeyDataTest()
        {
            // Generate an ECC key pair
            var generator = new ECKeyPairGenerator("ECDHC");
            generator.Init(new KeyGenerationParameters(new SecureRandom(), 384));
            var keyPair = generator.GenerateKeyPair();

            var publicKeyData = EccPublicKeyData.FromDerEncodedPublicKey(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(keyPair.Public).GetDerEncoded());

            Assert.IsNotNull(publicKeyData.publicKey);
            Assert.IsNotNull(publicKeyData.crc32c);
            Assert.IsNotNull(publicKeyData.expiration);
        }

        [Test]
        public void EccFullKeyDataTest()
        {
            // Generate a new full key
            var fullKeyData = new EccFullKeyData(key, 1);

            Assert.IsNotNull(fullKeyData.publicKey);
            Assert.IsNotNull(fullKeyData.storedKey);
            Assert.IsNotNull(fullKeyData.iv);
            Assert.IsNotNull(fullKeyData.keyHash);
            Assert.IsNotNull(fullKeyData.createdTimeStamp);

            // Sign a message
            var signature = fullKeyData.Sign(key, testMessage);

            Assert.IsNotNull(signature);

            // Verify the signature with the public key
            var publicKeyData = EccPublicKeyData.FromDerEncodedPublicKey(fullKeyData.publicKey);
            Assert.IsTrue(publicKeyData.VerifySignature(testMessage, signature));
        }

        [Test]
        public void TestGetSharedSecret()
        {
            // Generate a pair of ECC keys
            SensitiveByteArray keyApwd = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData fullKeyA = new EccFullKeyData(keyApwd, 2);

            SensitiveByteArray keyBpwd = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData fullKeyB = new EccFullKeyData(keyBpwd, 2);

            // Get the shared secret from both sides
            byte[] sharedSecretA = fullKeyA.GetSharedSecret(keyApwd, fullKeyB);
            byte[] sharedSecretB = fullKeyB.GetSharedSecret(keyBpwd, fullKeyA);

            // The shared secrets should be identical
            Assert.AreEqual(sharedSecretA, sharedSecretB);
        }
    }
}
