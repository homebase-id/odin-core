using System.Diagnostics;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Serialization;
using Odin.Core.Time;

namespace Odin.Core.Tests
{
    public class TimeZoneUtcTests
    {
        [Test]
        public void TimeZoneUtc01()
        {
            var tz = new TimeZoneUtc(11, 0);
            tz = new TimeZoneUtc(-11, 15);
            tz = new TimeZoneUtc(0, 30);
            tz = new TimeZoneUtc(-1, 45);
            Assert.Pass();
        }


        [Test]
        public void TimeZoneUtc02()
        {
            bool ok = true;

            try
            {
                var tz = new TimeZoneUtc(-12, 0);
                ok = false;
            }
            catch
            {
            }
            ClassicAssert.IsTrue(ok);

            try
            {
                var tz = new TimeZoneUtc(+12, 0);
                ok = false;
            }
            catch
            {
            }
            ClassicAssert.IsTrue(ok);


            try
            {
                var tz = new TimeZoneUtc(0, 1);
                ok = false;
            }
            catch
            {
            }
            ClassicAssert.IsTrue(ok);

            try
            {
                var tz = new TimeZoneUtc(0, 60);
                ok = false;
            }
            catch
            {
            }

            ClassicAssert.IsTrue(ok);
            Assert.Pass();
        }

        [Test]
        public void TimeZoneUtc03()
        {
            var tz = new TimeZoneUtc(1, 0);
            ClassicAssert.IsTrue(tz.ToString() == "UTC+01:00");

            tz = new TimeZoneUtc(10, 15);
            ClassicAssert.IsTrue(tz.ToString() == "UTC+10:15");

            tz = new TimeZoneUtc(0, 15);
            ClassicAssert.IsTrue(tz.ToString() == "UTC+00:15");

            tz = new TimeZoneUtc(-1, 30);
            ClassicAssert.IsTrue(tz.ToString() == "UTC-01:30");

            tz = new TimeZoneUtc(-11, 45);
            ClassicAssert.IsTrue(tz.ToString() == "UTC-11:45");

            Assert.Pass();
        }

        [Test]
        public void TimeZoneUtc04()
        {
            var value = new TimeZoneUtc(+7, 45);
            var json = OdinSystemSerializer.Serialize(value);
            var deserializedValue = OdinSystemSerializer.Deserialize<TimeZoneUtc>(json);

            ClassicAssert.IsTrue(value.ToString() == deserializedValue.ToString());
        }
    }
}