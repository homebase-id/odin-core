using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

// Enable testing
[assembly: InternalsVisibleTo("Odin.Core.Cryptography.Tests")]

namespace Odin.Core.Cryptography.Crypto
{
    /// <summary>
    /// </summary>
    public static class AesCbc
    {
        private static byte[] PerformCryptography(byte[] data, ICryptoTransform cryptoTransform)
        {
            using (var ms = new MemoryStream())
            using (var cryptoStream = new CryptoStream(ms, cryptoTransform, CryptoStreamMode.Write))
            {
                cryptoStream.Write(data, 0, data.Length);
                cryptoStream.FlushFinalBlock();

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Do not use this function unless you specifically need to reencrypt with the same IV.
        /// This is only needed when we transform the header.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="key"></param>
        /// <param name="iv"></param>
        /// <returns></returns>
        public static byte[] Encrypt(byte[] data, SensitiveByteArray key, byte[] iv)
        {
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = key.GetKey();

                aesAlg.IV = iv;

                aesAlg.Mode = CipherMode.CBC;

                using (var encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV))
                {
                    return PerformCryptography(data, encryptor);
                }
            }
        }

        //public static (byte[] IV, byte[] ciphertext) Encrypt(byte[] data, byte[] Key)
        public static (byte[] IV, byte[] ciphertext) Encrypt(byte[] data, SensitiveByteArray Key)
        {
            byte[] IV;

            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Key.GetKey();

                aesAlg.GenerateIV();
                IV = aesAlg.IV;

                aesAlg.Mode = CipherMode.CBC;

                using (var encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV))
                {
                    return (IV, PerformCryptography(data, encryptor));
                }
            }
        }

        public static byte[] Decrypt(byte[] cipherText, SensitiveByteArray Key, byte [] IV)
        {
            // Create an Aes object 
            // with the specified key and IV. 
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Key.GetKey();
                aesAlg.IV = IV;
                aesAlg.Mode = CipherMode.CBC;

                using (var decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV))
                {
                    return PerformCryptography(cipherText, decryptor);
                }
            }
        }
    }
}
