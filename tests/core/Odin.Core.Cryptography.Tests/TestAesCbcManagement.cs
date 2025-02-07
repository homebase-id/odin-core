using System;
using System.Text;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;

namespace Odin.Core.Cryptography.Tests
{
    public class TestAesCbcManagement
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

            var mysk = new SensitiveByteArray(key);

            var (IV, cipher) = AesCbc.Encrypt(Encoding.UTF8.GetBytes(testData), mysk);
            var roundtrip = Encoding.UTF8.GetString(AesCbc.Decrypt(cipher, mysk, IV));

            if (roundtrip == testData)
                Assert.Pass();
            else
                Assert.Fail();
        }

        [Test]
        public void AesCbcPass()
        {
            try
            {
                var key = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
                var iv = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
                var testData = new byte[] { 162, 146, 244, 255, 127, 128, 0, 42, 7, 0 };

                var mysk = key.ToSensitiveByteArray();

                var cipher = AesCbc.Encrypt(testData, mysk, iv);

                var s = ByteArrayUtil.PrintByteArray(cipher);
                Console.WriteLine("Cipher: " + s);
                var roundtrip = AesCbc.Decrypt(cipher, mysk, iv);

                if (ByteArrayUtil.EquiByteArrayCompare(roundtrip, testData) == false)
                    Assert.Fail();
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: {0}", e.Message);
                Assert.Fail();
            }
        }
    }
}
