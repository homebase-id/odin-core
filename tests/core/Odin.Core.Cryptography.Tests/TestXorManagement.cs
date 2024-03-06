using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;

namespace Odin.Core.Cryptography.Tests
{
    public class TestXorManagement
    {
        [SetUp]
        public void Setup()
        {
        }

        /// <summary>
        /// Do a generic test of low level XOR management - move to Test YFByteArray class
        /// </summary>
        [Test]
        public void XorPass()
        {
            byte[] ba1 = ByteArrayUtil.GetRndByteArray(40);
            byte[] ba2 = ByteArrayUtil.GetRndByteArray(40);

            if (ByteArrayUtil.EquiByteArrayCompare(ba1, ba2))
                Assert.Fail();

            var ra = ByteArrayUtil.EquiByteArrayXor(ba1, ba2);

            if (ByteArrayUtil.EquiByteArrayCompare(ra, ba1))
                Assert.Fail();

            if (ByteArrayUtil.EquiByteArrayCompare(ra, ba2))
                Assert.Fail();

            var fa = ByteArrayUtil.EquiByteArrayXor(ra, ba2);

            if (ByteArrayUtil.EquiByteArrayCompare(fa, ba1))
                Assert.Pass();
            else
                Assert.Fail();
        }


        [Test]
        public void XorMgmtPass()
        {
            byte[] data = ByteArrayUtil.GetRndByteArray(40);
            byte[] key = ByteArrayUtil.GetRndByteArray(40);

            var cipher = XorManagement.XorEncrypt(data, key);
            var copyData = XorManagement.XorDecrypt(cipher, key);

            if (ByteArrayUtil.EquiByteArrayCompare(data, copyData))
                Assert.Pass();
            else
                Assert.Fail();
        }

        [Test]
        public void RefreshKeyPass()
        {
            byte[] oldToken = ByteArrayUtil.GetRndByteArray(40);
            byte[] key = ByteArrayUtil.GetRndByteArray(40);

            var xorKeyOld = XorManagement.XorEncrypt(oldToken, key);

            byte[] newToken = ByteArrayUtil.GetRndByteArray(40);

            var xorKeyNew = XorManagement.RefreshToken(oldToken, newToken, xorKeyOld);

            var copyKey = XorManagement.XorDecrypt(newToken, xorKeyNew);

            if (ByteArrayUtil.EquiByteArrayCompare(key, copyKey))
                Assert.Pass();
            else
                Assert.Fail();
        }

        [Test]
        public void SplitKeyPass()
        {
            byte[] key = ByteArrayUtil.GetRndByteArray(40);

            var (cipher, random)= XorManagement.XorSplitKey(key);

            var copyKey = XorManagement.XorDecrypt(cipher, random);

            if (ByteArrayUtil.EquiByteArrayCompare(key, copyKey))
                Assert.Pass();
            else
                Assert.Fail();
        }


    }
}
