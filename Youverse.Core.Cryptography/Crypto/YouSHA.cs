using System;
using System.Security.Cryptography;
using System.Text;

namespace Youverse.Core.Cryptography.Crypto
{
    public static class YouSHA
    {
        public static byte[] CalculateSHA256Hash(byte[] input)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                // ComputeHash - returns byte array  
                byte[] bytes = sha256Hash.ComputeHash(input);
                return bytes;
            }
        }

        public static byte[] ReduceSHA256Hash(byte[] input)
        {
            var bytes = CalculateSHA256Hash(input);
            var half = bytes.Length / 2;
            var (part1, part2) = ByteArrayUtil.Split(bytes, half, half);
            var reducedBytes = ByteArrayUtil.EquiByteArrayXor(part1, part2);
            return reducedBytes;
        }
    }
}
