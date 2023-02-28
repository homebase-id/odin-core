using NUnit.Framework;
using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Youverse.Core.Serialization;

namespace Youverse.Core.Tests
{
    public class UnixTimeUtcTests
    {

        [Test]
        public void UnitTimeXCreateTest01()
        {
            var s = new UnixTimeUtc();
            var ms = new UnixTimeUtc(s);
            Debug.Assert(s.milliseconds == ms.milliseconds);
            Debug.Assert(s.seconds == ms.seconds);
        }

        [Test]
        public void UnitTimeXCreateTest04()
        {
            var os = new UnixTimeUtc();
            var ts = os;
            Debug.Assert(ts.milliseconds == os.milliseconds);

            var ns = ts.AddSeconds(1);
            Debug.Assert(ns.milliseconds == ts.milliseconds+1000); // The original unchanged
            Debug.Assert(ns.seconds == ts.seconds + 1);

            ts = ns.AddMilliseconds(500);
            Debug.Assert(os.milliseconds + 1500 == ts.milliseconds);

            var pp = ts.AddMilliseconds(250);
            Debug.Assert(os.milliseconds + 1500 == ts.milliseconds);
            Debug.Assert(os.milliseconds + 1750 == pp.milliseconds);
            Debug.Assert(pp != os);
        }


        [Test]
        public void UnixTimeAddtest05()
        {
            var ts1 = UnixTimeUtc.Now();
            UInt64 ms = ts1.milliseconds;
            ts1 = ts1.AddSeconds(24 * 3600);
            Debug.Assert(ts1.milliseconds > ms);
        }


        [Test]
        public void UnixTimeOperator06()
        {
            var ts1 = UnixTimeUtc.Now();
            var ts2 = ts1;
            Debug.Assert(ts1 == ts2);
            Debug.Assert(ts1 >= ts2);
            Debug.Assert(ts1 <= ts2);

            ts1 = ts1.AddSeconds(24 * 3600);
            Debug.Assert(ts1 > ts2);
            Debug.Assert(ts1 >= ts2);
            Debug.Assert(ts2 < ts1);
            Debug.Assert(ts2 <= ts1);
            Debug.Assert(ts1 != ts2);
        }

        [Test]
        public void CanSerializeUnixTimeUtc()
        {
            var value = UnixTimeUtc.Now();
            var json = DotYouSystemSerializer.Serialize(value);

            var deserializedValue = DotYouSystemSerializer.Deserialize<UnixTimeUtc>(json);

            Assert.IsTrue(value == deserializedValue);
            Assert.IsTrue(value.milliseconds == deserializedValue.milliseconds);
        }


        [Test]
        public void UniqueIsUniqueTest()
        {
            UnixTimeUtcUnique t1, t2;

            for (int i = 0; i < 1000; i++)
            {
                t1 = UnixTimeUtcUnique.Now();
                t2 = UnixTimeUtcUnique.Now();

                Assert.IsTrue(t1.uniqueTime != t2.uniqueTime);
            }
        }
    }
}