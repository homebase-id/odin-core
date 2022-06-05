using System;
using System.Threading;

namespace Youverse.Core.Services.Drive.Query.Sqlite
{
    public static class UnixTime
    {
        static private Object _lock = new Object();
        static private Random _rnd = new Random();
        static private UInt64 _lastSecond = 0;
        static private UInt64 _counter = 0;

        /// <summary>
        /// Gets number of seconds since January 1, year 1 gregorian (can't do less than 1)
        /// </summary>
        /// <returns></returns>
        public static UInt64 GetUnixTimeSeconds()
        {
            return (UInt64) DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds;
        }


        public static UInt64 GetUnixTimeSeconds(DateTime dt)
        {
            return (UInt64) dt.Subtract(DateTime.UnixEpoch).TotalSeconds;
        }


        public static UInt64 GetUnixTimeMilliseconds()
        {
            return (UInt64) DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalMilliseconds;
        }


        public static UInt64 GetUnixTimeMilliseconds(DateTime dt)
        {
            return (UInt64) dt.Subtract(DateTime.UnixEpoch).TotalMilliseconds;
        }


        /// <summary>
        /// Returns a (single server) unique timestamp. 
        /// Top 48 bits (0xFF FF FF FF FF FF 00 00) are the milliseconds (8,925 years since year 1970)
        /// Bottom 16 bits (0xFF FF) are the counter (up to 16,383 per millisecond)
        /// </summary>
        /// <returns></returns>
        public static UInt64 UnixTimeMillisecondsUnique()
        {
            UInt64 seconds;

            seconds = GetUnixTimeMilliseconds();

            lock (_lock)
            {
                // 16 bit counter, 16383 max / millisecondsecond
                if (seconds == _lastSecond)
                {
                    _counter++;
                    if (_counter >= 0xFFFF)
                    {
                        Thread.Sleep(1);
                        // http://msdn.microsoft.com/en-us/library/c5kehkcz.aspx
                        // A lock knows which thread locked it. If the same thread comes again it just increments a counter and does not block.
                        return UnixTimeMillisecondsUnique();
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