using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;


namespace DotYou.Kernel.Cryptography
{
    /// <summary>
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

                var (tIV, cipher) = EncryptBytesToBytes_Aes(testData, key, iv);
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

        // Only used for test purposes to cross validate JS & .NET with IV as parameter
        private static (byte[] IV, byte[] ciphertext) EncryptBytesToBytes_Aes(byte[] data, byte[] key, byte[] iv)
        {
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = key;

                aesAlg.IV = iv;

                aesAlg.Mode = CipherMode.CBC;

                using (var encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV))
                {
                    return (iv, PerformCryptography(data, encryptor));
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
