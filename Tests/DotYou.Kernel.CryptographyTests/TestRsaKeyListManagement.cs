using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DotYou.Kernel.Cryptography;
using DotYou.Kernel.Services.Admin.Authentication;
using DotYou.Types.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using NUnit.Framework;

namespace DotYou.Kernel.CryptographyTests
{
    public class TestRsaKeyListManagement
    {
        [SetUp]
        public void Setup()
        {
        }


        [Test]
        public void TestGenerateNewKeyDefaultsPass()
        {
            var rsaList = RsaKeyListManagement.CreateRsaKeyList(7);

            if (rsaList.listRSA.Count != 1)
                Assert.Fail();
            else
                Assert.Pass();
        }

        [Test]
        public void TestGenerateNewKeyIllegalMaxFail()
        {
            try
            {
                var rsaList = RsaKeyListManagement.CreateRsaKeyList(0);
            }
            catch (Exception e)
            {
                Assert.Pass();
                return;
            }

            Assert.Fail();
        }

        [Test]
        public void TestGenerateNewKeyIllegalHoursFail()
        {
            try
            {
                var rsaList = RsaKeyListManagement.CreateRsaKeyList(1, 23);
            }
            catch (Exception e)
            {
                Assert.Pass();
                return;
            }

            Assert.Fail();
        }


        [Test]
        public void TestGenerateNewKeyPass()
        {
            var rsaList = RsaKeyListManagement.CreateRsaKeyList(1);

            if (rsaList.listRSA.Count != 1)
                Assert.Fail();

            RsaKeyListManagement.GenerateNewKey(rsaList, 24);

            if (rsaList.listRSA.Count != 1)
                Assert.Fail();

            RsaKeyListManagement.GenerateNewKey(rsaList, 24);

            // Got to make this part of the code
            if (rsaList.listRSA.Count != 1)
                Assert.Fail();

            Assert.Pass();
        }
    }
}

