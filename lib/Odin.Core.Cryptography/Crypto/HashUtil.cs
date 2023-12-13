using System;
using System.IO;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace Odin.Core.Cryptography.Crypto
{
    public static class HashUtil
    {
        public const string SHA256Algorithm = "SHA-256";

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

        public static (byte[], Int64) StreamSHA256(Stream inputStream, byte[] optionalNonce = null)
        {
            using (var hasher = SHA256.Create())
            {
                // if nonce is provided, compute hash of nonce first
                if (optionalNonce != null)
                {
                    hasher.TransformBlock(optionalNonce, 0, optionalNonce.Length, null, 0);
                }

                Int64 streamLength = 0;

                byte[] buffer = new byte[8192];
                int bytesRead;
                while ((bytesRead = inputStream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    hasher.TransformBlock(buffer, 0, bytesRead, null, 0);
                    streamLength += bytesRead;
                }

                // finalize the hash computation
                hasher.TransformFinalBlock(buffer, 0, 0);

                return (hasher.Hash, streamLength);
            }
        }
    }
}