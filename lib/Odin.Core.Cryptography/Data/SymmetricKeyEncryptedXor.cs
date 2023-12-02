using System;
using Dawn;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Util;

namespace Odin.Core.Cryptography.Data
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


        //HACK: the _ is a hack so I can use the same signature.
        // Alternate constructor. 
        // The localHalfKey XOR remoteHalfKey = secretKey. The remoteHalf is the
        // half key not stored here.
        public SymmetricKeyEncryptedXor(SensitiveByteArray localHalfKey, SensitiveByteArray remoteHalfKey, bool _, bool __)
        {
            var secretKey = XorManagement.XorDecrypt(localHalfKey.GetKey(), remoteHalfKey.GetKey()).ToSensitiveByteArray();

            EncryptKey(remoteHalfKey, secretKey);

            secretKey.Wipe();
        }




        public SymmetricKeyEncryptedXor(SensitiveByteArray secretKeyToSplit, out SensitiveByteArray halfKey1)
        {
            halfKey1 = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(secretKeyToSplit.GetKey().Length));

            EncryptKey(halfKey1, secretKeyToSplit);
        }

        public byte[] CalcKeyHash(SensitiveByteArray key)
        {
            var k = ByteArrayUtil.ReduceSHA256Hash(key.GetKey());
            return k;
        }

        private void EncryptKey(SensitiveByteArray remoteHalfKey, SensitiveByteArray keyToProtect)
        {
            Guard.Argument(KeyHash == null).True();

            KeyEncrypted = XorManagement.XorEncrypt(remoteHalfKey.GetKey(), keyToProtect.GetKey());
            KeyHash = CalcKeyHash(remoteHalfKey);
        }


        public SensitiveByteArray DecryptKeyClone(SensitiveByteArray remoteHalfKey)
        {
            if (!ByteArrayUtil.EquiByteArrayCompare(KeyHash, CalcKeyHash(remoteHalfKey)))
            {
                throw new OdinSecurityException("Byte arrays don't match")
                {
                    IsRemoteIcrIssue = true
                };
            }

            var key = new SensitiveByteArray(XorManagement.XorEncrypt(KeyEncrypted, remoteHalfKey.GetKey()));

            return key;
        }
    }
}
