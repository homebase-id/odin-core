using System;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;

namespace Odin.Services.Peer.Encryption
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

        /// <summary>
        /// Decrypts this Encrypted Key header
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        /// <exception cref="OdinSecurityException"></exception>
        public KeyHeader DecryptAesToKeyHeader(ref SensitiveByteArray key)
        {
            if (this.EncryptionVersion == 1)
            {
                var bytes = AesCbc.Decrypt(this.EncryptedAesKey, key, this.Iv);
                var kh = KeyHeader.FromCombinedBytes(bytes, 16, 16);
                bytes.ToSensitiveByteArray().Wipe();
                return kh;
            }

            throw new OdinSecurityException("Unsupported encryption version");
        }

        public static EncryptedKeyHeader EncryptKeyHeaderAes(KeyHeader keyHeader, byte[] iv, ref SensitiveByteArray key)
        {
            var secureKeyHeader = keyHeader.Combine();
            var data = AesCbc.Encrypt(secureKeyHeader.GetKey(), key, iv);
            secureKeyHeader.Wipe();

            return new EncryptedKeyHeader()
            {
                EncryptionVersion = 1,
                Type = EncryptionType.Aes,
                Iv = iv,
                EncryptedAesKey = data
            };
        }

        public SensitiveByteArray Combine()
        {
            //TODO: I Dont know the length of encrypted AES Key so maybe base64 encode this instead?
            return new SensitiveByteArray(ByteArrayUtil.Combine(this.Iv, this.EncryptedAesKey));
        }

        public static EncryptedKeyHeader Empty()
        {
            var empty = Guid.Empty.ToByteArray();
            var emptySba = empty.ToSensitiveByteArray();
            return new EncryptedKeyHeader()
            {
                EncryptionVersion = 1,
                Type = EncryptionType.Aes,
                Iv = empty,
                EncryptedAesKey = ByteArrayUtil.Combine(empty, empty, empty)
            };
            // return EncryptKeyHeaderAes(KeyHeader.Empty(), empty, ref emptySba);
        }

        public string ToBase64()
        {
            byte[] versionBytes = BitConverter.GetBytes(this.EncryptionVersion);
            var combinedBytes = ByteArrayUtil.Combine(this.Iv, this.EncryptedAesKey, versionBytes);
            var encryptedKeyHeader64 = Convert.ToBase64String(combinedBytes);
            combinedBytes.ToSensitiveByteArray().Wipe();
            return encryptedKeyHeader64;
        }

        public static EncryptedKeyHeader FromBase64(string data64)
        {
            var bytes = Convert.FromBase64String(data64);
            var (iv, encryptedAesKey, version) = ByteArrayUtil.Split(bytes, 16, 48, 4);
            return new EncryptedKeyHeader()
            {
                Iv = iv,
                EncryptedAesKey = encryptedAesKey,
                EncryptionVersion = BitConverter.ToInt32(version)
            };
        }
    }
}