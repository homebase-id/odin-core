using System;
using System.Diagnostics;
using NodaTime;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Core.Time;

namespace Odin.Core.Tests
{
    public class UnixTimeUtcTests
    {

        [Test]
        public void TestCiGithub()
        {
            #if CI_GITHUB
            Assert.Fail("HURRAH CI_GITHUB");
            #endif
        }

        [Test]
        public void TestCiWindows()
        {
#if CI_WINDOWS
            Assert.Fail("HURRAH CI_WINDOWS");
#endif
        }

        [Test]
        public void TestCiLinux()
        {
#if CI_LINUX
            Assert.Fail("HURRAH CI_LINUX");
#endif
        }

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
            Debug.Assert(ns.milliseconds == ts.milliseconds + 1000); // The original unchanged
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
            Int64 ms = ts1.milliseconds;
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
            var json = OdinSystemSerializer.Serialize(value);

            var deserializedValue = OdinSystemSerializer.Deserialize<UnixTimeUtc>(json);

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


        [Test]
        public void TestNodaTimeExtremeRange02()
        {
            var future = Instant.FromUtc(9999, 12, 31, 23, 59, 59);
            Assert.AreEqual("9999-12-31T23:59:59Z", future.ToString());
            var futureUnixTime = future.ToUnixTimeMilliseconds();
            Assert.AreEqual(253402300799000, futureUnixTime);
            Assert.AreEqual(future, Instant.FromUnixTimeMilliseconds(futureUnixTime));

            var past = Instant.FromUtc(-9998, 01, 01, 00, 00, 00);
            Assert.AreEqual("-9998-01-01T00:00:00Z", past.ToString());
            var pastUnixTime = past.ToUnixTimeMilliseconds();
            Assert.AreEqual(-377673580800000, pastUnixTime);
            Assert.AreEqual(past, Instant.FromUnixTimeMilliseconds(pastUnixTime));

            var epoch = Instant.FromUtc(1970, 01, 01, 00, 00);
            Assert.AreEqual(epoch.ToUnixTimeMilliseconds(), new UnixTimeUtc(0).milliseconds);
        }

        [Test]
        public void TestNodaTimeVsUnixTimeUtc()
        {
            var epoch = Instant.FromUtc(1970, 01, 01, 00, 00);
            Assert.AreEqual(epoch.ToUnixTimeMilliseconds(), new UnixTimeUtc(0).milliseconds);
        }

        [Test]
        public void TestUnixTimeUtcTypeConversions()
        {
            UnixTimeUtc ut = UnixTimeUtc.Now();
            Instant nt = ut;
            Assert.AreEqual(nt.ToUnixTimeMilliseconds(), ut.milliseconds);

            var ut2 = new UnixTimeUtc(nt);
            Assert.AreEqual(nt.ToUnixTimeMilliseconds(), ut2.milliseconds);
        }

        [Test]
        public void UnitTimeType10()
        {
            var ut = UnixTimeUtc.Now();
            Int64 i = (Int64) ut;

            Assert.AreEqual(i, (Int64) ut);

            UnixTimeUtc ut2 = i;
            Assert.AreEqual(i, (Int64) ut2);
        }
    }
}