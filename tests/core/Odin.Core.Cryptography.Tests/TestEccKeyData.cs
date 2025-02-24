using NUnit.Framework.Legacy;

namespace Odin.Core.Cryptography.Tests
{
    using NUnit.Framework;
    using Odin.Core.Cryptography.Crypto;
    using Odin.Core.Cryptography.Data;
    using System;
    using System.Collections.Generic;
    using System.Text.Json;

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
        public void TestEcdh()
        {
            SensitiveByteArray pwdFrodo = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData fullKeyFrodo = new EccFullKeyData(pwdFrodo, EccKeySize.P256, 2);

            EccPublicKeyData pk = (EccPublicKeyData) fullKeyFrodo;
            var s = pk.GenerateEcdsaBase64Url();

            ClassicAssert.IsTrue(s != "");
            ClassicAssert.IsTrue(s != null);

            var ba = Base64UrlEncoder.Decode(s);
            ClassicAssert.IsTrue(ba.Length == 65);
        }

        [Test]
        public void TestJwkPublicKeyRepeat()
        {
            SensitiveByteArray pwdFrodo = new SensitiveByteArray(Guid.NewGuid().ToByteArray());


            for (int i=0; i < 256; i++)
            {
                EccFullKeyData fullKeyFrodo = new EccFullKeyData(pwdFrodo, EccKeySize.P384, 2);

                var jwk = fullKeyFrodo.PublicKeyJwk();
                var jwkObject = JsonSerializer.Deserialize<Dictionary<string, string>>(jwk);

                byte[] x = Base64UrlEncoder.Decode(jwkObject["x"]);
                byte[] y = Base64UrlEncoder.Decode(jwkObject["y"]);

                ClassicAssert.IsTrue(x.Length == 48);
                ClassicAssert.IsTrue(y.Length == 48);
            }
        }


        [Test]
        public void TestJwkPublicKey()
        {
            SensitiveByteArray pwdFrodo = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData fullKeyFrodo = new EccFullKeyData(pwdFrodo, EccKeySize.P384, 2);

            var jwk = fullKeyFrodo.PublicKeyJwk();

            var pk = EccPublicKeyData.FromJwkPublicKey(jwk);

            ClassicAssert.IsTrue(pk.PublicKeyJwk() == fullKeyFrodo.PublicKeyJwk());
            ClassicAssert.IsTrue(pk.PublicKeyJwkBase64Url() == fullKeyFrodo.PublicKeyJwkBase64Url());
        }

        [Test]
        public void TestJwkPublicKey256()
        {
            SensitiveByteArray pwdFrodo = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData fullKeyFrodo = new EccFullKeyData(pwdFrodo, EccKeySize.P256, 2);

            var jwk = fullKeyFrodo.PublicKeyJwk();

            var pk = EccPublicKeyData.FromJwkPublicKey(jwk);

            ClassicAssert.IsTrue(pk.PublicKeyJwk() == fullKeyFrodo.PublicKeyJwk());
            ClassicAssert.IsTrue(pk.PublicKeyJwkBase64Url() == fullKeyFrodo.PublicKeyJwkBase64Url());
        }

        [Test]
        public void TestJwkPublicKeyBase64()
        {
            SensitiveByteArray pwdFrodo = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData fullKeyFrodo = new EccFullKeyData(pwdFrodo, EccKeySize.P384, 2);

            var jwkBase64Url = fullKeyFrodo.PublicKeyJwkBase64Url();
            var pk = EccPublicKeyData.FromJwkBase64UrlPublicKey(jwkBase64Url);

            ClassicAssert.IsTrue(pk.PublicKeyJwk() == fullKeyFrodo.PublicKeyJwk());
        }

        [Test]
        public void TestJwkPublicKeyBase64_256()
        {
            SensitiveByteArray pwdFrodo = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData fullKeyFrodo = new EccFullKeyData(pwdFrodo, EccKeySize.P256, 2);

            var jwkBase64Url = fullKeyFrodo.PublicKeyJwkBase64Url();
            var pk = EccPublicKeyData.FromJwkBase64UrlPublicKey(jwkBase64Url);

            ClassicAssert.IsTrue(pk.PublicKeyJwk() == fullKeyFrodo.PublicKeyJwk());
        }

