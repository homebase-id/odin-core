using System;
using NUnit.Framework;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Tests.DriveIndexerTests
{
    
    public class UnixTimeTests
    {
        [Test]
        public void GetUnixTimeSecondsTest()
        {
            if (UnixTime.GetUnixTimeSeconds() != UnixTime.GetUnixTimeSeconds())
                if (UnixTime.GetUnixTimeSeconds() != UnixTime.GetUnixTimeSeconds())
                    Assert.Fail();
        }

        [Test]
        public void GetUnixTimeSecondsTest1()
        {
            if (UnixTime.GetUnixTimeSeconds(DateTime.UtcNow) != UnixTime.GetUnixTimeSeconds(DateTime.UtcNow))
                if (UnixTime.GetUnixTimeSeconds(DateTime.UtcNow) != UnixTime.GetUnixTimeSeconds(DateTime.UtcNow))
                    Assert.Fail();

            if (UnixTime.GetUnixTimeSeconds(DateTime.UtcNow) != UnixTime.GetUnixTimeSeconds())
                if (UnixTime.GetUnixTimeSeconds(DateTime.UtcNow) != UnixTime.GetUnixTimeSeconds())
                    Assert.Fail();
        }

        [Test]
        public void GetUnixTimeMillisecondsTest()
        {
            if (UnixTime.GetUnixTimeMilliseconds() != UnixTime.GetUnixTimeMilliseconds())
                if (UnixTime.GetUnixTimeMilliseconds() != UnixTime.GetUnixTimeMilliseconds())
                    Assert.Fail();
        }

        [Test]
        public void GetUnixTimeMillisecondsTest1()
        {
            if (UnixTime.GetUnixTimeMilliseconds(DateTime.UtcNow) != UnixTime.GetUnixTimeMilliseconds(DateTime.UtcNow))
                if (UnixTime.GetUnixTimeMilliseconds(DateTime.UtcNow) != UnixTime.GetUnixTimeMilliseconds(DateTime.UtcNow))
                    Assert.Fail();

            if (UnixTime.GetUnixTimeMilliseconds(DateTime.UtcNow) != UnixTime.GetUnixTimeMilliseconds())
                if (UnixTime.GetUnixTimeMilliseconds(DateTime.UtcNow) != UnixTime.GetUnixTimeMilliseconds())
                    Assert.Fail();
        }

        [Test]
        public void UnixTimeMillisecondsUniqueTest()
        {
            var myArray = new UInt64[100_000];
            for (int i = 0; i < myArray.Length; i++)
            {
                myArray[i] = UnixTime.UnixTimeMillisecondsUnique();
            }

            for (int i = 1; i < myArray.Length; i++)
            {
                if (myArray[i-1] == myArray[i])
                    Assert.Fail();
            }

        }
    }
}