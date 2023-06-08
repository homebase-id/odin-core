using NUnit.Framework;
using System;
using System.Diagnostics;
using Youverse.Core.Serialization;

namespace Youverse.Core.Tests
{
    public class DateUtcTests
    {
        [Test]
        public void DateUtc01()
        {
            var tz = new DateUtc(0, 1, 1);
            tz = new DateUtc(-9999, 12, 31);
            tz = new DateUtc(9999, 1, 1);
            Assert.Pass();
        }


        [Test]
        public void DateUtc02()
        {
            bool ok = true;

            try
            {
                var tz = new DateUtc(Int16.MinValue-1, 1, 1);
                ok = false;
            }
            catch
            {
            }
            Assert.IsTrue(ok);

            try
            {
                var tz = new DateUtc(Int16.MaxValue + 1, 1, 1);
                ok = false;
            }
            catch
            {
            }
            Assert.IsTrue(ok);


            try
            {
                var tz = new DateUtc(0, -1, 1);
                ok = false;
            }
            catch
            {
            }
            Assert.IsTrue(ok);

            try
            {
                var tz = new DateUtc(0, 13, 1);
                ok = false;
            }
            catch
            {
            }
            Assert.IsTrue(ok);

            try
            {
                var tz = new DateUtc(0, 1, 0);
                ok = false;
            }
            catch
            {
            }
            Assert.IsTrue(ok);

            try
            {
                var tz = new DateUtc(0, 1, 32);
                ok = false;
            }
            catch
            {
            }
            Assert.IsTrue(ok);

            Assert.Pass();
        }

        [Test]
        public void DateUtc03()
        {
            var tz = new DateUtc(1, 1, 1);
            Debug.Assert(tz.ToString() == "1-01-01 CE");

            tz = new DateUtc(0, 12, 31);
            Debug.Assert(tz.ToString() == "0-12-31 CE");

            tz = new DateUtc(-1,12,31);
            Debug.Assert(tz.ToString() == "-1-12-31 CE");

            tz = new DateUtc(9999,7,1);
            Debug.Assert(tz.ToString() == "9999-07-01 CE");

            tz = new DateUtc(-9999, 9, 1);
            Debug.Assert(tz.ToString() == "-9999-09-01 CE");

            Assert.Pass();
        }

        [Test]
        public void DateUtc04()
        {
            var value = new DateUtc(-9999, 12, 31);
            var json = OdinSystemSerializer.Serialize(value);
            var deserializedValue = OdinSystemSerializer.Deserialize<DateUtc>(json);

            Assert.IsTrue(value.ToString() == deserializedValue.ToString());

            value = new DateUtc(+9999, 12, 31);
            json = OdinSystemSerializer.Serialize(value);
            deserializedValue = OdinSystemSerializer.Deserialize<DateUtc>(json);

            Assert.IsTrue(value.ToString() == deserializedValue.ToString());

            Assert.Pass();
        }
    }
}