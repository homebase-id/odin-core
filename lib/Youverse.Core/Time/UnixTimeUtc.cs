using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Youverse.Core
{
    public class UnixTimeUtcConverter : JsonConverter<UnixTimeUtc>
    {
        public override UnixTimeUtc Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetInt64();
            return new UnixTimeUtc(value);
        }

        public override void Write(Utf8JsonWriter writer, UnixTimeUtc value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value.milliseconds);
        }
    }

    /// <summary>
    /// UnixTimeUtc: Keeps track of UNIX time in milliseconds since UTC January 1, year 1970 gregorian.
    /// Immutable. Once set, cannot be altered. Negative are in the past
    /// Simply a Int64 in a fancy class.
    /// </summary>
    [JsonConverter(typeof(UnixTimeUtcConverter))]
    public struct UnixTimeUtc
    {
        public static readonly UnixTimeUtc ZeroTime = new UnixTimeUtc(0);

        public UnixTimeUtc()
        {
            _milliseconds = (Int64) DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalMilliseconds;
        }

        public UnixTimeUtc(Int64 milliseconds)
        {
            _milliseconds = milliseconds;
        }

        public UnixTimeUtc(UnixTimeUtc ut)
        {
            _milliseconds = ut.milliseconds;
        }

        public UnixTimeUtc(DateTime dt)
        {
            _milliseconds = (Int64)dt.Subtract(DateTime.UnixEpoch).TotalMilliseconds;
        }

        /// <summary>
        /// Returns a new UnixTimeUtc object with the seconds added.
        /// </summary>
        /// <param name="s"></param>
        public UnixTimeUtc AddSeconds(int s)
        {
            return new UnixTimeUtc((Int64)(((Int64)_milliseconds) + (s * 1000)));
        }

        /// <summary>
        /// Returns a new UnixTimeUtc object with the milliseconds added.
        /// </summary>
        /// <param name="ms"></param>
        public UnixTimeUtc AddMilliseconds(int ms)
        {
            return new UnixTimeUtc((Int64)(((Int64)_milliseconds) + ms));
        }

        public static UnixTimeUtc Now()
        {
            return new UnixTimeUtc();
        }

        public static implicit operator DateTime(UnixTimeUtc ms)
        {
            DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds((long)ms.milliseconds);
            return dateTimeOffset.DateTime;
        }

        public static explicit operator UnixTimeUtc(DateTime dateTime)
        {
            return new UnixTimeUtc(dateTime);
        }

        public bool IsBetween(UnixTimeUtc start, UnixTimeUtc end, bool inclusive = true)
        {
            if (inclusive)
            {
                return start >= this && end <= this;
            }

            return start > this && end < this;
        }

        public static bool operator ==(UnixTimeUtc left, UnixTimeUtc right)
        {
            return left.milliseconds == right.milliseconds;
        }

        public static bool operator !=(UnixTimeUtc left, UnixTimeUtc right)
        {
            return left.milliseconds != right.milliseconds;
        }

        public static bool operator >(UnixTimeUtc left, UnixTimeUtc right)
        {
            return left.milliseconds > right.milliseconds;
        }

        public static bool operator >=(UnixTimeUtc left, UnixTimeUtc right)
        {
            return left.milliseconds >= right.milliseconds;
        }

        public static bool operator <=(UnixTimeUtc left, UnixTimeUtc right)
        {
            return left.milliseconds <= right.milliseconds;
        }

        public static bool operator <(UnixTimeUtc left, UnixTimeUtc right)
        {
            return left.milliseconds < right.milliseconds;
        }

        public override bool Equals(object value)
        {
            if (!(value is UnixTimeUtc))
                return false;

            return ((UnixTimeUtc)value).milliseconds == this.milliseconds;
        }

        public override int GetHashCode()
        {
            return this.milliseconds.GetHashCode();
        }

        public Int64 seconds { get { return _milliseconds / 1000; } }
        public Int64 milliseconds { get { return _milliseconds; } }

        private readonly Int64 _milliseconds;
    }


    /// <summary>
    /// A 64-bit timestamp guaranteed to be unique on the local server. The first portion of the
    /// timestamp contains the number of milliseconds since UnixEpoch, and the next 16 bits of
    /// the timestamp is a 16-bit counter allowing 65K timestamps per millisecond. 
    /// </summary>
    public struct UnixTimeUtcUnique
    {
        public static readonly UnixTimeUtcUnique ZeroTime = new UnixTimeUtcUnique(0);

        public UnixTimeUtcUnique(Int64 msWithCounter)
        {
            _millisecondsUniqueWithCounter = msWithCounter;
        }

        public static UnixTimeUtcUnique Now()
        {
            return UnixTimeUtcUniqueGenerator.Generator();
        }

        public UnixTimeUtc ToUnixTimeUtc()
        {
            return new UnixTimeUtc(_millisecondsUniqueWithCounter >> 16);
        }

        public Int64 uniqueTime { get { return _millisecondsUniqueWithCounter; } }

        private Int64 _millisecondsUniqueWithCounter;
    }


    public static class UnixTimeUtcUniqueGenerator
    {
        static private Object _lock = new Object();
        static private UnixTimeUtc _lastSecond = new UnixTimeUtc(0);
        static private Int32 _counter = 0;


        /// <summary>
        /// Returns a (single server) unique timestamp. 
        /// Top 48 bits (0xFF FF FF FF FF FF 00 00) are the milliseconds (8,925 years since year 1970)
        /// Bottom 16 bits (0xFF FF) are the counter (up to 16,383 per millisecond)
        /// Thread safe.
        /// </summary>
        /// <returns>UnixTimeUtcUnique</returns>
        public static UnixTimeUtcUnique Generator()
        {
            var ms = new UnixTimeUtc();

            lock (_lock)
            {
                if (ms == _lastSecond)
                {
                    // 16 bit counter, 16383 max / millisecondsecond
                    _counter++;
                    if (_counter >= 0xFFFF)
                    {
                        Thread.Sleep(1);
                        // http://msdn.microsoft.com/en-us/library/c5kehkcz.aspx
                        // A lock knows which thread locked it. If the same thread comes again it just increments a counter and does not block.
                        return Generator();
                    }
                }
                else
                {
                    _lastSecond = ms;
                    _counter = 0;
                }
            }

            Int64 r = (ms.milliseconds << 16) | (UInt16) _counter;

            return new UnixTimeUtcUnique(r);
        }
    }
}
