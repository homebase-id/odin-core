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
        public byte[] KeyEncrypted  { get; set; }
        public byte[] KeyHash       { get; set; }  // Hash (SHA256 XORed to 128) of the unencrypted SymKey

        public SymmetricKeyEncryptedXor()
        {
            //For LiteDB
        }

        //HACK: the _ is a hack so I can use the same signature
        public SymmetricKeyEncryptedXor(ref SensitiveByteArray secretKeyToSplit, SensitiveByteArray halfKey1, bool _)
        {
            EncryptKey(ref halfKey1, ref secretKeyToSplit);
            KeyHash = CalcKeyHash(ref halfKey1);
        }


        public SymmetricKeyEncryptedXor(ref SensitiveByteArray secretKeyToSplit, out SensitiveByteArray halfKey1)
        {
            halfKey1 = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(secretKeyToSplit.GetKey().Length));

            EncryptKey(ref halfKey1, ref secretKeyToSplit);
            KeyHash = CalcKeyHash(ref halfKey1);
        }

        public byte[] CalcKeyHash(ref SensitiveByteArray key)
        {
            KeyHash = YouSHA.ReduceSHA256Hash(key.GetKey());
            return KeyHash;
        }

        private void EncryptKey(ref SensitiveByteArray secret, ref SensitiveByteArray keyToProtect)
        {
            Guard.Argument(KeyHash == null).True();

            KeyEncrypted = XorManagement.XorEncrypt(secret.GetKey(), keyToProtect.GetKey());
            KeyHash = CalcKeyHash(ref secret);
        }


        public SensitiveByteArray DecryptKeyClone(ref SensitiveByteArray halfKey)
        {
            if (!ByteArrayUtil.EquiByteArrayCompare(KeyHash, CalcKeyHash(ref halfKey)))
                throw new Exception();

            var key = new SensitiveByteArray(XorManagement.XorEncrypt(KeyEncrypted, halfKey.GetKey()));

            return key;
        }
    }
}
