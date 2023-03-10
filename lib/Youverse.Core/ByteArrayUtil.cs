using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Youverse.Core
{
    public static class ByteArrayUtil
    {
        public static byte[] UInt32ToBytes(UInt32 i)
        {
            return new byte[] { (byte)(i >> 24 & 0xFF), (byte)(i >> 16 & 0xFF), (byte)(i >> 8 & 0xFF), (byte)(i & 0xFF) };
        }

        public static byte[] UInt64ToBytes(UInt64 i)
        {
            return new byte[] { (byte)(i >> 56 & 0xFF), (byte)(i >> 48 & 0xFF), (byte)(i >> 40 & 0xFF), (byte)(i >> 32 & 0xFF), (byte)(i >> 24 & 0xFF), (byte)(i >> 16 & 0xFF), (byte)(i >> 8 & 0xFF), (byte)(i & 0xFF) };
        }

        public static byte[] Int32ToBytes(Int32 i)
        {
            return new byte[] { (byte)(i >> 24 & 0xFF), (byte)(i >> 16 & 0xFF), (byte)(i >> 8 & 0xFF), (byte)(i & 0xFF) };
        }

        public static byte[] Int64ToBytes(Int64 i)
        {
            return new byte[] { (byte)(i >> 56 & 0xFF), (byte)(i >> 48 & 0xFF), (byte)(i >> 40 & 0xFF), (byte)(i >> 32 & 0xFF), (byte)(i >> 24 & 0xFF), (byte)(i >> 16 & 0xFF), (byte)(i >> 8 & 0xFF), (byte)(i & 0xFF) };
        }

        public static byte[] Combine(byte[] first, byte[] second)
        {
            byte[] ret = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, ret, 0, first.Length);
            Buffer.BlockCopy(second, 0, ret, first.Length, second.Length);
            return ret;
        }

        public static (byte[] part1, byte[] part2) Split(byte[] data, int len1, int len2)
        {
            var part1 = new byte[len1];
            var part2 = new byte[len2];
    
            Buffer.BlockCopy(data, 0, part1, 0, len1);
            Buffer.BlockCopy(data, len1, part2, 0, len2);

            return (part1, part2);
        }

        public static byte[] Combine(byte[] first, byte[] second, byte[] third)
        {
            byte[] ret = new byte[first.Length + second.Length + third.Length];
            Buffer.BlockCopy(first, 0, ret, 0, first.Length);
            Buffer.BlockCopy(second, 0, ret, first.Length, second.Length);
            Buffer.BlockCopy(third, 0, ret, first.Length + second.Length, third.Length);

            return ret;
        }

        public static (byte[] part1, byte[] part2, byte[] part3) Split(byte[] data, int len1, int len2, int len3)
        {
            var part1 = new byte[len1];
            var part2 = new byte[len2];
            var part3 = new byte[len3];

            Buffer.BlockCopy(data, 0, part1, 0, len1);
            Buffer.BlockCopy(data, len1, part2, 0, len2);
            Buffer.BlockCopy(data, len1 + len2, part3, 0, len3);

            return (part1, part2, part3);
        }

        public static string PrintByteArray(byte[] bytes)
        {
            var sb = new StringBuilder("new byte[] { ");
            foreach (var b in bytes)
            {
                sb.Append(b + ", ");
            }

            sb.Append("}");
            return sb.ToString();
        }


        // Oh memset() oh memset().... I love memset()... Why write fancy for loops when
        // you can brutally use memset... I know the answer. But I still love memset(). 
        public static void WipeByteArray(byte[] b)
        {
            for (int i = 0; i < b.Length; i++)
                b[i] = 0;
        }

        /// <summary>
        /// Returns a cryptographically strong random Guid
        /// </summary>
        /// <returns></returns>
        public static Guid GetRandomCryptoGuid()
        {
            return new Guid(GetRndByteArray(16));
        }

        /// <summary>
        /// Returns true if key is strong, false if it appears constructed or wrong
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static bool IsStrongKey(byte[] data)
        {
            if (data == null || data.Length < 16)
                return false;

            int j = 0;

            // Keys like this are considered weak "nnnn mmmm oooo pppp"
            for (int i = 0; i < data.Length / 4; i++, j+=4)
            {
                if ((data[j] != data[j + 1]) ||
                    (data[j] != data[j + 2]) ||
                    (data[j] != data[j + 3]))
                    return true;
            }

            if (data.Length % 4 != 0)
            {
                // If the key is an odd size then let's just see if the last
                // bytes are the same as the byte before
                for (int i = 0; i < data.Length % 4; i++)
                {
                    if (data[j-1] != data[j + i])
                        return true;
                }

            }

            return false;
        }

        /// <summary>
        /// Generates a cryptographically safe (?) array of random bytes. To be used for XORing private keys
        /// </summary>
        /// <param name="nCount">Number of bytes (should be as long as data to XOR</param>
        /// <returns>Array of random bytes of the specified length</returns>
        public static byte[] GetRndByteArray(int nCount)
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] byteArray = new byte[nCount];
                rng.GetBytes(byteArray);
                return byteArray;
            }
        }

        /// <summary>
        /// Check if two byte arrays of equal length are identical. Tempting to use memcmp() ;)
        /// </summary>
        /// <param name="ba1"></param>
        /// <param name="ba2"></param>
        /// <returns>True if identical, false otherwise</returns>
        public static bool EquiByteArrayCompare(byte[] ba1, byte[] ba2)
        {
            if (ba1.Length != ba2.Length)
                throw new ArgumentException("Byte arrays are not the same length");

            int i = 0;
            while (i < ba1.Length)
            {
                if (ba1[i] != ba2[i])
                    return false;
                i++;
            }

            return true;
        }

        /// <summary>
        /// XOR the two byte arrays with each other. Requires the same length.
        /// </summary>
        /// <param name="ba1"></param>
        /// <param name="ba2"></param>
        /// <returns>The XOR'ed byte array</returns>
        public static byte[] EquiByteArrayXor(byte[] ba1, byte[] ba2)
        {
            if (ba1.Length != ba2.Length)
                throw new ArgumentException("Byte arrays are not the same length");

            byte[] ra = new byte[ba1.Length];
            int i = 0;
            while (i < ba1.Length)
            {
                ra[i] = (byte)(ba1[i] ^ ba2[i]);
                i++;
            }

            return ra;
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

        /// <summary>
        /// memcmp for two Guids.
        /// </summary>
        /// <param name="b1"></param>
        /// <param name="b2"></param>
        /// <returns>+1 : b1 > b2; 0 equal; -1 : b1 < b2</returns>
        public static int muidcmp(Guid? b1, Guid? b2)
        {
            return muidcmp(b1?.ToByteArray(), b2?.ToByteArray());
        }
    }
}