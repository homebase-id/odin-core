using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using NUnit.Framework;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;

namespace DotYou.Kernel.CryptographyTests
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
            byte[] ba1 = YFByteArray.GetRndByteArray(40);
            byte[] ba2 = YFByteArray.GetRndByteArray(40);

            if (YFByteArray.EquiByteArrayCompare(ba1, ba2))
                Assert.Fail();

            var ra = YFByteArray.EquiByteArrayXor(ba1, ba2);

            if (YFByteArray.EquiByteArrayCompare(ra, ba1))
                Assert.Fail();

            if (YFByteArray.EquiByteArrayCompare(ra, ba2))
                Assert.Fail();

            var fa = YFByteArray.EquiByteArrayXor(ra, ba2);

            if (YFByteArray.EquiByteArrayCompare(fa, ba1))
                Assert.Pass();
            else
                Assert.Fail();
        }


        [Test]
        public void XorMgmtPass()
        {
            byte[] data = YFByteArray.GetRndByteArray(40);
            byte[] key = YFByteArray.GetRndByteArray(40);

            var cipher = XorManagement.XorEncrypt(data, key);
            var copyData = XorManagement.XorDecrypt(cipher, key);

            if (YFByteArray.EquiByteArrayCompare(data, copyData))
                Assert.Pass();
            else
                Assert.Fail();
        }

        [Test]
        public void RefreshKeyPass()
        {
            byte[] oldToken = YFByteArray.GetRndByteArray(40);
            byte[] key = YFByteArray.GetRndByteArray(40);

            var xorKeyOld = XorManagement.XorEncrypt(oldToken, key);

            byte[] newToken = YFByteArray.GetRndByteArray(40);

            var xorKeyNew = XorManagement.RefreshToken(oldToken, newToken, xorKeyOld);

            var copyKey = XorManagement.XorDecrypt(newToken, xorKeyNew);

            if (YFByteArray.EquiByteArrayCompare(key, copyKey))
                Assert.Pass();
            else
                Assert.Fail();
        }

        [Test]
        public void SplitKeyPass()
        {
            byte[] key = YFByteArray.GetRndByteArray(40);

            var (cipher, random)= XorManagement.XorSplitKey(key);

            var copyKey = XorManagement.XorDecrypt(cipher, random);

            if (YFByteArray.EquiByteArrayCompare(key, copyKey))
                Assert.Pass();
            else
                Assert.Fail();
        }


    }
}
