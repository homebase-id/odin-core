using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;


// https://docs.microsoft.com/en-us/dotnet/api/system.security.cryptography.rijndaelmanaged?redirectedfrom=MSDN&view=net-5.0
//
namespace DotYou.Kernel.Cryptography
{

    public static class YFRijndaelWrap
    {
        public static byte[] EncryptStringToBytes(string plainText, byte[] Key, byte[] IV)
        {
            // Check arguments.
            if (plainText == null || plainText.Length <= 0)
                throw new ArgumentNullException("plainText");
            if (Key == null || Key.Length <= 0)
                throw new ArgumentNullException("Key");
            if (IV == null || IV.Length <= 0)
                throw new ArgumentNullException("IV");

            byte[] encrypted;

            // Create an RijndaelManaged object
            // with the specified key and IV.
            using (RijndaelManaged rijAlg = new RijndaelManaged())
            {
                rijAlg.Key = Key;
                rijAlg.IV = IV;

                // Create an encryptor to perform the stream transform.
                ICryptoTransform encryptor = rijAlg.CreateEncryptor(rijAlg.Key, rijAlg.IV);

                // Create the streams used for encryption.
                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                        {

                            //Write all data to the stream.
                            swEncrypt.Write(plainText);
                        }
                        encrypted = msEncrypt.ToArray();
                    }
                }
            }

