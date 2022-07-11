using System;
using NUnit.Framework;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Tests.DriveIndexerTests
{
    
    public class SequentialGuidTests
    {
        [Test]
        public void CreateGuidTest()
        {
            byte[][] myArray = new byte[100_000][];
            for (int i = 0; i < myArray.Length; i++)
            {
                myArray[i] = SequentialGuid.CreateGuid();
            }

            for (int i = 1; i < myArray.Length; i++)
            {
                if (SequentialGuid.muidcmp(myArray[i-1], myArray[i]) != -1)
                    Assert.Fail();
            }
        }

        [Test]
        public void FileIdToUnixTimeTest()
        {
            var id = new Guid(SequentialGuid.CreateGuid());
            UInt64 t = SequentialGuid.FileIdToUnixTime(id);

            if (t != UnixTime.GetUnixTimeSeconds())
            {
                // Ok, we might have been unlucky and passed that nanosecond where
                // the second switched, so try again.
                id = new Guid(SequentialGuid.CreateGuid());
                t = SequentialGuid.FileIdToUnixTime(id);

                if (t != UnixTime.GetUnixTimeSeconds())
                    Assert.Fail();
            }
        }

        [Test]
        public void muidcmpTest()
        {
            var id1 = SequentialGuid.CreateGuid();
            var id2 = SequentialGuid.CreateGuid();

            if (SequentialGuid.muidcmp(id1, id1) != 0)
                Assert.Fail();

            if (SequentialGuid.muidcmp(id1, id2) != -1)
                Assert.Fail();

            if (SequentialGuid.muidcmp(id2, id1) != 1)
                Assert.Fail();
        }
    }
}