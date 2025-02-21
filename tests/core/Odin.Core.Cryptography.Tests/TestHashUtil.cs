using System;
using System.Text;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Cryptography.Crypto;

namespace Odin.Core.Cryptography.Tests
{
    public class TestHashUtil
    {
        private byte[] _sharedEccSecret;
        private byte[] _salt;

        [SetUp]
        public void Setup()
        {
            _sharedEccSecret = Encoding.ASCII.GetBytes("ECCSecret");
            _salt = Encoding.ASCII.GetBytes("Salt");
        }

        [Test]
        public void Hkdf_SharedEccSecretIsNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => HashUtil.Hkdf(null, _salt, 16));
        }

        [Test]
        public void Hkdf_SaltIsNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => HashUtil.Hkdf(_sharedEccSecret, null, 16));
        }

        [Test]
        public void Hkdf_OutputKeySizeIsLessThanSixteen_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => HashUtil.Hkdf(_sharedEccSecret, _salt, 10));
        }

        [Test]
        public void Hkdf_ValidInput_ReturnsCorrectLengthKey()
        {
            var key = HashUtil.Hkdf(_sharedEccSecret, _salt, 32);

            ClassicAssert.AreEqual(32, key.Length);
        }

        [Test]
        public void Hkdf_DifferentInput_ReturnsDifferentKeys()
        {
            var key1 = HashUtil.Hkdf(_sharedEccSecret, _salt, 32);
            var key2 = HashUtil.Hkdf(Encoding.ASCII.GetBytes("DifferentSecret"), _salt, 32);

            ClassicAssert.AreNotEqual(key1, key2);
        }
    }
}
