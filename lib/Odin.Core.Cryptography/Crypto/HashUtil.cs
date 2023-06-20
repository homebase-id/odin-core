using System;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;


namespace Odin.Core.Cryptography.Crypto
{
    public static class HashUtil
    {
        public static byte[] Hkdf(byte[] sharedEccSecret, byte[] salt, int outputKeySize)
        {
            if (sharedEccSecret == null)
                throw new ArgumentNullException(nameof(sharedEccSecret));

            if (salt == null)
                throw new ArgumentNullException(nameof(salt));

            if (outputKeySize < 16)
                throw new ArgumentException("Output key size cannot be less than 16", nameof(outputKeySize));

            // Create an instance of HKDFBytesGenerator with SHA-256
            HkdfBytesGenerator hkdf = new HkdfBytesGenerator(new Sha256Digest());

            // Initialize the generator
            hkdf.Init(new HkdfParameters(sharedEccSecret, salt, null));

            // Create a byte array for the output key
            byte[] outputKey = new byte[outputKeySize];

            // Generate the key
            hkdf.GenerateBytes(outputKey, 0, outputKey.Length);

            return outputKey;
        }
        public static byte[] FileSHA256(string fileName)
        {
            using (var stream = System.IO.File.OpenRead(fileName))
            {
                using (var hasher = SHA256.Create())
                {
                    return hasher.ComputeHash(stream);
                }
            }
        }
    }
}