using System;
using System.Threading;

namespace Youverse.Core
{
    /// <summary>
    /// Might consider switching to UnixTime.UnixTimeMillisecondsUnique()
    /// because the ms resolution will give less issues in a multi-server setup.
    /// </summary>
    public static class SequentialGuid
    {
        static private Object _lock = new Object();
        static private Random _rnd = new Random();
        static private UInt64 _lastMilisecond = 0;
        static private int _counter = 0;

        public static byte[] CreateGuid(UInt64 unixTimeMiliseconds)
        {
            // One year is 3600*24*365.25*1000 = 31,557,600,000 miliseconds (35 bits)
            // Use 9 bits for the years, for a total of 44 bits (5½ bytes)
            // Thus able to hold 557 years since 1970-01-01
            // The counter is 12 bits, for a total of 4096, which gets us to ~1/4ns per guid before clash / wait()
            // Total bit usage of milisecond time+counter is thus 44+12=56 bits aka 7 bytes

            lock (_lock)
            {
                if (unixTimeMiliseconds == _lastMilisecond)
                {
                    //  bits counter 12 bits, aka 1111-1111-1111 / 0xFFF; 4.1M max / second, 1/4 ns
                    _counter++;
                    if (_counter >= 0xFFF)
                    {
                        Thread.Sleep(1);
                        // http://msdn.microsoft.com/en-us/library/c5kehkcz.aspx
                        // A lock knows which thread locked it. If the same thread comes again it just increments a counter and does not block.
                        return CreateGuid();
                    }
                }
                else
                {
                    _lastMilisecond = unixTimeMiliseconds;
                    _counter = 0;
                }
            }

            // Create 56 bits (7 bytes) {miliseconds (44 bits), _counter(12 bits)}
            UInt64 milisecondsctr = (UInt64)(unixTimeMiliseconds << 12) | (UInt32)_counter;

            byte[] byte16 = new byte[16] {
            (byte) ((milisecondsctr  >> 48) & 0xFF),
            (byte) ((milisecondsctr  >> 40) & 0xFF),
            (byte) ((milisecondsctr  >> 32) & 0xFF),
            (byte) ((milisecondsctr  >> 24) & 0xFF),
            (byte) ((milisecondsctr  >> 16) & 0xFF),
            (byte) ((milisecondsctr  >>  8) & 0xFF),
            (byte) ((milisecondsctr  >>  0) & 0xFF),
            (byte) _rnd.Next(0,255),
            (byte) _rnd.Next(0,255), (byte) _rnd.Next(0,255),
            (byte) _rnd.Next(0,255), (byte) _rnd.Next(0,255),
            (byte) _rnd.Next(0,255), (byte) _rnd.Next(0,255),
            (byte) _rnd.Next(0,255), (byte) _rnd.Next(0,255)};

            return byte16;
        }


        public static byte[] CreateGuid()
        {
            return CreateGuid(UnixTime.GetUnixTimeMilliseconds());
        }

        public static UInt64 FileIdToUnixTime(Guid fileid)
        {
            byte[] fibytes = fileid.ToByteArray();

            UInt64 i = (((UInt64)fibytes[0]) << 48) | (((UInt64)fibytes[1]) << 40) | (((UInt64)fibytes[2]) << 32) |
                        (((UInt64)fibytes[3]) << 24) | (((UInt64)fibytes[4]) << 16) | (((UInt64)fibytes[5]) << 8) |
                        (UInt64)fibytes[6];

            i = i >> 12;

            return i;
        }

        // memcmp for two 16 byte arrays
        // 1 : b1 > b2; 0 equal; -1 : b1 < b2
        public static int muidcmp(byte[] b1, byte[] b2)
        {
            if ((b1 == null) || (b2 == null))
            {
                if (b1 == b2)
                    return 0;
                else if (b1 == null)
                    return -1;
                else
                    return +1;
            }

            if ((b1.Length != 16) || (b2.Length != 16))
                throw new Exception("b1,b2 must be 16 bytes");

            for (int i = 0; i < 16; i++)
            {
                if (b1[i] == b2[i])
                    continue;
                if (b1[i] > b2[i])
                    return 1; // b1 larger than b2
                else
                    return -1; // b2 larger than b1
            }

            return 0;
        }
    }
}   