            // Return the encrypted bytes from the memory stream.
            return encrypted;
        }


        public static string DecryptStringFromBytes(byte[] cipherText, byte[] Key, byte[] IV)
        {
            // Check arguments.
            if (cipherText == null || cipherText.Length <= 0)
                throw new ArgumentNullException("cipherText");
            if (Key == null || Key.Length <= 0)
                throw new ArgumentNullException("Key");
            if (IV == null || IV.Length <= 0)
                throw new ArgumentNullException("IV");

            // Declare the string used to hold
            // the decrypted text.
            string plaintext = null;

            // Create an RijndaelManaged object
            // with the specified key and IV.
            using (RijndaelManaged rijAlg = new RijndaelManaged())
            {
                rijAlg.Key = Key;
                rijAlg.IV = IV;

                // Create a decryptor to perform the stream transform.
                ICryptoTransform decryptor = rijAlg.CreateDecryptor(rijAlg.Key, rijAlg.IV);

                // Create the streams used for decryption.
                using (MemoryStream msDecrypt = new MemoryStream(cipherText))
                {
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                        {
                            // Read the decrypted bytes from the decrypting stream
                            // and place them in a string.
                            plaintext = srDecrypt.ReadToEnd();
                        }
                    }
                }
            }

            return plaintext;
        }
    }

    /// <summary>
    ///  Hmmm... where was it I found this snippet :-D
    /// </summary>
    public static class TestAesCbc
    {
        public static void Test()
        {
            try
            {

                string original = "Here is some data to encrypt!";

                // Create a new instance of the Aes 
                // class.  This generates a new key and initialization  
                // vector (IV). 
                using (var random = new RNGCryptoServiceProvider())
                {
                    var key = new byte[16];
                    random.GetBytes(key);

                    // Encrypt the string to an array of bytes. 
                    byte[] encrypted = EncryptStringToBytes_Aes(original, key);

                    // Decrypt the bytes to a string. 
                    string roundtrip = DecryptStringFromBytes_Aes(encrypted, key);

                    //Display the original data and the decrypted data.
                    Console.WriteLine("Original:   {0}", original);
                    Console.WriteLine("Encrypted (b64-encode): {0}", Convert.ToBase64String(encrypted));
                    Console.WriteLine("Round Trip: {0}", roundtrip);
                }

            }
            catch (Exception e)
            {
                Console.WriteLine("Error: {0}", e.Message);
            }
        }

        static byte[] EncryptStringToBytes_Aes(string plainText, byte[] Key)
        {
            byte[] encrypted;
            byte[] IV;

            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Key;

                aesAlg.GenerateIV();
                IV = aesAlg.IV;

                aesAlg.Mode = CipherMode.CBC;

                var encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

                // Create the streams used for encryption. 
                using (var msEncrypt = new MemoryStream())
                {
                    using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        using (var swEncrypt = new StreamWriter(csEncrypt))
                        {
                            //Write all data to the stream.
                            swEncrypt.Write(plainText);
                        }
                        encrypted = msEncrypt.ToArray();
                    }
                }
            }

            var combinedIvCt = new byte[IV.Length + encrypted.Length];
            Array.Copy(IV, 0, combinedIvCt, 0, IV.Length);
            Array.Copy(encrypted, 0, combinedIvCt, IV.Length, encrypted.Length);

            // Return the encrypted bytes from the memory stream. 
            return combinedIvCt;

        }

        static string DecryptStringFromBytes_Aes(byte[] cipherTextCombined, byte[] Key)
        {

            // Declare the string used to hold 
            // the decrypted text. 
            string plaintext = null;

            // Create an Aes object 
            // with the specified key and IV. 
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Key;

                byte[] IV = new byte[aesAlg.BlockSize / 8];
                byte[] cipherText = new byte[cipherTextCombined.Length - IV.Length];

                Array.Copy(cipherTextCombined, IV, IV.Length);
                Array.Copy(cipherTextCombined, IV.Length, cipherText, 0, cipherText.Length);

                aesAlg.IV = IV;

                aesAlg.Mode = CipherMode.CBC;

                // Create a decrytor to perform the stream transform.
                ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

                // Create the streams used for decryption. 
                using (var msDecrypt = new MemoryStream(cipherText))
                {
                    using (var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        using (var srDecrypt = new StreamReader(csDecrypt))
                        {

                            // Read the decrypted bytes from the decrypting stream
                            // and place them in a string.
                            plaintext = srDecrypt.ReadToEnd();
                        }
                    }
                }

            }

            return plaintext;

        }
    }

    // https://github.com/scottbrady91/Blog-Example-Classes/blob/master/AesGcmEncryption/Program.cs
    //
    public static class MyAes
    {
        public static void Test()
        {
            var plaintextBytes = new byte[8192];
            RandomNumberGenerator.Fill(plaintextBytes);
            var plaintext = Convert.ToBase64String(plaintextBytes);

            var key = new byte[32];
            RandomNumberGenerator.Fill(key);

            var (ciphertext, nonce, tag) = EncryptWithNet(plaintext, key);
            var decryptedPlaintext = DecryptWithNet(ciphertext, nonce, tag, key);

            if (decryptedPlaintext.Equals(plaintext))
                Console.WriteLine("Decryption succesful!");
            else
                Console.WriteLine("Error!");
        }

        private static (byte[] ciphertext, byte[] nonce, byte[] tag) EncryptWithNetStream(string plainText, byte[] key)
        {
            if (plainText == null || plainText.Length <= 0)
                throw new ArgumentNullException("plainText");
            if (key == null || key.Length <= 0)
                throw new ArgumentNullException("Key");

            using (var aes = new AesGcm(key))
            {
                var nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
                RandomNumberGenerator.Fill(nonce);

                var tag = new byte[AesGcm.TagByteSizes.MaxSize];

                var plaintextBytes = Encoding.UTF8.GetBytes(plainText);
                var ciphertext = new byte[plaintextBytes.Length];

                aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

                return (ciphertext, nonce, tag);
            }
        }

        private static (byte[] ciphertext, byte[] nonce, byte[] tag) EncryptWithNet(string plainText, byte[] key)
        {
            if (plainText == null || plainText.Length <= 0)
                throw new ArgumentNullException("plainText");
            if (key == null || key.Length <= 0)
                throw new ArgumentNullException("Key");

            using (var aes = new AesGcm(key))
            {
                var nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
                RandomNumberGenerator.Fill(nonce);

                var tag = new byte[AesGcm.TagByteSizes.MaxSize];

                var plaintextBytes = Encoding.UTF8.GetBytes(plainText);
                var ciphertext = new byte[plaintextBytes.Length];

                aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

                return (ciphertext, nonce, tag);
            }
        }

        private static string DecryptWithNet(byte[] cipherText, byte[] nonce, byte[] tag, byte[] key)
        {
            if (cipherText == null || cipherText.Length <= 0)
                throw new ArgumentNullException("cipherText");
            if (key == null || key.Length <= 0)
                throw new ArgumentNullException("Key");
            if (tag == null || tag.Length <= 0)
                throw new ArgumentNullException("Tag");
            if (nonce == null || nonce.Length <= 0)
                throw new ArgumentNullException("Nonce");

            using (var aes = new AesGcm(key))
            {
                var plaintextBytes = new byte[cipherText.Length];

                aes.Decrypt(nonce, cipherText, tag, plaintextBytes);

                return Encoding.UTF8.GetString(plaintextBytes);
            }
        }
    }
}