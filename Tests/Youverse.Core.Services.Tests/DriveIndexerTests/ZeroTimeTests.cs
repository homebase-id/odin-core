using System;
using NUnit.Framework;

namespace Youverse.Core.Services.Tests.DriveIndexerTests
{
    
    public class ZeroTimeTests
    {
        [Test]
        public void GetZeroTimeSecondsTest()
        {
            if (ZeroTime.GetZeroTimeSeconds() != ZeroTime.GetZeroTimeSeconds())
                if (ZeroTime.GetZeroTimeSeconds() != ZeroTime.GetZeroTimeSeconds())
                    Assert.Fail();
        }

        [Test]
        public void ZeroTimeSecondsTest()
        {
            if (ZeroTime.GetZeroTimeSeconds(DateTime.UtcNow) != ZeroTime.GetZeroTimeSeconds(DateTime.UtcNow))
                if (ZeroTime.GetZeroTimeSeconds(DateTime.UtcNow) != ZeroTime.GetZeroTimeSeconds(DateTime.UtcNow))
                    Assert.Fail();

            if (ZeroTime.GetZeroTimeSeconds(DateTime.UtcNow) != ZeroTime.GetZeroTimeSeconds())
                if (ZeroTime.GetZeroTimeSeconds(DateTime.UtcNow) != ZeroTime.GetZeroTimeSeconds())
                    Assert.Fail();
        }

        [Test]
        public void GetZeroTimeMillisecondsTest()
        {
            if (ZeroTime.GetZeroTimeMilliseconds() != ZeroTime.GetZeroTimeMilliseconds())
                if (ZeroTime.GetZeroTimeMilliseconds() != ZeroTime.GetZeroTimeMilliseconds())
                    Assert.Fail();
        }

        [Test]
        public void ZeroTimeMillisecondsTest()
        {
            if (ZeroTime.GetZeroTimeMilliseconds(DateTime.UtcNow) != ZeroTime.GetZeroTimeMilliseconds(DateTime.UtcNow))
                if (ZeroTime.GetZeroTimeMilliseconds(DateTime.UtcNow) != ZeroTime.GetZeroTimeMilliseconds(DateTime.UtcNow))
                    Assert.Fail();

            if (ZeroTime.GetZeroTimeMilliseconds(DateTime.UtcNow) != ZeroTime.GetZeroTimeMilliseconds())
                if (ZeroTime.GetZeroTimeMilliseconds(DateTime.UtcNow) != ZeroTime.GetZeroTimeMilliseconds())
                    Assert.Fail();
        }

        [Test]
        public void ZeroTimeMillisecondsUniqueTest()
        {
            var myArray = new UInt64[100_000];
            for (int i = 0; i < myArray.Length; i++)
            {
                myArray[i] = ZeroTime.ZeroTimeMillisecondsUnique();
            }

            for (int i = 1; i < myArray.Length; i++)
            {
                if (myArray[i - 1] == myArray[i])
                    Assert.Fail();
            }
        }
    }
}