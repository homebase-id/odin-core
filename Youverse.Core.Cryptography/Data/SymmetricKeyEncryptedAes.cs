using System;
using Youverse.Core.Cryptography.Crypto;

namespace Youverse.Core.Cryptography.Data
{
    /// <summary>
    /// Holding an encrypted symmetric key (AES key)
    /// </summary>
    public class SymmetricKeyEncryptedAes
    {
        private SecureKey _decryptedKey;  // Cache value to only decrypt once

        public byte[] KeyEncrypted  { get; set; } // The symmetric encryption key encrypted with AES using the IV below
        public byte[] KeyIV         { get; set; } // IV used for AES encryption of the key
        public byte[] KeyHash       { get; set; }  // Hash (SHA256 XORed to 128) of the unencrypted SymKey


        ~SymmetricKeyEncryptedAes()
        {
            _decryptedKey.Wipe();
        }

        public SymmetricKeyEncryptedAes()
        {
            //For LiteDB
        }

        public SymmetricKeyEncryptedAes(SecureKey secret)
        {
            var newKey = new SecureKey(ByteArrayUtil.GetRndByteArray(16)); // Create the ApplicationDataEncryptionKey (AdeK)
            // _decryptedKey = new SecureKey(ByteArrayUtil.GetRndByteArray(16)); // Create the ApplicationDataEncryptionKey (AdeK)

            (KeyIV, KeyEncrypted) = AesCbc.EncryptBytesToBytes_Aes(newKey.GetKey(), secret.GetKey());
            KeyHash = YouSHA.ReduceSHA256Hash(newKey.GetKey());
            _decryptedKey = null; // It's null until someone decrypts the key.

            newKey.Wipe();
            // Another option is to keep the _decryptedKey. For now, for testing primarily, I wipe it.
        }


        /// <summary>
        /// Get the Application Dek by means of the LoginKek master key
        /// </summary>
        /// <param name="keyData">The ApplicationTokenData</param>
        /// <param name="secret">The master key LoginKek</param>
        /// <returns>The decrypted Application DeK</returns>
        public SecureKey DecryptKey(byte[] secret)
        {
            if (_decryptedKey == null)
            {
                var key = AesCbc.DecryptBytesFromBytes_Aes(KeyEncrypted, secret, KeyIV);

                if (!ByteArrayUtil.EquiByteArrayCompare(KeyHash, YouSHA.ReduceSHA256Hash(key)))
                    throw new Exception();

                _decryptedKey = new SecureKey(key);
            }

            return _decryptedKey;
        }
    }
}
