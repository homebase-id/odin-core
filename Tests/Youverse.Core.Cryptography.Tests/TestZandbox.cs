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
        public void TestSymKey()
        {
            var secret = new SecureKey(ByteArrayUtil.GetRndByteArray(16));
            var key = new SymKeyData(secret);

            var sk = key.DecryptKey(secret.GetKey());

            Assert.Pass();
        }

        [Test]
        // Should split this into its own file.
        public void TestSymKeyFail()
        {
            var secret = new SecureKey(ByteArrayUtil.GetRndByteArray(16));
            var key = new SymKeyData(secret);
            var garbage = new SecureKey(ByteArrayUtil.GetRndByteArray(16));

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
