using System;
using System.Diagnostics;
using NodaTime;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Serialization;
using Odin.Core.Time;

namespace Odin.Core.Tests
{
    public class UnixTimeUtcTests
    {
        [Test]
        public void UnitTimeXCreateTest01()
        {
            var s = new UnixTimeUtc();
            var ms = new UnixTimeUtc(s);
            ClassicAssert.IsTrue(s.milliseconds == ms.milliseconds);
            ClassicAssert.IsTrue(s.seconds == ms.seconds);
        }

        [Test]
        public void UnitTimeXCreateTest04()
        {
            var os = new UnixTimeUtc();
            var ts = os;
            ClassicAssert.IsTrue(ts.milliseconds == os.milliseconds);

            var ns = ts.AddSeconds(1);
            ClassicAssert.IsTrue(ns.milliseconds == ts.milliseconds + 1000); // The original unchanged
            ClassicAssert.IsTrue(ns.seconds == ts.seconds + 1);

            ts = ns.AddMilliseconds(500);
            ClassicAssert.IsTrue(os.milliseconds + 1500 == ts.milliseconds);

            var pp = ts.AddMilliseconds(250);
            ClassicAssert.IsTrue(os.milliseconds + 1500 == ts.milliseconds);
            ClassicAssert.IsTrue(os.milliseconds + 1750 == pp.milliseconds);
            ClassicAssert.IsTrue(pp != os);
        }


        [Test]
        public void UnixTimeAddtest05()
        {
            var ts1 = UnixTimeUtc.Now();
            Int64 ms = ts1.milliseconds;
            ts1 = ts1.AddSeconds(24 * 3600);
            ClassicAssert.IsTrue(ts1.milliseconds > ms);
        }


        [Test]
        public void UnixTimeOperator06()
        {
            var ts1 = UnixTimeUtc.Now();
            var ts2 = ts1;
            ClassicAssert.IsTrue(ts1 == ts2);
            ClassicAssert.IsTrue(ts1 >= ts2);
            ClassicAssert.IsTrue(ts1 <= ts2);

            ts1 = ts1.AddSeconds(24 * 3600);
            ClassicAssert.IsTrue(ts1 > ts2);
            ClassicAssert.IsTrue(ts1 >= ts2);
            ClassicAssert.IsTrue(ts2 < ts1);
            ClassicAssert.IsTrue(ts2 <= ts1);
            ClassicAssert.IsTrue(ts1 != ts2);
        }

        [Test]
        public void CanSerializeUnixTimeUtc()
        {
            var value = UnixTimeUtc.Now();
            var json = OdinSystemSerializer.Serialize(value);

            var deserializedValue = OdinSystemSerializer.Deserialize<UnixTimeUtc>(json);

            ClassicAssert.IsTrue(value == deserializedValue);
            ClassicAssert.IsTrue(value.milliseconds == deserializedValue.milliseconds);
        }


        [Test]
        public void UniqueIsUniqueTest()
        {
            UnixTimeUtcUnique t1, t2;

            for (int i = 0; i < 1000; i++)
            {
                t1 = UnixTimeUtcUnique.Now();
                t2 = UnixTimeUtcUnique.Now();

                ClassicAssert.IsTrue(t1.uniqueTime != t2.uniqueTime);
            }
        }


        [Test]
        public void TestNodaTimeExtremeRange02()
        {
            var future = Instant.FromUtc(9999, 12, 31, 23, 59, 59);
            ClassicAssert.AreEqual("9999-12-31T23:59:59Z", future.ToString());
            var futureUnixTime = future.ToUnixTimeMilliseconds();
            ClassicAssert.AreEqual(253402300799000, futureUnixTime);
            ClassicAssert.AreEqual(future, Instant.FromUnixTimeMilliseconds(futureUnixTime));

            var past = Instant.FromUtc(-9998, 01, 01, 00, 00, 00);
            ClassicAssert.AreEqual("-9998-01-01T00:00:00Z", past.ToString());
            var pastUnixTime = past.ToUnixTimeMilliseconds();
            ClassicAssert.AreEqual(-377673580800000, pastUnixTime);
            ClassicAssert.AreEqual(past, Instant.FromUnixTimeMilliseconds(pastUnixTime));

            var epoch = Instant.FromUtc(1970, 01, 01, 00, 00);
            ClassicAssert.AreEqual(epoch.ToUnixTimeMilliseconds(), new UnixTimeUtc(0).milliseconds);
        }

        [Test]
        public void TestNodaTimeVsUnixTimeUtc()
        {
            var epoch = Instant.FromUtc(1970, 01, 01, 00, 00);
            ClassicAssert.AreEqual(epoch.ToUnixTimeMilliseconds(), new UnixTimeUtc(0).milliseconds);
        }

        [Test]
        public void TestUnixTimeUtcTypeConversions()
        {
            UnixTimeUtc ut = UnixTimeUtc.Now();
            Instant nt = ut;
            ClassicAssert.AreEqual(nt.ToUnixTimeMilliseconds(), ut.milliseconds);

            var ut2 = new UnixTimeUtc(nt);
            ClassicAssert.AreEqual(nt.ToUnixTimeMilliseconds(), ut2.milliseconds);
        }

        [Test]
        public void UnitTimeType10()
        {
            var ut = UnixTimeUtc.Now();
            Int64 i = (Int64) ut;

            ClassicAssert.AreEqual(i, (Int64) ut);

            UnixTimeUtc ut2 = i;
            ClassicAssert.AreEqual(i, (Int64) ut2);
        }
        
        [Test]
        public void SubtractingEarlierTime_ReturnsPositiveTimeSpan()
        {
            var earlier = UnixTimeUtc.FromDateTime(new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Utc));
            var later = UnixTimeUtc.FromDateTime(new DateTime(2023, 1, 1, 12, 5, 0, DateTimeKind.Utc));

            TimeSpan result = later - earlier;

            ClassicAssert.AreEqual(TimeSpan.FromMinutes(5), result);
        }

        [Test]
        public void SubtractingLaterTime_ReturnsNegativeTimeSpan()
        {
            var earlier = UnixTimeUtc.FromDateTime(new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Utc));
            var later = UnixTimeUtc.FromDateTime(new DateTime(2023, 1, 1, 12, 5, 0, DateTimeKind.Utc));

            TimeSpan result = earlier - later;

            ClassicAssert.AreEqual(TimeSpan.FromMinutes(-5), result);
        }

        [Test]
        public void SubtractingEqualTimes_ReturnsZeroTimeSpan()
        {
            var time = UnixTimeUtc.FromDateTime(new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Utc));

            TimeSpan result = time - time;

            ClassicAssert.AreEqual(TimeSpan.Zero, result);
        }

        [Test]
        public void SubtractionWithMillisecondsPrecision_WorksAccurately()
        {
            var baseTime = UnixTimeUtc.FromDateTime(new DateTime(2023, 1, 1, 12, 0, 0, 0, DateTimeKind.Utc));
            var slightlyLater = UnixTimeUtc.FromDateTime(new DateTime(2023, 1, 1, 12, 0, 0, 250, DateTimeKind.Utc));

            TimeSpan result = slightlyLater - baseTime;

            ClassicAssert.AreEqual(TimeSpan.FromMilliseconds(250), result);
        }

        [Test]
        public void BetweenIsBetween()
        {
            var start = UnixTimeUtc.Now().AddSeconds(-100);
            var end = UnixTimeUtc.Now().AddDays(20);
            var now = UnixTimeUtc.Now();
            Assert.That(now.IsBetween(start, end), Is.True);
        }


    }
}