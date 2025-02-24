using System;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Time;

namespace Odin.Core.Tests
{
    public class SequentialGuidTests
    {
        [Test]
        public void GuidSeqeunceTest()
        {
            var ba = new byte[16] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            var g = new Guid(ba);
            ClassicAssert.IsTrue(g.ToString() == "04030201-0605-0807-090a-0b0c0d0e0f10");

            // Creating a guid in this sequence will output the right sequence.
            ba = new byte[16] { 4, 3, 2, 1, 6, 5, 8, 7, 9, 10, 11, 12, 13, 14, 15, 16 };
            g = new Guid(ba);
            ClassicAssert.IsTrue(g.ToString() == "01020304-0506-0708-090a-0b0c0d0e0f10");
        }



        [Test]
        public void SequentialGuid01()
        {
            var t1 = new UnixTimeUtc();
            var g1 = SequentialGuid.CreateGuid();
            var t2 = new UnixTimeUtc();

            var tg = SequentialGuid.ToUnixTimeUtc(g1);

            ClassicAssert.True((t1 <= tg) && (t2 >= tg));

            Assert.Pass();
        }


        [Test]
        public void SequentialGuid02()
        {
            var t1 = new UnixTimeUtc();
            var g1 = SequentialGuid.CreateGuid(t1);

            var tg = SequentialGuid.ToUnixTimeUtc(g1);

            ClassicAssert.True(tg == t1);

            Assert.Pass();
        }



        [Test]
        public void SequentialGuid03()
        {
            Guid g1, g2;

            for (int i = 0; i < 100; i++)
            {
                g1 = SequentialGuid.CreateGuid();
                g2 = SequentialGuid.CreateGuid();

                ClassicAssert.True(ByteArrayUtil.muidcmp(g1, g2) == -1);
            }

            Assert.Pass();
        }
    }
}