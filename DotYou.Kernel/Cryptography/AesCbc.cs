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

                    var (IV, encrypted)= EncryptStringToBytes_Aes(original, key);

                    // Decrypt the bytes to a string. 
                    string roundtrip = DecryptStringFromBytes_Aes(encrypted, key, IV);

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

        static (byte[] IV, byte[] ciphertext) EncryptStringToBytes_Aes(string plainText, byte[] Key)
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

            // Return the encrypted bytes from the memory stream. 
            return (IV, encrypted);

        }

        static string DecryptStringFromBytes_Aes(byte[] cipherText, byte[] Key, byte [] IV)
        {

            // Declare the string used to hold 
            // the decrypted text. 
            string plaintext = null;

            // Create an Aes object 
            // with the specified key and IV. 
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Key;
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
}
