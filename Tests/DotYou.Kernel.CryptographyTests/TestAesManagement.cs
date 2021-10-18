using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using NUnit.Framework;
using Youverse.Core.Cryptography.Crypto;

namespace DotYou.Kernel.CryptographyTests
{
    public class TestAesManagement
    {
        [SetUp]
        public void Setup()
        {
        }

        //
        // ===== AES CBC TESTS =====
        //

        [Test]
        public void AesCbcTextPass()
        {
            var key = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            string testData = "The quick red fox";

            var (IV, cipher) = AesCbc.EncryptStringToBytes_Aes(testData, key);
            var roundtrip = AesCbc.DecryptStringFromBytes_Aes(cipher, key, IV);

            if (roundtrip == testData)
                Assert.Pass();
            else
                Assert.Fail();
        }

        [Test]
        public void AesCbcPass()
        {
            if (AesCbc.TestPrivate())
                Assert.Pass();
            else
                Assert.Fail();
        }


    }
}
