﻿using System;
using System.Text;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Utility;

namespace Youverse.Core.Cryptography
{
    public class CryptoniteKey
    {
        public byte[] key;  // The key the payload is encrypted with
        public byte[] iv;   // The iv the payload is encrypted with
    }

    public class CryptoniteData
    {
        public UInt32 crc;   // CRC of payload + creation-time. Should CRC be of decrypted content?
        public UInt64 creationtime;
        public byte[] payload;
    }

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
        public static (CryptoniteKey, CryptoniteData) CreateCryptonitePair(byte[] data, byte[] KeyKey)
        {
            var key = YFByteArray.GetRndByteArray(16);

            var ck = new CryptoniteKey
            {
                iv = YFByteArray.GetRndByteArray(16)
            };

            var cd = new CryptoniteData
            {
                creationtime = DateTimeExtensions.UnixTime(),
                payload =  AesCbc.EncryptBytesToBytes_Aes(data, key, ck.iv)
            };

            cd.crc = CRC32C.CalculateCRC32C(0, YFByteArray.UInt64ToBytes(cd.creationtime));
            cd.crc = CRC32C.CalculateCRC32C(cd.crc, cd.payload);

            ck.key = AesCbc.EncryptBytesToBytes_Aes(key, KeyKey, ck.iv);
            YFByteArray.WipeByteArray(key);

            return (ck, cd);
        }

        public static (CryptoniteKey, CryptoniteData) CreateCryptonitePair(string message, byte[] KeyKey)
        {
            return CreateCryptonitePair(Encoding.UTF8.GetBytes(message), KeyKey);
        }
    }
}
