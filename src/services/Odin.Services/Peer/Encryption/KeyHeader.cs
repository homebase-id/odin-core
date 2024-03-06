using System;
using System.IO;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;

namespace Odin.Services.Peer.Encryption
{
    public class KeyHeader
    {
        public byte[] Iv { get; set; }

        public SensitiveByteArray AesKey { get; set; }

        public SensitiveByteArray Combine()
        {
            return new SensitiveByteArray(ByteArrayUtil.Combine(this.Iv, this.AesKey.GetKey()));
        }

        public Stream EncryptDataAesAsStream(string data)
        {
            return this.EncryptDataAesAsStream(data.ToUtf8ByteArray());
        }
        
        public Stream EncryptDataAesAsStream(byte[] data)
        {
            var cipher = this.EncryptDataAes(data);
            return new MemoryStream(cipher);
        }
        
        public byte[] EncryptDataAes(byte[] data)
        {
            var key = this.AesKey;
            var cipher = AesCbc.Encrypt(
                data: data,
                key: key,
                iv: this.Iv);

            return cipher;
        }


        public static KeyHeader FromCombinedBytes(byte[] data, int ivLength = 16, int keyLength = 16)
        {
            var (p1, p2) = ByteArrayUtil.Split(data, ivLength, keyLength);
            return new KeyHeader()
            {
                Iv = p1,
                AesKey = new SensitiveByteArray(p2)
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
                AesKey = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16))
            };
        }

        public static KeyHeader Empty()
        {
            return new KeyHeader()
            {
                Iv = Guid.Empty.ToByteArray(),
                AesKey = Guid.Empty.ToByteArray().ToSensitiveByteArray()
            };
        }

        public byte[] Decrypt(byte[] encryptedData)
        {
            var aesKey = this.AesKey;
            var bytes = AesCbc.Decrypt(
                cipherText: encryptedData,
                Key: aesKey,
                IV: this.Iv);
            return bytes;
        }
    }
}