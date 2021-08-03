using System;
using System.Security.Cryptography;
using System.Text;

namespace DotYou.Kernel.Cryptography
{
    public static class YFByteArray
    {
        public static byte[] Combine(byte[] first, byte[] second)
        {
            byte[] ret = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, ret, 0, first.Length);
            Buffer.BlockCopy(second, 0, ret, first.Length, second.Length);
            return ret;
        }
        public static byte[] Combine(byte[] first, byte[] second, byte[] third)
        {
            byte[] ret = new byte[first.Length + second.Length + third.Length];
            Buffer.BlockCopy(first,  0, ret, 0, first.Length);
            Buffer.BlockCopy(second, 0, ret, first.Length, second.Length);
            Buffer.BlockCopy(third,  0, ret, first.Length + second.Length, third.Length);

            return ret;
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
        /// Generates a cryptographically safe (?) array of random bytes. To be used for XORing private keys
        /// </summary>
        /// <param name="nCount">Number of bytes (should be as long as data to XOR</param>
        /// <returns>Array of random bytes of the specified length</returns>
        public static byte[] GetRndByteArray(int nCount)
        {
            RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider();

            byte[] byteArray = new byte[nCount];
            rng.GetBytes(byteArray);

            return byteArray;
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
                ra[i] = (byte) (ba1[i] ^ ba2[i]);
                i++;
            }

            return ra;
        }
    }
}


