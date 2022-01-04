using System;
using Youverse.Core.Cryptography.Crypto;

namespace Youverse.Core.Cryptography.Data
{
    /// <summary>
    /// Holding an encrypted symmetric key (AES key)
    /// </summary>
    public class SymmetricKeyEncryptedAes
    {
        private SensitiveByteArray _decryptedKey;  // Cache value to only decrypt once

        public byte[] KeyEncrypted { get; set; } // The symmetric encryption key encrypted with AES using the IV below
        public byte[] KeyIV { get; set; } // IV used for AES encryption of the key
        public byte[] KeyHash { get; set; }  // Hash (SHA256 XORed to 128) of the unencrypted SymKey


        ~SymmetricKeyEncryptedAes()
        {
            _decryptedKey?.Wipe();
        }

        public SymmetricKeyEncryptedAes()
        {
            //For LiteDB
            _decryptedKey = null;
        }

        public SymmetricKeyEncryptedAes(SensitiveByteArray secret)
        {
            var newKey = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16)); // Create the ApplicationDataEncryptionKey (AdeK)
            
            // _decryptedKey = new SecureKey(ByteArrayUtil.GetRndByteArray(16)); // Create the ApplicationDataEncryptionKey (AdeK)

            (KeyIV, KeyEncrypted) = AesCbc.EncryptBytesToBytes_Aes(newKey.GetKey(), secret);
            KeyHash = YouSHA.ReduceSHA256Hash(newKey.GetKey());
            _decryptedKey = null; // It's null until someone decrypts the key.

            newKey.Wipe();
            // Another option is to keep the _decryptedKey. For now, for testing primarily, I wipe it.
        }

        public SymmetricKeyEncryptedAes(SensitiveByteArray secret, ref SensitiveByteArray data)
        {
            (KeyIV, KeyEncrypted) = AesCbc.EncryptBytesToBytes_Aes(data.GetKey(), secret);
            KeyHash = YouSHA.ReduceSHA256Hash(data.GetKey());
            _decryptedKey = null; // It's null until someone decrypts the key.
        }


        /// <summary>
        /// Get the Application Dek by means of the LoginKek master key
        /// </summary>
        /// <param name="keyData">The ApplicationTokenData</param>
        /// <param name="secret">The master key LoginKek</param>
        /// <returns>The decrypted Application DeK</returns>
        [Obsolete("Use SenstiveByteArray overload instead")]
        public SensitiveByteArray DecryptKey(byte[] secret)
        {
            if (_decryptedKey == null || _decryptedKey.IsEmpty())
            {
                var key = AesCbc.DecryptBytesFromBytes_Aes(KeyEncrypted, secret, KeyIV);

                if (!ByteArrayUtil.EquiByteArrayCompare(KeyHash, YouSHA.ReduceSHA256Hash(key)))
                    throw new Exception();

                _decryptedKey = new SensitiveByteArray(key);
            }

            return _decryptedKey;
        }

        public SensitiveByteArray DecryptKey(SensitiveByteArray secret)
        {
            return this.DecryptKey(secret.GetKey());
        }
    }
}
