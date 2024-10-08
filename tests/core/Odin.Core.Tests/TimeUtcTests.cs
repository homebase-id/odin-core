﻿using System.Diagnostics;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Core.Time;

namespace Odin.Core.Tests
{
    public class TimeUtcTests
    {
        [Test]
        public void TimeUtc01()
        {
            var tz = new TimeUtc(0, 0, 0);
            tz = new TimeUtc(23, 59, 59);
            Assert.Pass();
        }


        [Test]
        public void TimeUtc02()
        {
            bool ok = true;

            try
            {
                var tz = new TimeUtc(-1, 0, 0);
                ok = false;
            }
            catch
            {
            }
            Assert.IsTrue(ok);

            try
            {
                var tz = new TimeUtc(24, 0, 0);
                ok = false;
            }
            catch
            {
            }
            Assert.IsTrue(ok);


            try
            {
                var tz = new TimeUtc(0, -1, 0);
                ok = false;
            }
            catch
            {
            }
            Assert.IsTrue(ok);

            try
            {
                var tz = new TimeUtc(0, 60, 0);
                ok = false;
            }
            catch
            {
            }
            Assert.IsTrue(ok);

            try
            {
                var tz = new TimeUtc(0, 0, -1);
                ok = false;
            }
            catch
            {
            }
            Assert.IsTrue(ok);

            try
            {
                var tz = new TimeUtc(0, 0, 60);
                ok = false;
            }
            catch
            {
            }
            Assert.IsTrue(ok);

            Assert.Pass();
        }

        [Test]
        public void TimeUtc03()
        {
            var tz = new TimeUtc(0, 0, 0);
            Debug.Assert(tz.ToString() == "00:00:00");

            tz = new TimeUtc(1, 1, 1);
            Debug.Assert(tz.ToString() == "01:01:01");

            tz = new TimeUtc(23,59,59);
            Debug.Assert(tz.ToString() == "23:59:59");

            Assert.Pass();
        }

        [Test]
        public void TimeUtc04()
        {
            var value = new TimeUtc(0, 0, 0);
            var json = OdinSystemSerializer.Serialize(value);
            var deserializedValue = OdinSystemSerializer.Deserialize<TimeUtc>(json);
            Assert.IsTrue(value.ToString() == deserializedValue.ToString());

            value = new TimeUtc(23, 59, 59);
            json = OdinSystemSerializer.Serialize(value);
            deserializedValue = OdinSystemSerializer.Deserialize<TimeUtc>(json);
            Assert.IsTrue(value.ToString() == deserializedValue.ToString());

            Assert.Pass();
        }
    }
}