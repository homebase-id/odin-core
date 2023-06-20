using Dawn;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Util;

namespace Odin.Core.Cryptography.Data
{
    /// <summary>
    /// Holding an encrypted symmetric key (AES key)
    /// </summary>
    public class SymmetricKeyEncryptedAes
    {
        public byte[] KeyEncrypted { get; set; } // The symmetric encryption key encrypted with AES using the IV below
        public byte[] KeyIV { get; set; }        // IV used for AES encryption of the key
        public byte[] KeyHash { get; set; }      // Hash (SHA256 XORed to 128) of the secret & iv needed to decrypt


        public SymmetricKeyEncryptedAes()
        {
            //For LiteDB
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
            // KeyHash = HashUtil.ReduceSHA256Hash(key.GetKey());
            // KeyHash = ByteArrayUtil.EquiByteArrayXor(KeyHash, KeyIV);
            // return KeyHash;
            
            var k = ByteArrayUtil.ReduceSHA256Hash(key.GetKey());
            k = ByteArrayUtil.EquiByteArrayXor(k, KeyIV);
            return k;
        }

        private void EncryptKey(ref SensitiveByteArray secret, ref SensitiveByteArray keyToProtect)
        {
            Guard.Argument(KeyHash == null).True();

            (KeyIV, KeyEncrypted) = AesCbc.Encrypt(keyToProtect.GetKey(), ref secret);
            KeyHash = CalcKeyHash(ref secret);
        }

        /// <summary>
        /// Decrypt the encrypted key and return a clone of it
        /// </summary>
        /// <param name="secret">The decryption key</param>
        /// <returns>A clone of the decrypted key</returns>
        public SensitiveByteArray DecryptKeyClone(ref SensitiveByteArray secret)
        {
            if (!ByteArrayUtil.EquiByteArrayCompare(KeyHash, CalcKeyHash(ref secret)))
            {
                throw new OdinSecurityException("Key hash did not match");
            }

            var key = new SensitiveByteArray(AesCbc.Decrypt(KeyEncrypted, ref secret, KeyIV));

            return key;
        }
    }
}
