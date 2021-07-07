﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace DotYou.Kernel.Cryptography
{
    public static class HostToHost
    {
        private static UInt32 CRC32(string s)
        {
            return 0x11223344;
        }

        // ===

        public static byte[] CreateUnlockHeader(byte[] encryptionKey, byte[] iv)
        {
            using (MemoryStream mem = new MemoryStream(32))
            {
                using (var writer = new BinaryWriter(mem))
                {
                    writer.Write(encryptionKey);
                    writer.Write(iv);
                }
                mem.Flush();

                return mem.GetBuffer();
            }
        }

        public static (byte[] encryptionKey, byte[] iv) ParseUnlockHeader(byte[] header)
        {
            using (MemoryStream mem = new MemoryStream(header))
            {
                using (var reader = new BinaryReader(mem))
                {
                    var encryptionKey = reader.ReadBytes(16);
                    var iv = reader.ReadBytes(16);
                    return (encryptionKey, iv);
                }
            }
        }

        // ==== RSA

        public static byte[] CreateRsaHeader(UInt32 crc, byte[] encryptedUnlockHeader)
        {
            using (MemoryStream mem = new MemoryStream(4+encryptedUnlockHeader.Length))
            {
                using (var writer = new BinaryWriter(mem))
                {
                    writer.Write(crc);
                    writer.Write(encryptedUnlockHeader);
                }
                mem.Flush();

                return mem.GetBuffer();
            }
        }

        public static (UInt32 crc, byte[] unlockHeader) ParseRsaHeader(byte[] header)
        {
            using (MemoryStream mem = new MemoryStream(header))
            {
                using (var reader = new BinaryReader(mem))
                {
                    var crc = reader.ReadUInt32();
                    var unlockHeader = reader.ReadBytes(1024); // Need to improve this, read stream to end
                    return (crc, unlockHeader);   // could we do mem.ToArray()?
                }
            }
        }


        // ==== AES

        public static byte[] CreateAesHeader(byte[] randomIv2, byte[] encryptedUnlockHeader)
        {
            using (MemoryStream mem = new MemoryStream(randomIv2.Length + encryptedUnlockHeader.Length))
            {
                using (var writer = new BinaryWriter(mem))
                {
                    writer.Write(randomIv2);
                    writer.Write(encryptedUnlockHeader);
                }
                mem.Flush();

                return mem.GetBuffer();
            }
        }

        public static (byte[] randomIv2, byte[] encryptedUnlockHeader) ParseAesHeader(byte[] header)
        {
            using (MemoryStream mem = new MemoryStream(header))
            {
                using (var reader = new BinaryReader(mem))
                {
                    var randomIv2 = reader.ReadBytes(16);
                    var encryptedUnlockHeader = reader.ReadBytes(1024); // Need to improve this, read stream to end
                    return (randomIv2, encryptedUnlockHeader);   // could we do mem.ToArray()?
                }
            }
        }

        // ==== 


        public static (byte[] rsaHeader, byte[] encryptedPayload) EncryptRSAPacket(byte[] payload, string recipientPublicKey)
        {
            var crcRecipientPublicEncryptionKey = CRC32(recipientPublicKey); // A. Calculate with CRC library
            var randomKey = YFByteArray.GetRndByteArray(16); // B. Generate random encryption key

            // Q. Aes encrypt the data, and get the iv C.
            var (randomIv, encryptedPayload) = AesCbc.EncryptBytesToBytes_Aes(payload, randomKey);

            // System.Diagnostics.Debug.WriteLine($"Encrypting data:");
            // System.Diagnostics.Debug.WriteLine($"AES Encryption Key {string.Join(", ", AesEncryptionKey)}");
            // System.Diagnostics.Debug.WriteLine($"IV {string.Join(", ", iv)}");
            // System.Diagnostics.Debug.WriteLine($"encrypted payload {string.Join(", ", encryptedPayload)}");

            // Now we got 32 bytes to encrypt with the recipient's public key
            RSACryptoServiceProvider rsaPublic = new RSACryptoServiceProvider(2048);
            rsaPublic.FromXmlString(recipientPublicKey);
            // This bugs me, how can we not first create a random key pair, and then overwrite it

            // New generate the byte[] for the header
            var tempHeader = CreateUnlockHeader(randomKey, randomIv);
            YFByteArray.WipeByteArray(randomKey);

            byte[] encryptedHeader = rsaPublic.Encrypt(tempHeader, true);
            YFByteArray.WipeByteArray(tempHeader);

            var rsaHeader = CreateRsaHeader(crcRecipientPublicEncryptionKey, encryptedHeader);

            return (rsaHeader, encryptedPayload);
        }


        public static (byte[] header, byte[] payload) EncryptRSAPacketOrg(byte[] data, string recipientPublicKey)
        {
            var crcRecipientPublicEncryptionKey = CRC32(recipientPublicKey); // A. Calculate with CRC library
            var AesEncryptionKey = YFByteArray.GetRndByteArray(16); // B. Generate random encryption key

            // Q. Aes encrypt the data, and get the iv C.
            var (iv, encryptedPayload) = AesCbc.EncryptBytesToBytes_Aes(data, AesEncryptionKey);

            // System.Diagnostics.Debug.WriteLine($"Encrypting data:");
            // System.Diagnostics.Debug.WriteLine($"AES Encryption Key {string.Join(", ", AesEncryptionKey)}");
            // System.Diagnostics.Debug.WriteLine($"IV {string.Join(", ", iv)}");
            // System.Diagnostics.Debug.WriteLine($"encrypted payload {string.Join(", ", encryptedPayload)}");

            // New generate the byte[] for the header
            var tempHeader = new byte[AesEncryptionKey.Length + iv.Length];
 
            AesEncryptionKey.CopyTo(tempHeader, 0);
            iv.CopyTo(tempHeader, AesEncryptionKey.Length);
            YFByteArray.WipeByteArray(AesEncryptionKey);

            // Now we got 32 bytes to encrypt with the recipient's public key
            RSACryptoServiceProvider rsaPublic = new RSACryptoServiceProvider(2048);
            rsaPublic.FromXmlString(recipientPublicKey); 
            // This bugs me, how can we not first create a random key pair, and then overwrite it

            byte[] encryptedHeader = rsaPublic.Encrypt(tempHeader, true);
            YFByteArray.WipeByteArray(tempHeader);

            var finalHeader = new byte[4 + encryptedHeader.Length];
            finalHeader[0] = (byte) ((crcRecipientPublicEncryptionKey & 0xFF000000) >> 24);
            finalHeader[1] = (byte) ((crcRecipientPublicEncryptionKey & 0x00FF0000) >> 16);
            finalHeader[2] = (byte) ((crcRecipientPublicEncryptionKey & 0x0000FF00) >>  8);
            finalHeader[3] = (byte) ((crcRecipientPublicEncryptionKey & 0x000000FF));
            encryptedHeader.CopyTo(finalHeader, 4);

            return (finalHeader, encryptedPayload);
        }


        public static byte[] DecryptRSAPacket(byte[] rsaHeader, byte[] encryptedPayload, string recipientSecretKey)
        {
            if (rsaHeader.Length < 5)
                throw new Exception();

            var (crcRecipientPublicEncryptionKey, encryptedUnlockHeader) = ParseRsaHeader(rsaHeader);

            // Get the unencrypted CRC
            if (crcRecipientPublicEncryptionKey != 0x11223344) // Replace with CRC32(publicKey)
                throw new Exception();

            //Decode with private key
            var rsaPrivate = new RSACryptoServiceProvider(2048);
            rsaPrivate.FromXmlString(recipientSecretKey); // BUgs me, figure out how to not create random key

            var unlockHeader = rsaPrivate.Decrypt(encryptedUnlockHeader, true);

            var (randomKey, randomIv) = ParseUnlockHeader(unlockHeader);
            YFByteArray.WipeByteArray(unlockHeader);

            //System.Diagnostics.Debug.WriteLine($"Decrypting data:");
            //System.Diagnostics.Debug.WriteLine($"AES Encryption Key {string.Join(", ", decryptionKey)}");
            //System.Diagnostics.Debug.WriteLine($"IV {string.Join(", ", iv)}");
            //System.Diagnostics.Debug.WriteLine($"encrypted data {string.Join(", ", encryptedData)}");

            var payload = AesCbc.DecryptBytesFromBytes_Aes(encryptedPayload, randomKey, randomIv);
            YFByteArray.WipeByteArray(randomKey);

            return payload;
        }


        public static byte[] DecryptRSAPacketOrg(byte[] encryptedHeader, byte[] encryptedData, string recipientSecretKey)
        {
            if (encryptedHeader.Length < 5)
                throw new Exception();

            // Get the unencrypted CRC
            UInt32 crcRecipientPublicEncryptionKey = ((UInt32)(encryptedHeader[0]) << 24) | ((UInt32)(encryptedHeader[1]) << 16) | ((UInt32)(encryptedHeader[2]) << 8) | ((UInt32)encryptedHeader[3]);
            if (crcRecipientPublicEncryptionKey != 0x11223344) // Replace with CRC32(publicKey)
                throw new Exception();

            //Decode with private key
            var rsaPrivate = new RSACryptoServiceProvider(2048);
            rsaPrivate.FromXmlString(recipientSecretKey); // BUgs me, figure out how to not create random key

            var h = new byte[encryptedHeader.Length - 4];
            int i;

            for (i = 0; i < h.Length; i++)
                h[i] = encryptedHeader[i + 4];

            var decryptedHeader = rsaPrivate.Decrypt(h, true);

            var decryptionKey = new byte[16];
            var iv = new byte[16];

            for (i = 0; i < 16; i++)
            {
                decryptionKey[i] = decryptedHeader[i];
                iv[i] = decryptedHeader[i + 16];
            }
            YFByteArray.WipeByteArray(h);

            //System.Diagnostics.Debug.WriteLine($"Decrypting data:");
            //System.Diagnostics.Debug.WriteLine($"AES Encryption Key {string.Join(", ", decryptionKey)}");
            //System.Diagnostics.Debug.WriteLine($"IV {string.Join(", ", iv)}");
            //System.Diagnostics.Debug.WriteLine($"encrypted data {string.Join(", ", encryptedData)}");

            var data = AesCbc.DecryptBytesFromBytes_Aes(encryptedData, decryptionKey, iv);
            YFByteArray.WipeByteArray(decryptionKey);

            return data;
        }



        /// <summary>
        /// Transform the RSA encrypted header into an {iv, AES(secret key)} using the DeK as the key.
        /// TODO: Get security validation that it's OK I reuse the IV when I encrypt the secret encryption key. 
        /// </summary>
        /// <param name="rsaHeader"></param>
        /// <param name="recipientSecretKey"></param>
        /// <param name="sharedSecret"></param>
        /// <returns>The aes header</returns>
        public static byte[] TransformRSAtoAES(byte[] rsaHeader, string recipientSecretKey, byte[] sharedSecret)
        {
            if (rsaHeader.Length < 5)
                throw new Exception();

            var (crcRecipientPublicEncryptionKey, encryptedUnlockHeader) = ParseRsaHeader(rsaHeader);

            if (crcRecipientPublicEncryptionKey != 0x11223344) // Replace with CRC32(publicKey)
                throw new Exception();

            //Decode with private key
            var rsaPrivate = new RSACryptoServiceProvider(2048);
            rsaPrivate.FromXmlString(recipientSecretKey); // BUgs me, figure out how to not create random key

            var unlockHeader = rsaPrivate.Decrypt(encryptedUnlockHeader, true);

            // var (randomKey, randomIv) = ParseUnlockHeader(unlockHeader);
            // YFByteArray.WipeByteArray(unlockHeader);

            // Now we have the keys to decrypt the encryptedPayload, let's create a new AES header
            //
            var randomIv2 = YFByteArray.GetRndByteArray(16);

            var newEncryptedUnlockHeader = AesCbc.EncryptBytesToBytes_Aes(unlockHeader, sharedSecret, randomIv2);
            YFByteArray.WipeByteArray(unlockHeader);

            // Assemble the final AES header. 

            var aesHeader = CreateAesHeader(randomIv2, newEncryptedUnlockHeader);

            // To later retrieve the key used to encrypt the data to this:
            // var key = AesCbc.DecryptBytesFromBytes_Aes(newHeader, DeK, iv);

            return aesHeader;
        }

        public static (byte[] iv, byte[] aesEncryptedKey) TransformRSAtoAESOrg(byte[] encryptedHeader, string recipientSecretKey, byte[] DeK)
        {
            if (encryptedHeader.Length < 5)
                throw new Exception();

            // Get the unencrypted CRC
            UInt32 crcRecipientPublicEncryptionKey = ((UInt32)(encryptedHeader[0]) << 24) | ((UInt32)(encryptedHeader[1]) << 16) | ((UInt32)(encryptedHeader[2]) << 8) | ((UInt32)encryptedHeader[3]);
            if (crcRecipientPublicEncryptionKey != 0x11223344) // Replace with CRC32(publicKey)
                throw new Exception();

            //Decode with private key
            var rsaPrivate = new RSACryptoServiceProvider(2048);
            rsaPrivate.FromXmlString(recipientSecretKey); // BUgs me, figure out how to not create random key

            var h = new byte[encryptedHeader.Length - 4];
            int i;

            for (i = 0; i < h.Length; i++)
                h[i] = encryptedHeader[i + 4];

            var decryptedHeader = rsaPrivate.Decrypt(h, true);

            var decryptionKey = new byte[16];
            var iv = new byte[16];

            for (i = 0; i < 16; i++)
            {
                decryptionKey[i] = decryptedHeader[i];
                iv[i] = decryptedHeader[i + 16];
            }
            YFByteArray.WipeByteArray(h);

            //System.Diagnostics.Debug.WriteLine($"Decrypting data:");
            //System.Diagnostics.Debug.WriteLine($"AES Encryption Key {string.Join(", ", decryptionKey)}");
            //System.Diagnostics.Debug.WriteLine($"IV {string.Join(", ", iv)}");
            //System.Diagnostics.Debug.WriteLine($"encrypted data {string.Join(", ", encryptedData)}");

            var aesEncryptedKey = AesCbc.EncryptBytesToBytes_Aes(decryptionKey, DeK, iv);

            YFByteArray.WipeByteArray(decryptionKey);

            // To later retrieve the key used to encrypt the data to this:
            // var key = AesCbc.DecryptBytesFromBytes_Aes(newHeader, DeK, iv);

            return (iv, aesEncryptedKey);
        }
    }
}
