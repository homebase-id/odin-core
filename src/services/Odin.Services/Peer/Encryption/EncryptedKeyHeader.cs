using System;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Services.Security.ZeroKnowledge;

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
        /// <remarks>
        /// A sealed key header (a deposit) can't be decrypted here, and needn't be: it comes back as a
        /// <see cref="SealedKeyHeader"/>, which every write stores and every re-lock passes on as it is
        /// (<see cref="EncryptKeyHeaderAes"/>).
        /// </remarks>
        public KeyHeader DecryptAesToKeyHeader(ref SensitiveByteArray key)
        {
            if (IsSealed())
            {
                return new SealedKeyHeader(this);
            }

            if (this.EncryptionVersion == 1)
            {
                var bytes = AesCbc.Decrypt(this.EncryptedAesKey, key, this.Iv);
                var kh = KeyHeader.FromCombinedBytes(bytes, 16, 16);
                bytes.ToSensitiveByteArray().Wipe();
                return kh;
            }

            throw new OdinSecurityException("Unsupported encryption version");
        }

        /// <remarks>A <see cref="SealedKeyHeader"/> comes back sealed, as it was given: nobody here can open it.</remarks>
        public static EncryptedKeyHeader EncryptKeyHeaderAes(KeyHeader keyHeader, byte[] iv, ref SensitiveByteArray key)
        {
            if (keyHeader is SealedKeyHeader sealedKeyHeader)
            {
                return sealedKeyHeader.Sealed;
            }

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

        /// <summary>
        /// A deposit's key header: the file's key header (IV and AES key, as <see cref="KeyHeader.Combine"/>
        /// joins them) sealed to a drive's write-only public key.  The IV is also kept as is, as for an
        /// AES key header.
        /// </summary>
        public static EncryptedKeyHeader Sealed(byte[] iv, byte[] sealedKeyHeader) => new()
        {
            EncryptionVersion = 1,
            Type = EncryptionType.Sealed,
            Iv = iv,
            EncryptedAesKey = sealedKeyHeader
        };

        public bool IsSealed() => Type == EncryptionType.Sealed;

        /// <summary>
        /// Refuses a sealed key header that isn't one: the IV must be strong and the key header a
        /// sealed value.  Nothing more can be checked: nobody here can open it.
        /// </summary>
        public void AssertValidSealed()
        {
            if (EncryptionVersion != 1 || !ByteArrayUtil.IsStrongKey(Iv) || !ZkSeal.IsSealed(EncryptedAesKey))
            {
                throw new OdinClientException("The sealed key header is malformed", OdinClientErrorCode.InvalidKeyHeader);
            }
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