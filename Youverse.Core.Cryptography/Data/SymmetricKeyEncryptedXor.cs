using System;
using Youverse.Core.Cryptography.Crypto;

namespace Youverse.Core.Cryptography.Data
{
    /// <summary>
    /// Holding an encrypted symmetric key (AES key)
    /// </summary>
    public class SymmetricKeyEncryptedXor
    {
        private SecureKey _decryptedKey;  // Cache value to only decrypt once

        public byte[] KeyEncrypted  { get; set; } // The symmetric encryption key encrypted with AES using the IV below
        public byte[] KeyHash       { get; set; }  // Hash (SHA256 XORed to 128) of the unencrypted SymKey


        public SymmetricKeyEncryptedXor(SecureKey secretKeyToSplit, out byte[] halfKey1)
        {
            halfKey1 = ByteArrayUtil.GetRndByteArray(secretKeyToSplit.GetKey().Length);

            KeyEncrypted = XorManagement.XorEncrypt(halfKey1, secretKeyToSplit.GetKey());
            KeyHash = YouSHA.ReduceSHA256Hash(secretKeyToSplit.GetKey());

            _decryptedKey = null; // It's null until someone decrypts the key.

            // Another option is to store in _decryptedKey immediately. For now, for testing primarily, I wipe it.
        }


        /// <summary>
        /// Get the Application Dek by means of the LoginKek master key
        /// </summary>
        /// <param name="keyData">The half of the key needed to decrypt</param>
        /// <param name="halfKey">The master key LoginKek</param>
        /// <returns>The decrypted Application DeK</returns>
        public SecureKey DecryptKey(byte[] halfKey)
        {
            if (_decryptedKey == null)
            {
                var key = XorManagement.XorEncrypt(KeyEncrypted, halfKey);

                if (!ByteArrayUtil.EquiByteArrayCompare(KeyHash, YouSHA.ReduceSHA256Hash(key)))
                    throw new Exception();

                _decryptedKey = new SecureKey(key);
            }

            return _decryptedKey;
        }
    }
}
