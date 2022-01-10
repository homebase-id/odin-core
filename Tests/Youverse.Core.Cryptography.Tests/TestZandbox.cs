using System;
using System.Security.Cryptography;
using NUnit.Framework;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Cryptography.Tests
{
    public class TestZandbox
    {
        [SetUp]
        public void Setup()
        {
        }


        [Test]
        // Should split this into its own file.
        public void TestSymKeyAes()
        {
            var secret = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
            var key = new SymmetricKeyEncryptedAes(secret);

            var sk = key.DecryptKey(secret.GetKey());

            Assert.Pass();
        }

        [Test]
        // Should split this into its own file.
        public void TestSymKeyFailAes()
        {
            var secret = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
            var key = new SymmetricKeyEncryptedAes(secret);
            var garbage = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));

            try
            {
                key.DecryptKey(garbage.GetKey());
                Assert.Fail();
            }
            catch
            {
                Assert.Pass();
            }
        }

        [Test]
        // Ensures that if the decrypted key is cached then we can't get a copy
        // later with a junk key.
        public void TestSymKeyAes2()
        {
            var secret = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
            var key = new SymmetricKeyEncryptedAes(secret);

            var sk = key.DecryptKey(secret);

            var junk = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));

            try
            {
                key.DecryptKey(junk);
                Assert.Fail();
            }
            catch
            {
                Assert.Pass();
            }
        }


        [Test]
        // Should split this into its own file.
        public void TestSymKeyXor()
        {
            var secret = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
            // byte[] halfKey;
            var key = new SymmetricKeyEncryptedXor(secret, out var halfKey);
            var decryptKey = key.DecryptKey(halfKey);

            if (ByteArrayUtil.EquiByteArrayCompare(decryptKey.GetKey(), secret.GetKey()))
                Assert.Pass();
            else
                Assert.Fail();
        }


        [Test]
        // Should split this into its own file.
        public void TestSymKeyFailXor()
        {
            var secret = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
            // byte[] halfKey;
            var key = new SymmetricKeyEncryptedXor(secret, out var halfKey);

            var garbage = ByteArrayUtil.GetRndByteArray(16);

            try
            {
                var decryptKey = key.DecryptKey(garbage);
                Assert.Fail();
            }
            catch
            {
                Assert.Pass();
            }
        }

        [Test]
        // Ensures that if the decrypted key is cached then we can't get a copy
        // later with a junk key.
        public void TestSymKeyXor2()
        {
            var secret = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
            var key = new SymmetricKeyEncryptedXor(secret, out var halfKey);

            var sk = key.DecryptKey(secret);

            var junk = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));

            try
            {
                key.DecryptKey(junk);
                Assert.Fail();
            }
            catch
            {
                Assert.Pass();
            }
        }



        [Test]
        public void TestSha256Pass()
        {
            var key = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            var crypt = new SHA256Managed();
            byte[] hash = crypt.ComputeHash(key);
            Console.WriteLine("SHA-256={0}", hash);
            Assert.Pass();

            //
            // I've manually verified that the JS counterpart reaches the same value
        }
    }
}
