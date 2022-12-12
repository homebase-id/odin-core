using System;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Youverse.Core
{
    public class TimeZoneUtcConverter : JsonConverter<TimeZoneUtc>
    {
        public override TimeZoneUtc Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {

            var values = reader.GetString()?.Split(",");

            if ((values == null) || (values.Length < 2))
            {
                //TODO: Todd wrote: need to return an empty timezone value here
                //      Michael thinks: There is no such thing as an empty timezone, time or date. 
                //                      They are structs and should have a value. Suppose you could choose to call "0" empty.
                return new TimeZoneUtc(0, 0);
            }
            
            var hours = Int32.Parse(values[0]);
            var minutes = Int32.Parse(values[1]);
            
            return new TimeZoneUtc(hours, minutes);
        }

        public override void Write(Utf8JsonWriter writer, TimeZoneUtc value, JsonSerializerOptions options)
        {
            writer.WriteStringValue($"{value.Hours},{value.Minutes}");
        }
    }


    /// <summary>
    /// Stores a ISO UTC timezone in a sortable 2 byte format
    /// </summary>
    [JsonConverter(typeof(TimeZoneUtcConverter))]
    public readonly struct TimeZoneUtc
    {
        public TimeZoneUtc(int hours, int minutes)
        {
            if ((hours < -11) || (hours > 11))
                throw new Exception("hours must be [-11..+11}");

            if ((minutes != 0) && (minutes != 15) && (minutes != 30) && (minutes != 45))
                throw new Exception("minutes must be one of [0, 15, 30, 45]");

            _timezone = new sbyte[2];
            _timezone[0] = (sbyte)hours;
            _timezone[1] = (sbyte)minutes;
        }

        public int Hours
        {
            get
            {
                sbyte h = _timezone[0];
                return h;
            }
        }

        public int Minutes
        {
            get { return _timezone[1]; }
        }

        public override string ToString()
        {
            return "UTC" + _timezone[0].ToString("+00;-00;+00") + ":" + _timezone[1].ToString("00");
        }

        private readonly sbyte[] _timezone;
    }


    /*
    public static class ZeroTime
    {
        static private Object _lock = new Object();
        static private Random _rnd = new Random();
        static private UInt64 _lastSecond = 0;
        static private UInt64 _counter = 0;
        static private DateTime _christEpoch = new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);  // ;-) oh no, starts on year 1

        /// <summary>
        /// Gets number of seconds since January 1, year 1 gregorian (can't do less than 1)
        /// </summary>
        /// <returns></returns>
        public static UInt64 GetZeroTimeSeconds()
        {
            return (UInt64) DateTime.UtcNow.Subtract(_christEpoch).TotalSeconds;
        }

        public static UInt64 GetZeroTimeSeconds(DateTime dt)
        {
            return (UInt64) dt.Subtract(_christEpoch).TotalSeconds;
        }

        public static UInt64 GetZeroTimeMilliseconds()
        {
            return (UInt64) DateTime.UtcNow.Subtract(_christEpoch).TotalMilliseconds;
        }

        public static UInt64 GetZeroTimeMilliseconds(DateTime dt)
        {
            return (UInt64) dt.Subtract(_christEpoch).TotalMilliseconds;
        }

        /// <summary>
        /// Returns a (single server) unique timestamp. 
        /// Top 48 bits (0xFF FF FF FF FF FF 00 00) are the milliseconds (8,925 years since year 1)
        /// Bottom 16 bits (0xFF FF) are the counter (up to 16,383 per millisecond)
        /// </summary>
        /// <returns></returns>
        public static UInt64 ZeroTimeMillisecondsUnique()
        {
            UInt64 seconds;

            seconds = GetZeroTimeMilliseconds();

            lock (_lock)
            {
                if (seconds == _lastSecond)
                {
                    // 16 bit counter, 16383 max / millisecondsecond
                    _counter++;
                    if (_counter >= 0xFFFF)
                    {
                        Thread.Sleep(1);
                        // http://msdn.microsoft.com/en-us/library/c5kehkcz.aspx
                        // A lock knows which thread locked it. If the same thread comes again it just increments a counter and does not block.
                        return ZeroTimeMillisecondsUnique();
                    }
                }
                else
                {
                    _lastSecond = seconds;
                    _counter = 0;
                }
            }

            return (seconds << 16) | _counter;
        }
    }*/
}