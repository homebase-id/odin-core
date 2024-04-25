
using System;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;

namespace Odin.Core.Cryptography.Data
{
    /// <summary>
    /// Holding an encrypted symmetric key (AES key)
    /// </summary>
    public class SymmetricKeyEncryptedAes : IGenericCloneable<SymmetricKeyEncryptedAes>
    {
        public byte[] KeyEncrypted { get; set; } // The symmetric encryption key encrypted with AES using the IV below
        public byte[] KeyIV { get; set; }        // IV used for AES encryption of the key
        public byte[] KeyHash { get; set; }      // Hash (SHA256 XORed to 128) of the secret & iv needed to decrypt


        public SymmetricKeyEncryptedAes()
        {
            //For LiteDB
        }

        public SymmetricKeyEncryptedAes(SymmetricKeyEncryptedAes other)
        {
            KeyEncrypted = new byte[other.KeyEncrypted.Length];
            Array.Copy(other.KeyEncrypted, KeyEncrypted, KeyEncrypted.Length);
            KeyIV = new byte[other.KeyIV.Length];
            Array.Copy(other.KeyIV, KeyIV, KeyIV.Length);
            KeyHash = new byte[other.KeyHash.Length];
            Array.Copy(other.KeyHash, KeyHash, KeyHash.Length);
        }

        /// <summary>
        /// Create a new secret key and encrypt it AES using the the secret as the AES key.
        /// </summary>
        /// <param name="secret">The key with which to encrypt this newly generated key</param>
        public SymmetricKeyEncryptedAes(SensitiveByteArray secret)
        {
            var newKey = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16)); // Create the ApplicationDataEncryptionKey (AdeK)
            EncryptKey(secret, newKey);
            newKey.Wipe();
        }


        /// <summary>
        /// Create an AES encrypted key of dataToEncrypt using secret as the AES key
        /// </summary>
        /// <param name="secret">The key with which to encrypt the dataToEncrypt</param>
        /// <param name="dataToEncrypt">The key to encrypt</param>
        public SymmetricKeyEncryptedAes(SensitiveByteArray secret, SensitiveByteArray dataToEncrypt)
        {
            EncryptKey(secret, dataToEncrypt);
        }

        public SymmetricKeyEncryptedAes Clone()
        {
            return new SymmetricKeyEncryptedAes(this);
        }

        private byte[] CalcKeyHash(SensitiveByteArray key)
        {
            // KeyHash = HashUtil.ReduceSHA256Hash(key.GetKey());
            // KeyHash = ByteArrayUtil.EquiByteArrayXor(KeyHash, KeyIV);
            // return KeyHash;
            
            var k = ByteArrayUtil.ReduceSHA256Hash(key.GetKey());
            k = ByteArrayUtil.EquiByteArrayXor(k, KeyIV);
            return k;
        }

        private void EncryptKey(SensitiveByteArray secret, SensitiveByteArray keyToProtect)
        {
            (KeyIV, KeyEncrypted) = AesCbc.Encrypt(keyToProtect.GetKey(), secret);
            KeyHash = CalcKeyHash(secret);
        }

        /// <summary>
        /// Decrypt the encrypted key and return a clone of it
        /// </summary>
        /// <param name="secret">The decryption key</param>
        /// <returns>A clone of the decrypted key</returns>
        public SensitiveByteArray DecryptKeyClone(SensitiveByteArray secret)
        {
            if (!ByteArrayUtil.EquiByteArrayCompare(KeyHash, CalcKeyHash(secret)))
            {
                throw new OdinSecurityException("Key hash did not match")
                {
                    IsRemoteIcrIssue = true
                };
            }

            var key = new SensitiveByteArray(AesCbc.Decrypt(KeyEncrypted, secret, KeyIV));

            return key;
        }
    }
}
