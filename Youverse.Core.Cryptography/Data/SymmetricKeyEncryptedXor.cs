using System;
using Youverse.Core.Cryptography.Crypto;

namespace Youverse.Core.Cryptography.Data
{
    /// <summary>
    /// Holding an encrypted symmetric key (AES key)
    /// </summary>
    public class SymmetricKeyEncryptedXor
    {
        private SensitiveByteArray _decryptedKey;  // Cache value to only decrypt once
        private SensitiveByteArray _copyKey;


        public byte[] KeyEncrypted  { get; set; }
        public byte[] KeyHash       { get; set; }  // Hash (SHA256 XORed to 128) of the unencrypted SymKey

        ~SymmetricKeyEncryptedXor()
        {
            if (_decryptedKey != null)
                _decryptedKey.Wipe();

            if (_copyKey != null)
                _copyKey.Wipe();
        }

        public SymmetricKeyEncryptedXor()
        {
            //For LiteDB
            _decryptedKey = null;
            _copyKey = null;
        }

        public SymmetricKeyEncryptedXor(SensitiveByteArray secretKeyToSplit, out byte[] halfKey1)
        {
            halfKey1 = ByteArrayUtil.GetRndByteArray(secretKeyToSplit.GetKey().Length);

            KeyEncrypted = XorManagement.XorEncrypt(halfKey1, secretKeyToSplit.GetKey());
            KeyHash = YouSHA.ReduceSHA256Hash(secretKeyToSplit.GetKey());

            _decryptedKey = null; // It's null until someone decrypts the key.
            _copyKey = null;

            // Another option is to store in _decryptedKey immediately. For now, for testing primarily, I wipe it.
        }


        public SensitiveByteArray DecryptKey(SensitiveByteArray halfKey)
        {
            if (_decryptedKey == null || _decryptedKey.IsEmpty())
            {
                var key = XorManagement.XorEncrypt(KeyEncrypted, halfKey.GetKey());

                if (!ByteArrayUtil.EquiByteArrayCompare(KeyHash, YouSHA.ReduceSHA256Hash(key)))
                    throw new Exception();

                _decryptedKey = new SensitiveByteArray(key);
                _copyKey = new SensitiveByteArray(YouSHA.ReduceSHA256Hash(halfKey.GetKey()));
            }
            else
            {
                if (!ByteArrayUtil.EquiByteArrayCompare(_copyKey.GetKey(), YouSHA.ReduceSHA256Hash(halfKey.GetKey())))
                    throw new Exception();
            }

            return _decryptedKey;
        }

        /// <summary>
        /// Get the Application Dek by means of the LoginKek master key
        /// </summary>
        /// <param name="keyData">The half of the key needed to decrypt</param>
        /// <param name="halfKey">The master key LoginKek</param>
        /// <returns>The decrypted Application DeK</returns>
        [Obsolete("Use overload which accepts a SensitiveByteArray")]
        public SensitiveByteArray DecryptKey(byte[] halfKey)
        {
            return this.DecryptKey(new SensitiveByteArray(halfKey));
        }
    }
}
