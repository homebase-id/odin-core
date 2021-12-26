using System;
using System.IO;
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
            var datetime = new DateTimeOffset(2021, 7, 21, 23, 59, 59, 999, TimeSpan.Zero);
            var g = CreateTimeBasedGuid(datetime);
            Console.WriteLine(g);
            Console.WriteLine(CreateTimeBasedGuid(datetime.AddDays(1).AddMinutes(7)));
            Console.WriteLine(CreateTimeBasedGuid(datetime.AddDays(1).AddHours(3).AddMinutes(5)));
            Console.WriteLine(CreateTimeBasedGuid(datetime.AddDays(2)));
            Console.WriteLine(CreateTimeBasedGuid(datetime.AddMonths(1)));

            var parts = g.ToString().Split("-");
            var yearMonthDay = parts[0];
            var year = yearMonthDay.Substring(0, 4);
            var month = yearMonthDay.Substring(4, 2);
            var day = yearMonthDay.Substring(6, 2);
            var hourMinute = parts[1];

            string dir = Path.Combine( year, month, day, hourMinute, g.ToString());
            Console.WriteLine($"Path will be:[{dir}]");
        }

        static Guid CreateTimeBasedGuid(DateTimeOffset datetime)
        {
            var random = new Random();
            var rnd = new byte[7];
            random.NextBytes(rnd);

            //byte[] offset = BitConverter.GetBytes(datetime.Offset.TotalMinutes);
            var year = BitConverter.GetBytes((short)datetime.Year);
            var bytes = new byte[16]
            {
                (byte)datetime.Day,
                (byte)datetime.Month,
                year[0],
                year[1],
                (byte)datetime.Minute,
                (byte)datetime.Hour,
                (byte)datetime.Second,
                (byte)datetime.Millisecond,
                255, //variant: unknown
                rnd[0],
                rnd[1],
                rnd[2],
                rnd[3],
                rnd[4],
                rnd[5],
                rnd[6]
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