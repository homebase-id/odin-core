using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using AesGcm = Odin.Core.Cryptography.Crypto.AesGcm;

namespace Odin.Core.Cryptography.Tests
{
    public class TestAesGcmManagement
    {
        [SetUp]
        public void Setup()
        {
        }

        //
        // ===== AES GCM TESTS =====
        //

        [Test]
        public void AesGcmTextPass()
        {
            var key = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            string testData = "The quick red fox";

            var mysk = new SensitiveByteArray(key);

            var (IV, cipher) =  AesGcm.Encrypt(Encoding.UTF8.GetBytes(testData), mysk);
            var roundtrip = Encoding.UTF8.GetString(AesGcm.Decrypt(cipher, mysk, IV));

            if (roundtrip == testData)
                Assert.Pass();
            else
                Assert.Fail();
        }

        [Test]
        public void AesGcmPass()
        {
            try
            {
                var key = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
                var iv = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
                var testData = new byte[] { 162, 146, 244, 255, 127, 128, 0, 42, 7, 0 };

                var mysk = key.ToSensitiveByteArray();

                var cipher = AesGcm.Encrypt(testData, mysk, iv);

                var s = ByteArrayUtil.PrintByteArray(cipher);
                Console.WriteLine("Cipher: " + s);
                var roundtrip = AesGcm.Decrypt(cipher, mysk, iv);

                if (ByteArrayUtil.EquiByteArrayCompare(roundtrip, testData) == false)
                    Assert.Fail();
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: {0}", e.Message);
                Assert.Fail();
            }
        }

        [Test]
        public void AesGcmShortIvFail()
        {
            var key = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            var iv = new byte[] { 1, 2, 3, 4 }; // Too short
            var testData = Encoding.UTF8.GetBytes("Invalid IV test");

            var mysk = new SensitiveByteArray(key);

            Assert.Throws<ArgumentException>(() => AesGcm.Encrypt(testData, mysk, iv));
        }

        [Test]
        public void AesGcmNullInputsFail()
        {
            Assert.Throws<ArgumentNullException>(() => AesGcm.Encrypt(null, null, null));
        }

        [Test]
        public void AesGcmTagTamperingFails()
        {
            var key = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            var iv = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            var testData = Encoding.UTF8.GetBytes("Tamper test");

            var mysk = new SensitiveByteArray(key);
            var cipher = AesGcm.Encrypt(testData, mysk, iv);

            // Tamper with the tag (last 16 bytes)
            cipher[cipher.Length - 1] ^= 1;

            Assert.Throws<System.Security.Cryptography.AuthenticationTagMismatchException>(() => AesGcm.Decrypt(cipher, mysk, iv));
        }

        [Test]
        public void AesGcmIvReuseProducesDifferentCiphers()
        {
            var key = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            var iv = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

            var testData1 = Encoding.UTF8.GetBytes("Test data 1");
            var testData2 = Encoding.UTF8.GetBytes("Test data 2");

            var mysk = new SensitiveByteArray(key);

            var cipher1 = AesGcm.Encrypt(testData1, mysk, iv);
            var cipher2 = AesGcm.Encrypt(testData2, mysk, iv);

            Assert.AreNotEqual(cipher1, cipher2, "Ciphertexts with reused IVs should not match.");
        }

        [Test]
        public void AesGcmLargeDataPass()
        {
            var key = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            var testData = new byte[10_000_000]; // 10 MB of random data
            RandomNumberGenerator.Fill(testData);

            var mysk = new SensitiveByteArray(key);

            var (IV, cipher) = AesGcm.Encrypt(testData, mysk);
            var roundtrip = AesGcm.Decrypt(cipher, mysk, IV);

            Assert.IsTrue(testData.SequenceEqual(roundtrip), "Large data encryption/decryption failed.");
        }

        [Test]
        public void AesGcmBinaryDataPass()
        {
            var key = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            var testData = new byte[] { 0, 255, 34, 56, 128, 200, 11, 76 };

            var mysk = new SensitiveByteArray(key);

            var (IV, cipher) = AesGcm.Encrypt(testData, mysk);
            var roundtrip = AesGcm.Decrypt(cipher, mysk, IV);

            Assert.IsTrue(testData.SequenceEqual(roundtrip), "Binary data encryption/decryption failed.");
        }

    }
}
