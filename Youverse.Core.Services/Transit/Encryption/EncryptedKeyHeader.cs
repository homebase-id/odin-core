using System.IO;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Services.Transit.Encryption
{
    public class EncryptedKeyHeader
    {
        public int EncryptionVersion { get; set; }
        
        public EncryptionType Type { get; set; }

        
        public byte[] Iv { get; set; }

        /// <summary>
        /// The encrypted bytes of the Aes key from the original key header
        /// </summary>
        public byte[] EncryptedAesKey { get; set; }

        public KeyHeader DecryptAesToKeyHeader(byte[] key)
        {
            if (this.EncryptionVersion == 1)
            {
                var bytes = AesCbc.DecryptBytesFromBytes_Aes(this.EncryptedAesKey, key, this.Iv);
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
                EncryptedAesKey = data
            };
        }

    
    }
}