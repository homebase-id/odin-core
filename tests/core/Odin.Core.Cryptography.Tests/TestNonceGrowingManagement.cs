using NUnit.Framework;

namespace Odin.Core.Cryptography.Tests
{
    public class TestNonceGrowingManagement
    {
        [SetUp]
        public void Setup()
        {
        }


        [Test]
        public void NoncePass()
        {
            var nt = new NonceTable();
            var secret = ByteArrayUtil.GetRndByteArray(16);

            if (NonceGrowingManager.ValidateNonce(nt, 1, NonceGrowingManager.CalculateBase64NonceSHA256(1, secret), secret))
                Assert.Pass();
            else
                Assert.Fail();
        }

        [Test]
        public void NonceTwoPass()
        {
            var nt = new NonceTable();
            var secret = ByteArrayUtil.GetRndByteArray(16);

            if (!NonceGrowingManager.ValidateNonce(nt, 7, NonceGrowingManager.CalculateBase64NonceSHA256(7, secret), secret))
            {
                Assert.Fail();
                return;
            }

            if (!NonceGrowingManager.ValidateNonce(nt, 11, NonceGrowingManager.CalculateBase64NonceSHA256(11, secret), secret))
            {
                Assert.Fail();
                return;
            }

            Assert.Pass();
        }


        [Test]
        public void NonceEqualFail()
        {
            var nt = new NonceTable();
            var secret = ByteArrayUtil.GetRndByteArray(16);

            if (!NonceGrowingManager.ValidateNonce(nt, 1, NonceGrowingManager.CalculateBase64NonceSHA256(1, secret), secret))
            {
                Assert.Fail();
                return;
            }

            try
            {
                if (NonceGrowingManager.ValidateNonce(nt, 1, NonceGrowingManager.CalculateBase64NonceSHA256(1, secret), secret))
                {
                    Assert.Fail();
                    return;
                }
            }
            catch {
                Assert.Pass();
                return;
            }

            Assert.Pass();
        }

        [Test]
        public void NonceMismatchFail()
        {
            var nt = new NonceTable();
            var secret1 = ByteArrayUtil.GetRndByteArray(16);
            var secret2 = ByteArrayUtil.GetRndByteArray(16);

            if (NonceGrowingManager.ValidateNonce(nt, 1, NonceGrowingManager.CalculateBase64NonceSHA256(1, secret1), secret2) == true)
            {
                Assert.Fail();
                return;
            }

            Assert.Pass();
        }

        [Test]
        public void NonceLessFail()
        {
            var nt = new NonceTable();
            var secret = ByteArrayUtil.GetRndByteArray(16);

            if (!NonceGrowingManager.ValidateNonce(nt, 5, NonceGrowingManager.CalculateBase64NonceSHA256(5, secret), secret))
            {
                Assert.Fail();
                return;
            }

            try
            {
                if (NonceGrowingManager.ValidateNonce(nt, 3, NonceGrowingManager.CalculateBase64NonceSHA256(3, secret), secret))
                {
                    Assert.Fail();
                    return;
                }
            }
            catch
            {
                Assert.Pass();
                return;
            }

            Assert.Pass();
        }

    }
}
