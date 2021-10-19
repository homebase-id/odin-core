using NUnit.Framework;

namespace Youverse.Core.Cryptography.Tests
{
    public class TestByteArrayManagement
    {
        [SetUp]
        public void Setup()
        {
        }


        //
        // ===== A FEW BYTE ARRAY TESTS =====
        //

        [Test]
        public void CompareTwoRndFail()
        {
            byte[] ba1 = YFByteArray.GetRndByteArray(40);
            byte[] ba2 = YFByteArray.GetRndByteArray(40);

            if (YFByteArray.EquiByteArrayCompare(ba1, ba2))
                Assert.Fail();
            else
                Assert.Pass();
        }

        [Test]
        public void CompareArrayPass()
        {
            byte[] ba1 = YFByteArray.GetRndByteArray(40);

            if (YFByteArray.EquiByteArrayCompare(ba1, ba1))
                Assert.Pass();
            else
                Assert.Fail();
        }



    }
}
