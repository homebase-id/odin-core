using DotYou.AdminClient.Extensions;
using System;
using System.Collections.Generic;

namespace DotYou.Kernel.Cryptography
{
    public class NonceEntry
    {
        public UInt32 nonce;
        public UInt64 timestamp;
    }

    public class NonceTable
    {
        public UInt32 lastNonce;
        // public int max;
        // public LinkedList<NonceEntry> entry;
    }


    public static class NonceGrowingManager
    {
        public static byte[] UInt32ToBytes(UInt32 i)
        {
            return new byte[] { (byte)(i >> 24 & 0xFF), (byte)(i >> 16 & 0xFF), (byte)(i >> 8 & 0xFF), (byte)(i & 0xFF) };
        }

        public static UInt32 CalculateNonce(UInt32 nonce, byte[] secret)
        {
            UInt32 calc;

            // Use MD5 or SHA-256 instead
            calc = CRC32C.CalculateCRC32C(0, UInt32ToBytes(nonce));  // I believe sequence matters, predictable int first.
            calc = CRC32C.CalculateCRC32C(calc, secret);

            return calc;
        }


        public static bool ValidateNonce(NonceTable table, UInt32 nonce, UInt32 compareto, byte[] secret)
        {
            if (nonce > table.lastNonce) // This is what we expect
            {
                // Check the nonce - just use CRC32C for now
                var calc = CalculateNonce(nonce, secret);

                if (calc != compareto)
                {
                    // This might indicate an attack.
                    // Let's log it.
                    // Potentially do something
                    return false;
                }

                table.lastNonce = nonce;
                /*                 
                var o = new NonceEntry
                {
                    nonce = nonce,
                    timestamp = DateTimeExtensions.ToDateTimeOffsetSec(0)
                };

                table.entry.AddFirst(o);

                if (table.entry.Count > table.max)
                    table.entry.RemoveLast();
                */
                return true;
            }

            if (nonce == table.lastNonce) // Should be impossible, attack, log?
            {
                throw new Exception("nonce equal to lastNonce");
                return false;
            }

            throw new Exception("nonce less than lastNonce");

            // nonce < table.lastNonce
            //
            // If we can have out of sequence nonces then let's make sure:
            //   a) The nonce is not already present in the table, and
            //   b) The timespan between the nonce and the last table entry closest to the (and less than) nonce
            //      is less than e.g. 10 seconds.


            return false;
        }
    }
}
