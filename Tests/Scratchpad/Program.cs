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
            Console.WriteLine(CreateTimeBasedGuid(DateTimeOffset.UtcNow));
            Console.WriteLine(CreateTimeBasedGuid(DateTimeOffset.UtcNow.AddMinutes(31)));
            Console.WriteLine(CreateTimeBasedGuid(DateTimeOffset.UtcNow.AddMinutes(31)));
            Console.WriteLine(CreateTimeBasedGuid(DateTimeOffset.UtcNow.AddHours(1)));

            var parts = CreateTimeBasedGuid(DateTimeOffset.UtcNow).ToString().Split("-");
            Array.Resize(ref parts, 3);
            Console.WriteLine(string.Join("-", parts));
        }

        static Guid CreateTimeBasedGuid(DateTimeOffset datetime)
        {
            var random = new Random();
            var rnd = new byte[5];
            random.NextBytes(rnd);

            //byte[] offset = BitConverter.GetBytes(datetime.Offset.TotalMinutes);
            var year = BitConverter.GetBytes((short)datetime.Year);
            var bytes = new byte[16]
            {
                year[0],
                year[1],
                (byte)datetime.Month,
                (byte)datetime.Day,
                (byte)datetime.Hour,
                (byte)datetime.Minute,
                0,
                0,
                255, //variant: unknown
                (byte)datetime.Second,
                (byte)datetime.Millisecond,
                rnd[0],
                rnd[1],
                rnd[2],
                rnd[3],
                rnd[4]
            };

            // set the version to be compliant with rfc; not sure it matters
            bytes[7] &= 0x0f;
            bytes[7] |= 0x04 << 4;

            return new Guid(bytes);
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

            Console.WriteLine($"b1Reduced: {string.Join(" ", b1Reduced)}");
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