using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Youverse.Core.Cryptography;

namespace Scratchpad
{
    class Program
    {
        static void Main(string[] args)
        {

            byte[] iv = new byte[16];
            var key = new byte[16];
            
            Array.Fill(iv,(byte)1);
            Array.Fill(key, (byte)1);
            
            RunSHA256Calcs();

        }

        static void RunSHA256Calcs()
        {
            const string domain = "frodobaggins.me";
            // const string domain = "it is time to test a realky really long 03819~casidsd º™¶£ string";
            string domain2 = domain.Replace(domain.Substring(2, 4), domain.Substring(2, 4).ToUpper());
            Console.WriteLine($"domain2: {domain2}");

            var b1 = CalculateSHA256Hash(domain);
            var b2 = CalculateSHA256Hash(domain);
            var fullSizeMatch = ByteArrayUtil.EquiByteArrayCompare(b1, b2);

            var b1Reduced = ReduceSHA256Hash(b1);
            var b2Reduced = ReduceSHA256Hash(b2);
            
            Console.WriteLine($"b1Reduced: {string.Join(" ",b1Reduced)}");
            Console.WriteLine($"b2Reduced: {string.Join(" ", b2Reduced)}");

            var reducedMatch = ByteArrayUtil.EquiByteArrayCompare(b1Reduced, b2Reduced);

            Console.WriteLine($"full size match:{fullSizeMatch}");
            Console.WriteLine($"reduced match {reducedMatch}");
            Console.WriteLine($"Reduced Value:{string.Join(" ", b1Reduced)}");
            Console.WriteLine($"Reduced Guid: {new Guid(b1Reduced)}");
        }
        
        static byte[] CalculateSHA256Hash(string input)
        {
            var adjustedInput = input.ToLower();
            using (SHA256 sha256Hash = SHA256.Create())
            {
                // ComputeHash - returns byte array  
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(adjustedInput));
                return bytes;
            }
        }

        static byte[] ReduceSHA256Hash(byte[] bytes)
        {
            var half = bytes.Length / 2;
            var (part1, part2) = ByteArrayUtil.Split(bytes, half, half);
            var reducedBytes = ByteArrayUtil.EquiByteArrayXor(part1, part2);
            return reducedBytes;
        }
    }
}