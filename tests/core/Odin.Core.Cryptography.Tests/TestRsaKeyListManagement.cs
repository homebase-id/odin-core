using System;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;

namespace Odin.Core.Cryptography.Tests
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
            var rsaList = RsaKeyListManagement.CreateRsaKeyList(RsaKeyListManagement.zeroSensitiveKey, 7, RsaKeyListManagement.DefaultHoursOfflineKey);

            if (rsaList.ListRSA.Count != 1)
                Assert.Fail();
            else
                Assert.Pass();
        }

        [Test]
        public void TestGenerateNewKeyIllegalMaxFail()
        {
            try
            {
                var rsaList = RsaKeyListManagement.CreateRsaKeyList(RsaKeyListManagement.zeroSensitiveKey, 0, RsaKeyListManagement.DefaultHoursOfflineKey);
            }
            catch (Exception)
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
                var rsaList = RsaKeyListManagement.CreateRsaKeyList(RsaKeyListManagement.zeroSensitiveKey, 1, 23);
            }
            catch (Exception)
            {
                Assert.Pass();
                return;
            }

            Assert.Fail();
        }


        [Test]
        public void TestGenerateNewKeyPass()
        {
            var rsaList = RsaKeyListManagement.CreateRsaKeyList(RsaKeyListManagement.zeroSensitiveKey, 1, RsaKeyListManagement.DefaultHoursOfflineKey);

            if (rsaList.ListRSA.Count != 1)
                Assert.Fail();

            RsaKeyListManagement.GenerateNewKey(RsaKeyListManagement.zeroSensitiveKey, rsaList, RsaKeyListManagement.DefaultHoursOfflineKey);

            if (rsaList.ListRSA.Count != 1)
                Assert.Fail();

            RsaKeyListManagement.GenerateNewKey(RsaKeyListManagement.zeroSensitiveKey, rsaList, RsaKeyListManagement.DefaultHoursOfflineKey);

            // Got to make this part of the code
            if (rsaList.ListRSA.Count != 1)
                Assert.Fail();

            Assert.Pass();
        }

        [Test]
        public void TestGenerateNewKeyTwoPass()
        {
            var rsaList = RsaKeyListManagement.CreateRsaKeyList(RsaKeyListManagement.zeroSensitiveKey, 2, RsaKeyListManagement.DefaultHoursOfflineKey);

            if (rsaList.ListRSA.Count != 1)
                Assert.Fail();

            var crc1 = rsaList.ListRSA[0].crc32c;

            if (RsaKeyListManagement.FindKey(rsaList, crc1) == null)
                Assert.Fail();

            if (RsaKeyListManagement.FindKey(rsaList, crc1+1) != null)
                Assert.Fail();

            RsaKeyListManagement.GenerateNewKey(RsaKeyListManagement.zeroSensitiveKey, rsaList, RsaKeyListManagement.DefaultHoursOfflineKey);

            if (rsaList.ListRSA.Count != 2)
                Assert.Fail();

            var crc2 = rsaList.ListRSA[0].crc32c;

            if (RsaKeyListManagement.FindKey(rsaList, crc1) == null)
                Assert.Fail();

            if (RsaKeyListManagement.FindKey(rsaList, crc2) == null)
                Assert.Fail();

            Assert.Pass();
        }


        [Test]
        public void TestEmptyList()
        {
            var rsaList = RsaKeyListManagement.CreateRsaKeyList(RsaKeyListManagement.zeroSensitiveKey, 7, RsaKeyListManagement.DefaultHoursOfflineKey);
            rsaList.ListRSA.Clear();

            if (rsaList.ListRSA.Count != 0)
                Assert.Fail();

            if (RsaKeyListManagement.FindKey(rsaList, 42) != null)
                Assert.Fail();

            Assert.Pass();
        }
    }
}

