using NUnit.Framework;
using System.Text;
using Youverse.Core.Cryptography.Crypto;

namespace Youverse.Core.Cryptography.Tests
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

            var mysk = new SensitiveByteArray(key);

            var (IV, cipher) = AesCbc.Encrypt(Encoding.UTF8.GetBytes(testData), ref mysk);
            var roundtrip = Encoding.UTF8.GetString(AesCbc.Decrypt(cipher, ref mysk, IV));

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
