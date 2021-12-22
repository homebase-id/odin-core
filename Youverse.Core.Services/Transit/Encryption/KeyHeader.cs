using System.IO;
using Youverse.Core.Cryptography;

namespace Youverse.Core.Services.Transit.Encryption
{
    public class KeyHeader
    {
        public byte[] Iv { get; set; }

        public SecureKey AesKey { get; set; }

        public SecureKey Combine()
        {
            return new SecureKey(ByteArrayUtil.Combine(this.Iv, this.AesKey.GetKey()));
        }
        
        public Stream GetEncryptedStreamAes(string data)
        {
            var cipher = Core.Cryptography.Crypto.AesCbc.EncryptBytesToBytes_Aes(
                data: System.Text.Encoding.UTF8.GetBytes(data),
                key: this.AesKey.GetKey(),
                iv: this.Iv);

            return new MemoryStream(cipher);
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

        /// <summary>
        /// Creates a new KeyHeader with random 16-byte arrays
        /// </summary>
        /// <returns></returns>
        public static KeyHeader NewRandom16()
        {
            return new KeyHeader()
            {
                Iv = ByteArrayUtil.GetRndByteArray(16),
                AesKey = new SecureKey(ByteArrayUtil.GetRndByteArray(16))
            };
        }
        
    }
}