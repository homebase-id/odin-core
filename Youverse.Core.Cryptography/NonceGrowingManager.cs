using System;
using System.Security.Cryptography;

namespace Youverse.Core.Cryptography
{
    public class NonceEntry
    {
        public UInt32 nonce;
        public UInt64 timestamp;
    }

    public struct NonceTable // Struct because it's used in a class
    {
        public UInt32 lastNonce;
        // Not needed for now, but maybe if we can get out of sequence nonces
        // we might need to add some logic
        // public int max;
        // public LinkedList<NonceEntry> entry;
    }


    public static class NonceGrowingManager
    {
        // I think this is overkill, wish there was a simple 128 bit hash
        public static byte[] CalculateNonceSHA256(UInt32 nonce, byte[] secret)
        {
            SHA256Managed hashstring = new SHA256Managed();
            byte[] hash = hashstring.ComputeHash(YFByteArray.Combine(YFByteArray.UInt32ToBytes(nonce), secret));

            return hash;
        }

        // I think this is overkill, wish there was a simple 128 bit hash
        public static string CalculateBase64NonceSHA256(UInt32 nonce, byte[] secret)
        {
            return Convert.ToBase64String(CalculateNonceSHA256(nonce, secret));
        }


        public static bool ValidateNonce(NonceTable table, UInt32 nonce, string compareto, byte[] secret)
        {
            if (nonce > table.lastNonce) // This is what we expect
            {
                if ((nonce - table.lastNonce) > 20) 
                {
                    // When you have a shaky network connection some requests might
                    // be lost. We might need to experiment with the margin of jump 
                    // we allow here. I've also considered not incrementing the nonce
                    // with 1, but e.g. random(1,255). In that case this margin needs
                    // to be reconsidered.
                    //
                    return false;
                }

                // Check the nonce - just use CRC32C for now
                var calc = CalculateBase64NonceSHA256(nonce, secret);

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
            }

            throw new Exception("nonce less than lastNonce");

            // nonce < table.lastNonce
            //
            // If we can have out of sequence nonces then let's make sure:
            //   a) The nonce is not already present in the table, and
            //   b) The timespan between the nonce and the last table entry closest to the (and less than) nonce
            //      is less than e.g. 10 seconds.
        }
    }
}
