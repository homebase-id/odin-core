using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;


namespace DotYou.Kernel.Cryptography
{
    /// <summary>
    /// We may want to switch to AES-CTR. Not sure. If we do, AES CTR is not supported by
    /// .NET so we'll have to write a wrapper around it. There's example code online to do this.
    /// 
    ///  Hmmm... where was it I found this snippet :-D
    /// </summary>
    public static class AesCbc
    {
        public static bool TestPrivate()
        {
            try
            {
                var key = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
                var iv = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
                var testData = new byte[] { 162, 146, 244, 255, 127, 128, 0, 42, 7, 0 };

                var cipher = EncryptBytesToBytes_Aes(testData, key, iv);

                var s = YFByteArray.PrintByteArray(cipher);
                Console.WriteLine("Cipher: " + s);
                var roundtrip = DecryptBytesFromBytes_Aes(cipher, key, iv);

                if (YFByteArray.EquiByteArrayCompare(roundtrip, testData))
                    return true;
                else
                    return false;
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: {0}", e.Message);
            }

            return false;
        }

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
        public static byte[] EncryptBytesToBytes_Aes(byte[] data, byte[] key, byte[] iv)
        {
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = key;

                aesAlg.IV = iv;

                aesAlg.Mode = CipherMode.CBC;

                using (var encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV))
                {
                    return PerformCryptography(data, encryptor);
                }
            }
        }

        public static (byte[] IV, byte[] ciphertext) EncryptBytesToBytes_Aes(byte[] data, byte[] Key)
        {
            byte[] IV;

            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Key;

                aesAlg.GenerateIV();
                IV = aesAlg.IV;

                aesAlg.Mode = CipherMode.CBC;

                using (var encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV))
                {
                    return (IV, PerformCryptography(data, encryptor));
                }
            }
        }

        public static (byte[] IV, byte[] ciphertext) EncryptStringToBytes_Aes(string plainText, byte[] Key)
        {
            return EncryptBytesToBytes_Aes(Encoding.UTF8.GetBytes(plainText), Key);
        }

        public static byte[] DecryptBytesFromBytes_Aes(byte[] cipherText, byte[] Key, byte [] IV)
        {
            // Create an Aes object 
            // with the specified key and IV. 
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Key;
                aesAlg.IV = IV;
                aesAlg.Mode = CipherMode.CBC;

                using (var decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV))
                {
                    return PerformCryptography(cipherText, decryptor);
                }
            }
        }

        public static string DecryptStringFromBytes_Aes(byte[] cipherText, byte[] Key, byte[] IV)
        {
            return Encoding.UTF8.GetString(DecryptBytesFromBytes_Aes(cipherText, Key, IV));
        }
    }
}
