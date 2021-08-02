using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Scratchpad
{
    class Program
    {
        static void Main(string[] args)
        {
            RunCalcs();
        }

        static void RunCalcs()
        {
            const string frodo = "frodo baggins";
            GetId(frodo);
            GetId("frodo BAGGINS");
        }

        static Guid GetId(string input)
        {
            string lcase = input.ToLower();
            var bytes = CalculateMD5Hash(lcase);
            var id = new Guid(bytes);

            Console.WriteLine($"Bytes for [{input}]: {string.Join(" ", bytes)}");
            Console.WriteLine($"Guid Id for [{input}]: {id}");
            return id;
            
        }
        
        static byte[] CalculateMD5Hash(string input)
        {
            using MD5 hashAlgo = MD5.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var result = hashAlgo.ComputeHash(bytes);
            return result;
        }
    }
}