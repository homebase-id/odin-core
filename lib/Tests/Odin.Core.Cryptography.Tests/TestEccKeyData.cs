﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Odin.Core.Cryptography.Tests
{
    using NUnit.Framework;
    using Odin.Core.Cryptography.Crypto;
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
        public void TestEccExample()
        {
            // We have two hobbits, they each have an ECC key
            SensitiveByteArray pwdFrodo = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData fullKeyFrodo = new EccFullKeyData(pwdFrodo, 2);

            SensitiveByteArray pwdSam = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData fullKeySam = new EccFullKeyData(pwdSam, 2);

            byte[] message = "Hello World".ToUtf8ByteArray();

            //
            // Frodo wants to send a message to Sam
            //

            // First Frodo makes a RANDOM salt (important)
            var randomSalt = ByteArrayUtil.GetRndByteArray(16);

            // Now Frodo calculates the shared secret based on the random salt
            var sharedSecretFrodo = fullKeyFrodo.GetEcdhSharedSecret(pwdFrodo, (EccPublicKeyData)fullKeySam, randomSalt);

            // Now we AES encrypt the message with the sharedSecret
            var (iv, cipher) = AesCbc.Encrypt(message, ref sharedSecretFrodo);

            //
            // NOW WE SEND THE DATA TO SAM
            // {randomSalt, iv, cipher}
            // we could include Frodo's public key if Sam doesn't already have it
            //

            // Sam decrypts

            // Get the shared secret from both sides
            var sharedSecretSam = fullKeySam.GetEcdhSharedSecret(pwdSam, (EccPublicKeyData)fullKeyFrodo, randomSalt);

            // Decrypt the message
            var originalBytes = AesCbc.Decrypt(cipher, ref sharedSecretSam, iv);

            if (originalBytes.ToStringFromUtf8Bytes() != message.ToStringFromUtf8Bytes())
                throw new Exception("It doesn't work");

            // The shared secrets are identical
            Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(sharedSecretFrodo.GetKey(), sharedSecretSam.GetKey()));
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
        public void TestGetSharedSecret3()
        {
            // Generate a pair of ECC keys
            SensitiveByteArray keyApwd = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData fullKeyA = new EccFullKeyData(keyApwd, 2);

            SensitiveByteArray keyBpwd = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData fullKeyB = new EccFullKeyData(keyBpwd, 2);

            var salt = ByteArrayUtil.GetRndByteArray(16);

            // Get the shared secret from both sides
            var sharedSecretA = fullKeyA.GetEcdhSharedSecret(keyApwd, (EccPublicKeyData) fullKeyB, salt);
            var sharedSecretB = fullKeyB.GetEcdhSharedSecret(keyBpwd, (EccPublicKeyData) fullKeyA, salt);

            // The shared secrets should be identical
            Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(sharedSecretA.GetKey(), sharedSecretB.GetKey()));
        }
    }
}
