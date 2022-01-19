using Dawn;
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


        public byte[] KeyEncrypted  { get; set; }
        public byte[] KeyHash       { get; set; }  // Hash (SHA256 XORed to 128) of the unencrypted SymKey

        ~SymmetricKeyEncryptedXor()
        {
            //TODO: this is causing the master key to go null on other threads; need to figure out why
            //_decryptedKey?.Wipe();
        }

        public SymmetricKeyEncryptedXor()
        {
            //For LiteDB
            _decryptedKey = null;
        }

        public SymmetricKeyEncryptedXor(ref SensitiveByteArray secretKeyToSplit, out SensitiveByteArray halfKey1)
        {
            halfKey1 = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(secretKeyToSplit.GetKey().Length));

            EncryptKey(ref halfKey1, ref secretKeyToSplit);
            KeyHash = CalcKeyHash(ref halfKey1);

            _decryptedKey = null; // It's null until someone decrypts the key.

            // Another option is to store in _decryptedKey immediately. For now, for testing primarily, I wipe it.
        }

        private byte[] CalcKeyHash(ref SensitiveByteArray key)
        {
            KeyHash = YouSHA.ReduceSHA256Hash(key.GetKey());
            return KeyHash;
        }

        private void EncryptKey(ref SensitiveByteArray secret, ref SensitiveByteArray keyToProtect)
        {
            Guard.Argument(KeyHash == null).True();
            Guard.Argument(_decryptedKey == null).True();

            KeyEncrypted = XorManagement.XorEncrypt(secret.GetKey(), keyToProtect.GetKey());
            KeyHash = CalcKeyHash(ref secret);
        }


        public ref SensitiveByteArray DecryptKey(ref SensitiveByteArray halfKey)
        {
            if (!ByteArrayUtil.EquiByteArrayCompare(KeyHash, CalcKeyHash(ref halfKey)))
                throw new Exception();

            if (_decryptedKey == null || _decryptedKey.IsEmpty())
            {
                var key = XorManagement.XorEncrypt(KeyEncrypted, halfKey.GetKey());

                _decryptedKey = new SensitiveByteArray(key);
            }

            return ref _decryptedKey;
        }
    }
}
