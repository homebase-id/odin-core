using System;
using System.Security.Cryptography;
using System.Text;

namespace DotYou.Types
{
    /// <summary>
    /// Various Utilities for the prototrial
    /// </summary>
    public static class MiscUtils
    {
        /// <summary>
        /// Creates a Guid from an MD5 hash.  The input will be lower-cased
        /// such that Frodo Baggins and frodo baggins will create the same Guid
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static Guid MD5HashToGuid(string input)
        {
            string lcase = input.ToLower();
            var bytes = CalculateMD5Hash(lcase);
            var id = new Guid(bytes);
            // Console.WriteLine($"Bytes for [{input}]: {string.Join(" ", bytes)}");
            // Console.WriteLine($"Guid Id for [{input}]: {id}");
            return id;
            
        }
        
        private static byte[] CalculateMD5Hash(string input)
        {
            using MD5 hashAlgo = MD5.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var result = hashAlgo.ComputeHash(bytes);
            return result;
        }
    }
}