        [Test]
        public void TestJwkPublicEcdh()
        {
            SensitiveByteArray pwdFrodo = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData fullKeyFrodo = new EccFullKeyData(pwdFrodo, EccKeySize.P384, 2);

            SensitiveByteArray pwdSam = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData fullKeySam = new EccFullKeyData(pwdSam, EccKeySize.P384, 2);

            var jwkSam = fullKeySam.PublicKeyJwk();
            var publicKeySam = EccPublicKeyData.FromJwkPublicKey(jwkSam);

            var randomSalt = ByteArrayUtil.GetRndByteArray(16);
            var sharedSecretFrodo1 = fullKeyFrodo.GetEcdhSharedSecret(pwdFrodo, (EccPublicKeyData)fullKeySam, randomSalt);
            var sharedSecretFrodo2 = fullKeyFrodo.GetEcdhSharedSecret(pwdFrodo, publicKeySam, randomSalt);

            ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(sharedSecretFrodo1.GetKey(), sharedSecretFrodo2.GetKey()));
        }

        [Test]
        public void TestJwkPublicSignature()
        {
            SensitiveByteArray pwdSam = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData fullKeySam = new EccFullKeyData(pwdSam, EccKeySize.P384, 2);

            var signature = fullKeySam.Sign(pwdSam, testMessage);

            ClassicAssert.IsTrue(fullKeySam.VerifySignature(testMessage, signature));

            var jwkSam = fullKeySam.PublicKeyJwk();
            var publicKeySam = EccPublicKeyData.FromJwkPublicKey(jwkSam);

            ClassicAssert.IsTrue(publicKeySam.VerifySignature(testMessage, signature));
        }

        [Test]
        public void TestJwkPublicSignature256()
        {
            SensitiveByteArray pwdSam = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData fullKeySam = new EccFullKeyData(pwdSam, EccKeySize.P256, 2);

            var signature = fullKeySam.Sign(pwdSam, testMessage);

            ClassicAssert.IsTrue(fullKeySam.VerifySignature(testMessage, signature));

            var jwkSam = fullKeySam.PublicKeyJwk();
            var publicKeySam = EccPublicKeyData.FromJwkPublicKey(jwkSam);

            ClassicAssert.IsTrue(publicKeySam.VerifySignature(testMessage, signature));
        }


        /// <summary>
        /// This test shows two Hobbits that get a shared secret using ECC keys and a random salt,
        /// and encrypt a payload with the shared secret. The data transmitted is sent as:
        ///    { randomSalt, randomIv, cipher } or
        ///    { randomSalt, randomIv, cipher, publicKey } in case the recipient doesn't have the public key
        /// The shared secret is calculated on both sides via the GetEcdhSharedSecret() function.
        /// </summary>
        /// <exception cref="Exception"></exception>
        [Test]
        public void TestEccExample()
        {
            // We have two hobbits, they each have an ECC key
            SensitiveByteArray pwdFrodo = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData fullKeyFrodo = new EccFullKeyData(pwdFrodo, EccKeySize.P384, 2);

            SensitiveByteArray pwdSam = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData fullKeySam = new EccFullKeyData(pwdSam, EccKeySize.P384, 2);

            byte[] message = "Hello World".ToUtf8ByteArray();

            //
            // Frodo wants to send a message to Sam
            //

            // First Frodo makes a RANDOM salt (important)
            var randomSalt = ByteArrayUtil.GetRndByteArray(16);

            // Now Frodo calculates the shared secret based on the random salt
            var sharedSecretFrodo = fullKeyFrodo.GetEcdhSharedSecret(pwdFrodo, (EccPublicKeyData)fullKeySam, randomSalt);

            // Now we AES encrypt the message with the sharedSecret
            var (randomIv, cipher) = AesCbc.Encrypt(message, sharedSecretFrodo);

            //
            // NOW WE SEND THE DATA TO SAM
            // { randomSalt, randomIv, cipher }
            // we could include Frodo's public key if Sam doesn't already have it
            //

            // Sam decrypts

            // Get the shared secret from both sides
            var sharedSecretSam = fullKeySam.GetEcdhSharedSecret(pwdSam, (EccPublicKeyData)fullKeyFrodo, randomSalt);

            // Decrypt the message
            var originalBytes = AesCbc.Decrypt(cipher, sharedSecretSam, randomIv);

            if (originalBytes.ToStringFromUtf8Bytes() != message.ToStringFromUtf8Bytes())
                throw new Exception("It doesn't work");

            // The shared secrets are identical
            ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(sharedSecretFrodo.GetKey(), sharedSecretSam.GetKey()));
        }


        [Test]
        public void EccFullKeyDataTest()
        {
            // Generate a new full key
            var fullKeyData = new EccFullKeyData(key, EccKeySize.P384, 1);

            ClassicAssert.IsNotNull(fullKeyData.publicKey);
            ClassicAssert.IsNotNull(fullKeyData.storedKey);
            ClassicAssert.IsNotNull(fullKeyData.iv);
            ClassicAssert.IsNotNull(fullKeyData.keyHash);
            ClassicAssert.IsNotNull(fullKeyData.createdTimeStamp);

            // Sign a message
            var signature = fullKeyData.Sign(key, testMessage);

            ClassicAssert.IsNotNull(signature);

            // Verify the signature with the public key
            var publicKeyData = EccPublicKeyData.FromJwkBase64UrlPublicKey(fullKeyData.PublicKeyJwkBase64Url());
            ClassicAssert.IsTrue(publicKeyData.VerifySignature(testMessage, signature));
        }

        [Test]
        public void TestGetSharedSecret3()
        {
            // Generate a pair of ECC keys
            SensitiveByteArray keyApwd = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData fullKeyA = new EccFullKeyData(keyApwd, EccKeySize.P384, 2);

            SensitiveByteArray keyBpwd = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData fullKeyB = new EccFullKeyData(keyBpwd, EccKeySize.P384, 2);

            var salt = ByteArrayUtil.GetRndByteArray(16);

            // Get the shared secret from both sides
            var sharedSecretA = fullKeyA.GetEcdhSharedSecret(keyApwd, (EccPublicKeyData) fullKeyB, salt);
            var sharedSecretB = fullKeyB.GetEcdhSharedSecret(keyBpwd, (EccPublicKeyData) fullKeyA, salt);

            // The shared secrets should be identical
            ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(sharedSecretA.GetKey(), sharedSecretB.GetKey()));
        }
    }
}
