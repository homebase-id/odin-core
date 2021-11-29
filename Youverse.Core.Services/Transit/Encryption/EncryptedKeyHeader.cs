using System.IO;
using Youverse.Core.Cryptography.Crypto;

namespace Youverse.Core.Services.Transit.Encryption
{
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

        public static EncryptedKeyHeader EncryptKeyHeaderAes(KeyHeader keyHeader, byte[] iv, byte[] key)
        {
            var secureKeyHeader = keyHeader.Combine();
            var data = AesCbc.EncryptBytesToBytes_Aes(secureKeyHeader.GetKey(), key, iv);
            secureKeyHeader.Wipe();

            return new EncryptedKeyHeader()
            {
                EncryptionVersion = 1,
                Type = EncryptionType.Aes,
                Iv = iv,
                Data = data
            };
        }
    }
}