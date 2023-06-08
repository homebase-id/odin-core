using System.Text;
using NUnit.Framework;
using Youverse.Core.Cryptography.Crypto;

namespace Youverse.Core.Cryptography.Tests
{
    public class TestCrcManagement
    {
        [SetUp]
        public void Setup()
        {
        }


        [Test]
        public void CrcPass()
        {
            // CRC
            var crc = CRC32C.CalculateCRC32C(0, Encoding.ASCII.GetBytes("bear sandwich"));

            if (crc == 3711466352)
                Assert.Pass();
            else
                Assert.Fail();
        }

    }
}
