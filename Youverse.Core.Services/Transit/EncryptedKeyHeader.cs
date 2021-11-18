using System;
using System.IO;
using System.Security.Cryptography;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;

namespace Youverse.Core.Services.Transit
{
    public enum EncryptionType
    {
        Aes = 11,
        Rsa = 22
    }

    public class EncryptedKeyHeader
    {
        public int EncryptionVersion { get; set; }
        public EncryptionType Type { get; set; }

        public byte[] Iv { get; set; }

        /// <summary>
        /// The encrypted bytes of the data
        /// </summary>
        public byte[] Data { get; set; }

        public KeyHeader DecryptAesToKeyHeader(byte[] key)
        {
            if (this.EncryptionVersion == 1)
            {
                var bytes = AesCbc.DecryptBytesFromBytes_Aes(this.Data, key, this.Iv);
                var kh = KeyHeader.FromCombinedBytes(bytes, 16, 16);
                return kh;
            }

            throw new InvalidDataException("Unsupported encryption version");
        }

        public static EncryptedKeyHeader EncryptKeyHeaderAes(KeyHeader keyHeader, byte[] key)
        {
            var secureKeyHeader = keyHeader.Combine();
            var data = AesCbc.EncryptBytesToBytes_Aes(secureKeyHeader.GetKey(), key, keyHeader.Iv);
            secureKeyHeader.Wipe();

            return new EncryptedKeyHeader()
            {
                EncryptionVersion = 1,
                Type = EncryptionType.Aes,
                Iv = keyHeader.Iv,
                Data = data
            };
        }
    }

    public class KeyHeader
    {
        public byte[] Iv { get; set; }

        public SecureKey AesKey { get; set; }

        public SecureKey Combine()
        {
            return new SecureKey(ByteArrayUtil.Combine(this.Iv, this.AesKey.GetKey()));
        }

        public static KeyHeader FromCombinedBytes(byte[] data, int ivLength, int keyLength)
        {
            var (p1, p2) = ByteArrayUtil.Split(data, ivLength, keyLength);
            return new KeyHeader()
            {
                Iv = p1,
                AesKey = new SecureKey(p2)
            };
        }
    }
}