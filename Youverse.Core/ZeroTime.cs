using System;
using System.Threading;

namespace Youverse.Core
{
    /// <summary>
    /// Like Unix Time but operates from 1-1-0001. Useful for user dates in e.g. photo libraries 
    /// where dates easily go before 1970.
    /// </summary>
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
    }
}   