using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

// Enable testing
[assembly: InternalsVisibleTo("Odin.Core.Cryptography.Tests")]

namespace Odin.Core.Cryptography.Crypto
{
    /// <summary>
    /// AES-GCM implementation for encryption and decryption.
    /// </summary>
    public static class AesGcm
    {
        private const int NonceSize = 12; // AES-GCM recommended nonce size
        private const int TagSize = 16;   // AES-GCM tag size

        /// <summary>
        /// Encrypts data using AES-GCM with a specified IV.
        /// Do not use this function unless you specifically need to reencrypt with the same IV.
        /// This is only needed when we transform the header.
        /// </summary>
        /// <param name="data">The data to encrypt.</param>
        /// <param name="key">The encryption key.</param>
        /// <param name="nonce">The 16-byte IV to use.</param>
        /// <returns>The encrypted ciphertext with the authentication tag appended.</returns>
        public static byte[] Encrypt(byte[] data, SensitiveByteArray key, byte[] nonce)
        {
            if (data == null) throw new ArgumentNullException(nameof(data), "Data cannot be null.");
            if (key == null) throw new ArgumentNullException(nameof(key), "Key cannot be null.");
            if (nonce == null || nonce.Length < NonceSize) throw new ArgumentException("Nonce must be at least 12 bytes.", nameof(nonce));

            var ciphertext = new byte[data.Length];
            var tag = new byte[TagSize];

            using var aesGcm = new System.Security.Cryptography.AesGcm(key.GetKey(), TagSize);
            aesGcm.Encrypt(nonce, data, ciphertext, tag);

            // Combine ciphertext and tag for output
            var result = new byte[ciphertext.Length + tag.Length];
            Buffer.BlockCopy(ciphertext, 0, result, 0, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, result, ciphertext.Length, tag.Length);

            return result;
        }

        /// <summary>
        /// Encrypts data using AES-GCM with a generated IV.
        /// </summary>
        /// <param name="data">The data to encrypt.</param>
        /// <param name="key">The encryption key.</param>
        /// <returns>A tuple containing the IV and the ciphertext with the authentication tag appended.</returns>
        public static (byte[] IV, byte[] ciphertext) Encrypt(byte[] data, SensitiveByteArray key)
        {
            if (data == null) throw new ArgumentNullException(nameof(data), "Data cannot be null.");
            if (key == null) throw new ArgumentNullException(nameof(key), "Key cannot be null.");

            var iv = new byte[16]; // Always generate 16-byte IV
            RandomNumberGenerator.Fill(iv);

            var ciphertext = Encrypt(data, key, iv);
            return (iv, ciphertext);
        }

        /// <summary>
        /// Decrypts ciphertext using AES-GCM with a specified IV.
        /// </summary>
        /// <param name="cipherText">The ciphertext with the authentication tag appended.</param>
        /// <param name="key">The decryption key.</param>
        /// <param name="iv">The 16-byte IV used for encryption.</param>
        /// <returns>The decrypted plaintext.</returns>
        public static byte[] Decrypt(byte[] cipherText, SensitiveByteArray key, byte[] iv)
        {
            if (cipherText == null) throw new ArgumentNullException(nameof(cipherText), "CipherText cannot be null.");
            if (key == null) throw new ArgumentNullException(nameof(key), "Key cannot be null.");
            if (iv == null || iv.Length < NonceSize) throw new ArgumentException("IV must be at least 12 bytes.", nameof(iv));
            if (cipherText.Length < TagSize) throw new ArgumentException("CipherText is too short to contain an authentication tag.", nameof(cipherText));

            // Use the first 12 bytes of the IV
            var nonce = new byte[NonceSize];
            Buffer.BlockCopy(iv, 0, nonce, 0, NonceSize);

            var tag = new byte[TagSize];
            var actualCiphertext = new byte[cipherText.Length - TagSize];

            // Extract the tag and ciphertext
            Buffer.BlockCopy(cipherText, 0, actualCiphertext, 0, actualCiphertext.Length);
            Buffer.BlockCopy(cipherText, actualCiphertext.Length, tag, 0, TagSize);

            var plaintext = new byte[actualCiphertext.Length];
            using var aesGcm = new System.Security.Cryptography.AesGcm(key.GetKey(), TagSize);

            // Decrypt and authenticate
            aesGcm.Decrypt(nonce, actualCiphertext, tag, plaintext);
            return plaintext;
        }
    }
}
