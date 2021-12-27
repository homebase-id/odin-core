using System;
using Youverse.Core.Cryptography.Crypto;

namespace Youverse.Core.Cryptography.Data
{
    /// <summary>
    /// Holding an encrypted symmetric key (AES key)
    /// </summary>
    public class SymKeyData
    {
        private SecureKey _decryptedKey;

        public byte[] EncryptedSymKey  { get; set; } // The symmetric encryption key encrypted with AES using the IV below
        public byte[] KeyIV            { get; set; } // IV used for AES encryption above
        public byte[] Check            { get; set; }  // Hash (SHA256 XORed to 128) of the unencrypted SymKey


        public SymKeyData(SecureKey secret)
        {
            var newKey = new SecureKey(ByteArrayUtil.GetRndByteArray(16)); // Create the ApplicationDataEncryptionKey (AdeK)
            // _decryptedKey = new SecureKey(ByteArrayUtil.GetRndByteArray(16)); // Create the ApplicationDataEncryptionKey (AdeK)

            (KeyIV, EncryptedSymKey) = AesCbc.EncryptBytesToBytes_Aes(newKey.GetKey(), secret.GetKey());
            Check = YouSHA.ReduceSHA256Hash(newKey.GetKey());

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
                var key = AesCbc.DecryptBytesFromBytes_Aes(EncryptedSymKey, secret, KeyIV);

                if (!ByteArrayUtil.EquiByteArrayCompare(Check, YouSHA.ReduceSHA256Hash(key)))
                    throw new Exception();

                _decryptedKey = new SecureKey(key);
            }

            return _decryptedKey;
        }
    }
}
