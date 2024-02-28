using System;
using System.Threading;
using Odin.Core.Time;

namespace Odin.Core
{
    /// <summary>
    /// Might consider switching to UnixTime.UnixTimeMillisecondsUnique()
    /// because the ms resolution will give less issues in a multi-server setup.
    /// </summary>
    public static class SequentialGuid
    {
        private static readonly Random _rnd = new Random();
        static private Object _lock = new Object();
        static private UnixTimeUtc _lastMillisecond = new UnixTimeUtc(0);
        static private int _counter = 0;

        /// <summary>
        /// Create a new Guid consisting of {44 bits milliseconds, 12 bits counter, 9 bytes random values}
        /// </summary>
        /// <param name="millisecondsWithCounter">Rightmost 56 bits (7 bytes) {miliseconds (44 bits), _counter(12 bits)}</param>
        /// <returns>A new Guid</returns>
        private static Guid CreateGuidFromMillisecond(UInt64 millisecondsWithCounter)
        {
            byte a0, a1, a2, a3, a4, a5, a6, a7, a8;

            lock (_rnd)
            {
                a0 = (byte)_rnd.Next(0, 256);
                a1 = (byte)_rnd.Next(0, 256);
                a2 = (byte)_rnd.Next(0, 256);
                a3 = (byte)_rnd.Next(0, 256);
                a4 = (byte)_rnd.Next(0, 256);
                a5 = (byte)_rnd.Next(0, 256);
                a6 = (byte)_rnd.Next(0, 256);
                a7 = (byte)_rnd.Next(0, 256);
                a8 = (byte)_rnd.Next(0, 256);
            }

            byte[] byte16 = new byte[16] {
                (byte) ((millisecondsWithCounter  >> 48) & 0b_1111_1111_1111),
                (byte) ((millisecondsWithCounter  >> 40) & 0b_1111_1111_1111),
                (byte) ((millisecondsWithCounter  >> 32) & 0b_1111_1111_1111),
                (byte) ((millisecondsWithCounter  >> 24) & 0b_1111_1111_1111),
                (byte) ((millisecondsWithCounter  >> 16) & 0b_1111_1111_1111),
                (byte) ((millisecondsWithCounter  >>  8) & 0b_1111_1111_1111),
                (byte) ((millisecondsWithCounter  >>  0) & 0b_1111_1111_1111),
                a0, a1, a2,a3, a4, a5, a6, a7, a8 };

            return new Guid(byte16);
        }

        /// <summary>
        /// Create a SequentialGuid from the supplied timestamp. The counter portion will be zero.
        /// Not guaranteed unique timestamp.
        /// </summary>
        /// <param name="timestamp">Timestamp</param>
        /// <returns>Guid with a timestamp</returns>
        public static Guid CreateGuid(UnixTimeUtc timestamp)
        {
            UInt64 millisecondsctr = (UInt64)(timestamp.milliseconds << 12) | 0;

            return CreateGuidFromMillisecond(millisecondsctr);
        }


        /// <summary>
        /// Create a SequentialGuid using Now() as a timestamp with a counter. Guaranteed unique timestamp.
        /// The timestamp is 44 bits, the counter is 12 bits (7 bytes, 9 bytes random). 
        /// Able to hold 557 years since 1970-01-01
        /// The counter yields ~1/4ns per guid before clash at which time it sleeps for a millisecond and does a recursive call.
        /// </summary>
        /// <returns></returns>
        public static Guid CreateGuid()
        {
            UnixTimeUtc ts = new UnixTimeUtc();

            lock (_lock)
            {
                if (ts.Equals(_lastMillisecond))
                {
                    //  bits counter 12 bits, aka 1111-1111-1111 / 0b_1111_1111_1111F; 4.1M max / second, 1/4 ns
                    _counter++;
                    if (_counter >= 0b_1111_1111_1111)
                    {
                        Thread.Sleep(1);
                        // http://msdn.microsoft.com/en-us/library/c5kehkcz.aspx
                        // A lock knows which thread locked it. If the same thread comes again it just increments a counter and does not block.
                        // Call recursively to try and get a new timestamp again
                        return CreateGuid();
                    }
                }
                else
                {
                    _lastMillisecond = ts;
                    _counter = 0;
                }
            }

            // Create 56 bits (7 bytes) {miliseconds (44 bits), _counter(12 bits)}
            UInt64 millisecondsctr = (UInt64)(ts.milliseconds << 12) | (UInt32)_counter;

            return CreateGuidFromMillisecond(millisecondsctr);
        }

        public static UnixTimeUtc ToUnixTimeUtc(Guid fileid)
        {
            byte[] fibytes = fileid.ToByteArray();

            UInt64 t = (((UInt64)fibytes[0]) << 48) | (((UInt64)fibytes[1]) << 40) | (((UInt64)fibytes[2]) << 32) |
                        (((UInt64)fibytes[3]) << 24) | (((UInt64)fibytes[4]) << 16) | (((UInt64)fibytes[5]) << 8) |
                        (UInt64)fibytes[6];

            t = t >> 12;

            Int64 st = (Int64) t;

            return new UnixTimeUtc(st);
        }
    }
}   