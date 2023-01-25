using NUnit.Framework;
using System;
using System.Diagnostics;
using Youverse.Core.Serialization;

namespace Youverse.Core.Tests
{
    public class SequentialGuidTests
    {
        [Test]
        public void SequentialGuid01()
        {
            var t1 = new UnixTimeUtc();
            var g1 = SequentialGuid.CreateGuid();
            var t2 = new UnixTimeUtc();

            var tg = SequentialGuid.ToUnixTimeUtc(g1);

            Assert.True((t1 <= tg) && (t2 >= tg));

            Assert.Pass();
        }


        [Test]
        public void SequentialGuid02()
        {
            var t1 = new UnixTimeUtc();
            var g1 = SequentialGuid.CreateGuid(t1);

            var tg = SequentialGuid.ToUnixTimeUtc(g1);

            Assert.True(tg == t1);

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

                Assert.True(ByteArrayUtil.muidcmp(g1, g2) == -1);
            }

            Assert.Pass();
        }
    }
}