using System.Text;
using NUnit.Framework;
using Odin.Core.Cryptography.Data;
using Odin.Core.Util;

namespace Odin.Core.Cryptography.Tests
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
            var key = new SymmetricKeyEncryptedAes(ref secret);

            var sk = key.DecryptKeyClone(ref secret);

            Assert.Pass();
        }

        [Test]
        // Should split this into its own file.
        public void TestSymKeyFailAes()
        {
            var secret = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
            var key = new SymmetricKeyEncryptedAes(ref secret);
            var garbage = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));

            try
            {
                key.DecryptKeyClone(ref garbage);
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
            var key = new SymmetricKeyEncryptedAes(ref secret);

            var sk = key.DecryptKeyClone(ref secret);

            var junk = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));

            try
            {
                key.DecryptKeyClone(ref junk);
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
            var key = new SymmetricKeyEncryptedXor(ref secret, out var halfKey);
            var decryptKey = key.DecryptKeyClone(ref halfKey);

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
            var key = new SymmetricKeyEncryptedXor(ref secret, out var halfKey);

            var garbage = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));

            try
            {
                var decryptKey = key.DecryptKeyClone(ref garbage);
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
            var key = new SymmetricKeyEncryptedXor(ref secret, out var halfKey);

            var sk = key.DecryptKeyClone(ref halfKey);

            var junk = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));

            try
            {
                key.DecryptKeyClone(ref junk);
                Assert.Fail();
            }
            catch
            {
                Assert.Pass();
            }
        }


        [Test]
        public void TestSha256BrainsPass()
        {
            // https://www.sciencedirect.com/topics/computer-science/encrypted-value
            var dataToEncrypt = Encoding.UTF8.GetBytes("brains");
            byte[] result = { 0x44, 0xde, 0x9b, 0x7b, 0x03, 0x6b, 0x9b, 0x8d, 0x28, 0xf3, 0x64, 0xfa, 0x36, 0x4b, 0x76, 0xb7, 0xaf, 0x64, 0xd9, 0xe0, 0xb9, 0xef, 0xe1, 0x7d, 0x75, 0x36, 0x03, 0x37, 0x72, 0xa0, 0x48, 0x71 };

            var r = HashUtil.CalculateSHA256Hash(dataToEncrypt);

            if (!ByteArrayUtil.EquiByteArrayCompare(result, r))
                Assert.Fail();

            Assert.Pass();

            //
            // I've manually verified that the JS counterpart reaches the same value
        }
    }
}
