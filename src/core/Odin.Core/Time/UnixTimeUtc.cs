using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using NodaTime;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;

namespace Odin.Core.Time
{
    public class UnixTimeUtcConverter : JsonConverter<UnixTimeUtc>
    {
        public override UnixTimeUtc Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (!reader.TryGetInt64(out var value))
            {
                throw new OdinClientException("Invalid UnixTimeUtc value");
            }
            
            return new UnixTimeUtc(value);
        }

        public override void Write(Utf8JsonWriter writer, UnixTimeUtc value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value.milliseconds);
        }
    }

    public class UnixTimeUtcUniqueConverter : JsonConverter<UnixTimeUtcUnique>
    {
        public override UnixTimeUtcUnique Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (!reader.TryGetInt64(out var value))
            {
                throw new OdinClientException("Invalid UnixTimeUtc value");
            }

            return new UnixTimeUtcUnique(value);
        }

        public override void Write(Utf8JsonWriter writer, UnixTimeUtcUnique value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value.uniqueTime);
        }
    }

    /// <summary>
    /// UnixTimeUtc: Keeps track of UNIX time in milliseconds since UTC January 1, year 1970 gregorian.
    /// Immutable. Once set, cannot be altered. Negative are in the past
    /// Simply a Int64 in a fancy class.
    /// </summary>
    [JsonConverter(typeof(UnixTimeUtcConverter))]
    [DebuggerDisplay("dt={System.DateTimeOffset.FromUnixTimeMilliseconds(_milliseconds).ToString(\"yyyy-MM-dd HH:mm:ss.fff\")}")]
    public readonly struct UnixTimeUtc : IGenericCloneable<UnixTimeUtc>, IEquatable<UnixTimeUtc>
    {
        public static readonly UnixTimeUtc ZeroTime = new UnixTimeUtc(0);

        public UnixTimeUtc()
        {
            _milliseconds = (Int64)DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalMilliseconds;
        }

        public UnixTimeUtc(Int64 milliseconds)
        {
            _milliseconds = milliseconds;
        }

        public UnixTimeUtc(UnixTimeUtc ut)
        {
            _milliseconds = ut.milliseconds;
        }

        public UnixTimeUtc(Instant nodaTime)
        {
            _milliseconds = nodaTime.ToUnixTimeMilliseconds();
        }

        public UnixTimeUtc(DateTimeOffset dto)
        {
            _milliseconds = dto.ToUnixTimeMilliseconds();
        }

        public UnixTimeUtc Clone()
        {
            return new UnixTimeUtc(_milliseconds);
        }

        // Define cast to Int64
        public static implicit operator UnixTimeUtc(Int64 milliseconds)
        {
            return new UnixTimeUtc(milliseconds);
        }

        public static explicit operator Int64(UnixTimeUtc ut)
        {
            return ut.milliseconds;
        }


        /// <summary>
        /// Returns a new UnixTimeUtc object with the seconds added.
        /// </summary>
        /// <param name="s">Seconds</param>
        public UnixTimeUtc AddSeconds(Int64 s)
        {
            return new UnixTimeUtc((Int64)(((Int64)_milliseconds) + (s * 1000)));
        }

        /// <summary>
        /// Returns a new UnixTimeUtc object with the minutes added.
        /// </summary>
        /// <param name="m">Minutes</param>
        public UnixTimeUtc AddMinutes(Int64 m)
        {
            return new UnixTimeUtc((Int64)(((Int64)_milliseconds) + (m * 60 * 1000)));
        }

        /// <summary>
        /// Returns a new UnixTimeUtc object with the hours added.
        /// </summary>
        /// <param name="h">Hours</param>
        public UnixTimeUtc AddHours(Int64 h)
        {
            return new UnixTimeUtc((Int64)(((Int64)_milliseconds) + (h * 60 * 60 * 1000)));
        }


        /// <summary>
        /// Returns a new UnixTimeUtc object with the hours added.
        /// </summary>
        /// <param name="d">Days</param>
        public UnixTimeUtc AddDays(Int64 d)
        {
            return new UnixTimeUtc((Int64)(((Int64)_milliseconds) + (d * 24 * 60 * 60 * 1000)));
        }

        /// <summary>
        /// Returns a new UnixTimeUtc object with the milliseconds added.
        /// </summary>
        /// <param name="ms"></param>
        public UnixTimeUtc AddMilliseconds(Int64 ms)
        {
            return new UnixTimeUtc((Int64)(((Int64)_milliseconds) + ms));
        }

        public static UnixTimeUtc Now()
        {
            return new UnixTimeUtc();
        }

        public static implicit operator Instant(UnixTimeUtc ms)
        {
            return Instant.FromUnixTimeMilliseconds(ms.milliseconds);
        }

        public static explicit operator UnixTimeUtc(Instant nodaTime)
        {
            return new UnixTimeUtc(nodaTime.ToUnixTimeMilliseconds());
        }

        public DateTime ToDateTime()
        {
            DateTime unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime dateTime = unixEpoch.AddMilliseconds(this.milliseconds);
            return dateTime;
        }
        
        public DateTimeOffset ToDateTimeOffset()
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }

        public static UnixTimeUtc FromDateTime(DateTime dateTime)
        {
            DateTime unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            long millisecondsSinceEpoch = (long)(dateTime - unixEpoch).TotalMilliseconds;
            return new UnixTimeUtc(millisecondsSinceEpoch);
        }
        
        public static UnixTimeUtc FromDateTimeOffset(DateTimeOffset dateTime)
        {
            DateTime unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            long millisecondsSinceEpoch = (long)(dateTime - unixEpoch).TotalMilliseconds;
            return new UnixTimeUtc(millisecondsSinceEpoch);
        }
       

        public bool IsBetween(UnixTimeUtc start, UnixTimeUtc end, bool inclusive = true)
        {
            if (inclusive)
            {
                return start >= this && end <= this;
            }

            return start > this && end < this;
        }

        public static TimeSpan operator -(UnixTimeUtc left, UnixTimeUtc right)
        {
            return TimeSpan.FromMilliseconds(left.milliseconds - right.milliseconds);
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
        public bool Equals(UnixTimeUtc other)
        {
            return this._milliseconds == other._milliseconds;
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

        public Int64 milliseconds
        {
            get { return _milliseconds; }
        }

        public Int64 seconds
        {
            get { return _milliseconds / 1000; }
        }

        /// <summary>
        /// TODO ISO name
        /// TODO I don't think I need to convert to DateTime first
        /// Outputs time as ISO ... "yyyy-MM-ddTHH:mm:ssZ"
        /// </summary>
        /// <returns>ISO ... string</returns>
        public string Iso9441()
        {
            return ToDateTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        }

        public override string ToString()
        {
            return _milliseconds.ToString();
        }

        private readonly Int64 _milliseconds;
    }


    /// <summary>
    /// A 64-bit timestamp guaranteed to be unique on the local server. The first portion of the
    /// timestamp contains the number of milliseconds since UnixEpoch, and the next 16 bits of
    /// the timestamp is a 16-bit counter allowing 65K timestamps per millisecond. 
    /// </summary>
    [JsonConverter(typeof(UnixTimeUtcUniqueConverter))]
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

        public Int64 uniqueTime
        {
            get { return _millisecondsUniqueWithCounter; }
        }

        private Int64 _millisecondsUniqueWithCounter;

        public override string ToString()
        {
            return uniqueTime.ToString();
        }
    }


    public static class UnixTimeUtcUniqueGenerator
    {
        static private Object _lock = new Object();
        static private UnixTimeUtc _lastSecond = new UnixTimeUtc(0);
        static private Int32 _counter = 0;

        /// <summary>
        /// Returns a (single server) unique timestamp. 
        /// Top 48 bits (0xFF FF FF FF FF FF 00 00) are the milliseconds (8,925 years since year 1970)
        /// Bottom 16 bits (0xFF FF) are the counter (up to 65,535 per millisecond)
        /// Thread safe.
        /// </summary>
        /// <returns>UnixTimeUtcUnique</returns>
        public static UnixTimeUtcUnique Generator()
        {
            UnixTimeUtc ms;

            while (true)
            {
                lock (_lock)
                {
                    ms = new UnixTimeUtc(); // Update timestamp at the beginning of each iteration

                    if (ms == _lastSecond)
                    {
                        // 16 bit counter, 65535 max / millisecond
                        _counter++;
                        if (_counter >= 0xFFFF)
                        {
                            // Need to wait for the next millisecond
                            // Exit lock to sleep without holding the lock
                        }
                        else
                        {
                            break; // Unique timestamp generated
                        }
                    }
                    else
                    {
                        _lastSecond = ms;
                        _counter = 0;
                        break; // Unique timestamp generated
                    }
                }

                Thread.Sleep(1); // Sleep outside the lock
            }

            Int64 r = (ms.milliseconds << 16) | (UInt16)_counter;

            return new UnixTimeUtcUnique(r);
        }

        /// <summary>
        /// Returns a (single server) unique timestamp. 
        /// Top 48 bits (0xFF FF FF FF FF FF 00 00) are the milliseconds (8,925 years since year 1970)
        /// Bottom 16 bits (0xFF FF) are the counter (up to 16,383 per millisecond)
        /// Thread safe.
        /// </summary>
        /// <returns>UnixTimeUtcUnique</returns>
        ///
        public static UnixTimeUtcUnique OldGenerator()
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
                        return OldGenerator();
                    }
                }
                else
                {
                    _lastSecond = ms;
                    _counter = 0;
                }
            }

            Int64 r = (ms.milliseconds << 16) | (UInt16)_counter;

            return new UnixTimeUtcUnique(r);
        }
    }
}