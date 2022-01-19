using Dawn;
using System;
using System.Diagnostics;
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
        public byte[] KeyIV { get; set; }        // IV used for AES encryption of the key
        public byte[] KeyHash { get; set; }      // Hash (SHA256 XORed to 128) of the secret & iv needed to decrypt


        ~SymmetricKeyEncryptedAes()
        {
            //TODO: this is causing the master key to go null on other threads; need to figure out why
            //_decryptedKey?.Wipe();
        }

        public SymmetricKeyEncryptedAes()
        {
            //For LiteDB
            _decryptedKey = null;
        }

        /// <summary>
        /// Create a new secret key and encrypt it AES using the the secret as the AES key.
        /// </summary>
        /// <param name="secret">The key with which to encrypt this newly generated key</param>
        public SymmetricKeyEncryptedAes(ref SensitiveByteArray secret)
        {
            var newKey = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16)); // Create the ApplicationDataEncryptionKey (AdeK)
            EncryptKey(ref secret, ref newKey);
            newKey.Wipe();
        }


        /// <summary>
        /// Create an AES encrypted key of dataToEncrypt using secret as the AES key
        /// </summary>
        /// <param name="secret">The key with which to encrypt the dataToEncrypt</param>
        /// <param name="dataToEncrypt">The key to encrypt</param>
        public SymmetricKeyEncryptedAes(ref SensitiveByteArray secret, ref SensitiveByteArray dataToEncrypt)
        {
            EncryptKey(ref secret, ref dataToEncrypt);
        }

        private byte[] CalcKeyHash(ref SensitiveByteArray key)
        {
            KeyHash = YouSHA.ReduceSHA256Hash(key.GetKey());
            KeyHash = ByteArrayUtil.EquiByteArrayXor(KeyHash, KeyIV);
            return KeyHash;
        }

        private void EncryptKey(ref SensitiveByteArray secret, ref SensitiveByteArray keyToProtect)
        {
            Guard.Argument(KeyHash == null).True();
            Guard.Argument(_decryptedKey == null).True();

            (KeyIV, KeyEncrypted) = AesCbc.Encrypt(keyToProtect.GetKey(), ref secret);
            KeyHash = CalcKeyHash(ref secret);
        }

        /// <summary>
        /// Get the Application Dek by means of the LoginKek master key
        /// </summary>
        /// <param name="secret">The master key LoginKek</param>
        /// <returns>The decrypted Application DeK</returns>
        public ref SensitiveByteArray DecryptKey(ref SensitiveByteArray secret)
        {
            if (!ByteArrayUtil.EquiByteArrayCompare(KeyHash, CalcKeyHash(ref secret)))
                throw new Exception();

            if (_decryptedKey == null || _decryptedKey.IsEmpty())
            {
                var key = AesCbc.Decrypt(KeyEncrypted, ref secret, KeyIV);
                _decryptedKey = new SensitiveByteArray(key);
            }
            
            return ref _decryptedKey;
        }
    }
}
