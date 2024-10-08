﻿using System;
using System.Text;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Time;

namespace Odin.Core.Cryptography.Obsolete
{
    public class CryptoniteKey
    {
        public byte[] key;  // The key the payload is encrypted with
        public byte[] iv;   // The iv the payload is encrypted with
    }

    public class CryptoniteData
    {
        public uint crc;   // CRC of payload + creation-time. Should CRC be of decrypted content?
        public UnixTimeUtc creationtime;
        public byte[] payload;
    }

    [Obsolete]
    public static class CryptoniteManager
    {
        //  *** TODO *** I'd like to build a function of this together which uses Streams. 
        //               Also, discuss if these are stored as two chunks of bytes on the disk,
        //               and how that relates to sending them host to host.

        /// <summary>
        /// Creates a CryptoniteData which holds the encrypted data as well as the creation timestamp
        /// and CRC32C of the data. Also returns the CryptoniteKey which holds the iv & key needed to
        /// decrypt the data. The key in this package is AES encrypted with KeyKey.
        /// </summary>
        /// <param name="data">The data to make into cryptonite</param>
        /// <param name="KeyKey">The Key (byte[16]) to encrypt the CryptoniteKey with</param>
        /// <returns>A pair of CryptoniteKey and CryptoniteData</returns>
        public static (CryptoniteKey, CryptoniteData) CreateCryptonitePair(byte[] data, SensitiveByteArray KeyKey)
        {
            var key = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16)); // TODO: using

            var ck = new CryptoniteKey
            {
                iv = ByteArrayUtil.GetRndByteArray(16)
            };

            var cd = new CryptoniteData
            {
                creationtime = UnixTimeUtc.Now(),
                payload = AesCbc.Encrypt(data, key, ck.iv)
            };

            cd.crc = CRC32C.CalculateCRC32C(0, ByteArrayUtil.Int64ToBytes(cd.creationtime.milliseconds));
            cd.crc = CRC32C.CalculateCRC32C(cd.crc, cd.payload);

            ck.key = AesCbc.Encrypt(key.GetKey(), KeyKey, ck.iv);
            key.Wipe();

            return (ck, cd);
        }

        public static (CryptoniteKey, CryptoniteData) CreateCryptonitePair(string message, SensitiveByteArray KeyKey)
        {
            return CreateCryptonitePair(Encoding.UTF8.GetBytes(message), KeyKey);
        }
    }
}
