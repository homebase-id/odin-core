using System;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;

namespace Odin.Core.Cryptography.Tests
{
    public class TestEccKeyListManagement
    {
        [SetUp]
        public void Setup()
        {
        }


        [Test]
        public void TestGenerateNewKeyDefaultsPass()
        {
            var EccList = EccKeyListManagement.CreateEccKeyList(EccKeyListManagement.zeroSensitiveKey, 7, EccKeyListManagement.DefaultHoursOfflineKey);

            if (EccList.ListEcc.Count != 1)
                Assert.Fail();
            else
                Assert.Pass();
        }

        [Test]
        public void TestGenerateNewKeyIllegalMaxFail()
        {
            try
            {
                var EccList = EccKeyListManagement.CreateEccKeyList(EccKeyListManagement.zeroSensitiveKey, 0, EccKeyListManagement.DefaultHoursOfflineKey);
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
                var EccList = EccKeyListManagement.CreateEccKeyList(EccKeyListManagement.zeroSensitiveKey, 1, 23);
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
            var EccList = EccKeyListManagement.CreateEccKeyList(EccKeyListManagement.zeroSensitiveKey, 1, EccKeyListManagement.DefaultHoursOfflineKey);

            if (EccList.ListEcc.Count != 1)
                Assert.Fail();

            EccKeyListManagement.GenerateNewKey(EccKeyListManagement.zeroSensitiveKey, EccList, EccKeyListManagement.DefaultHoursOfflineKey);

            if (EccList.ListEcc.Count != 1)
                Assert.Fail();

            EccKeyListManagement.GenerateNewKey(EccKeyListManagement.zeroSensitiveKey, EccList, EccKeyListManagement.DefaultHoursOfflineKey);

            // Got to make this part of the code
            if (EccList.ListEcc.Count != 1)
                Assert.Fail();

            Assert.Pass();
        }

        [Test]
        public void TestGenerateNewKeyTwoPass()
        {
            var EccList = EccKeyListManagement.CreateEccKeyList(EccKeyListManagement.zeroSensitiveKey, 2, EccKeyListManagement.DefaultHoursOfflineKey);

            if (EccList.ListEcc.Count != 1)
                Assert.Fail();

            var crc1 = EccList.ListEcc[0].crc32c;

            if (EccKeyListManagement.FindKey(EccList, crc1) == null)
                Assert.Fail();

            if (EccKeyListManagement.FindKey(EccList, crc1+1) != null)
                Assert.Fail();

            EccKeyListManagement.GenerateNewKey(EccKeyListManagement.zeroSensitiveKey, EccList, EccKeyListManagement.DefaultHoursOfflineKey);

            if (EccList.ListEcc.Count != 2)
                Assert.Fail();

            var crc2 = EccList.ListEcc[0].crc32c;

            if (EccKeyListManagement.FindKey(EccList, crc1) == null)
                Assert.Fail();

            if (EccKeyListManagement.FindKey(EccList, crc2) == null)
                Assert.Fail();

            Assert.Pass();
        }


        [Test]
        public void TestEmptyList()
        {
            var EccList = EccKeyListManagement.CreateEccKeyList(EccKeyListManagement.zeroSensitiveKey, 7, EccKeyListManagement.DefaultHoursOfflineKey);
            EccList.ListEcc.Clear();

            if (EccList.ListEcc.Count != 0)
                Assert.Fail();

            if (EccKeyListManagement.FindKey(EccList, 42) != null)
                Assert.Fail();

            Assert.Pass();
        }
    }
}

