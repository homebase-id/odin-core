using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Utility;

namespace Youverse.Core.Cryptography
{
    public class HostToHostProtocolHeader 
    {
        public enum HostMessageType
        {
            Chat, Mail
        };

        public UInt16 version;  // Version of the header
        public HostMessageType type; // Kind of message
        public UInt64 length; // Number of bytes in the payload
        public UInt32 payloadCrc; // CRC32C of the payload
    }

    public class HostToHostCryptoHeader
    {
        // Unencrypted
        public UInt16 version;  // Version of the crypto header
        public UInt32 keyCrc; // CRC32C of the recipient public key

        // RSA encrypted below this line:

        // public UInt32 headerCrc; // CRC32C of the protocol header
        public byte[] key;  // The key the payload is encrypted with
        public byte[] iv;   // The iv the payload is encrypted with
    }

    public class HostToHostPayload
    {
        public UInt32 crc;   // CRC of payload + creation-time
        public UInt64 creationtime;
        public byte[] payload;
    }

    public static class HostToHostManager
    {

        // This code would run on the client. Imagine we just typed a chat message.
        // Now we need to send it to our own host, using the HostRSA key
        // Once received on our own host, the payload is probably stored in a binary format
        // and the cryptoheader is immediately re-encrypted to AES-CBC with the Chat-DeK 
        // and stored alongside the payload.
        // Hereafter we encrypt a header and send the disk stored payload to each recipient.
        //
        public static HostToHostCryptoHeader LocalEncryptChatMessage(string message, byte[] publicKey)
        {
            var cryptoheader = new HostToHostCryptoHeader
            {
                version = 1,
                key = ByteArrayUtil.GetRndByteArray(16),
                iv  = ByteArrayUtil.GetRndByteArray(16),
                keyCrc = CRC32C.CalculateCRC32C(0, publicKey)
            };

            var payload = new HostToHostPayload
            {
                crc = 0,
                creationtime = DateTimeExtensions.UnixTime(),
                payload = Encoding.UTF8.GetBytes(message)
            };

            var protocol = new HostToHostProtocolHeader
            {
                version = 1,
                type = HostToHostProtocolHeader.HostMessageType.Chat,
                length = (UInt64) payload.payload.Length, // # bytes
                payloadCrc = 0
            };

            // Now encrypt the payload with AES-CBC
            // Then encrypt the cryptoheader with RSA (my own host's HostRSA or EncryptionRSA public key)
            // Then transmit {protocol, cryptoheader, payload} to my own host

            return null;
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

        public static (byte[] rsaHeader, byte[] encryptedPayload) EncryptRSAPacket(byte[] payload, byte[] recipientPublicKey)
        {
            var crcRecipientPublicEncryptionKey = CRC32C.CalculateCRC32C(0, recipientPublicKey); // A. Calculate with CRC library
            var randomKey = ByteArrayUtil.GetRndByteArray(16); // B. Generate random encryption key

            // Q. Aes encrypt the data, and get the iv C.
            var (randomIv, encryptedPayload) = AesCbc.EncryptBytesToBytes_Aes(payload, randomKey);

            // System.Diagnostics.Debug.WriteLine($"Encrypting data:");
            // System.Diagnostics.Debug.WriteLine($"AES Encryption Key {string.Join(", ", AesEncryptionKey)}");
            // System.Diagnostics.Debug.WriteLine($"IV {string.Join(", ", iv)}");
            // System.Diagnostics.Debug.WriteLine($"encrypted payload {string.Join(", ", encryptedPayload)}");

            // Now we got 32 bytes to encrypt with the recipient's public key
            RSACryptoServiceProvider rsaPublic = new RSACryptoServiceProvider(2048);
            rsaPublic.ImportRSAPublicKey(recipientPublicKey, out int n);
            // This bugs me, how can we not first create a random key pair, and then overwrite it

            // New generate the byte[] for the header
            var tempHeader = CreateUnlockHeader(randomKey, randomIv);
            ByteArrayUtil.WipeByteArray(randomKey);

            byte[] encryptedHeader = rsaPublic.Encrypt(tempHeader, true);
            ByteArrayUtil.WipeByteArray(tempHeader);

            var rsaHeader = CreateRsaHeader(crcRecipientPublicEncryptionKey, encryptedHeader);

            return (rsaHeader, encryptedPayload);
        }


        public static (byte[] header, byte[] payload) EncryptRSAPacketOrg(byte[] data, string recipientPublicKey)
        {
            // XXX
            var crcRecipientPublicEncryptionKey = CRC32C.CalculateCRC32C(0, Convert.FromBase64String(recipientPublicKey)); // A. Calculate with CRC library
            var AesEncryptionKey = ByteArrayUtil.GetRndByteArray(16); // B. Generate random encryption key

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
            ByteArrayUtil.WipeByteArray(AesEncryptionKey);

            // Now we got 32 bytes to encrypt with the recipient's public key
            RSACryptoServiceProvider rsaPublic = new RSACryptoServiceProvider(2048);
            rsaPublic.FromXmlString(recipientPublicKey); 
            // This bugs me, how can we not first create a random key pair, and then overwrite it

            byte[] encryptedHeader = rsaPublic.Encrypt(tempHeader, true);
            ByteArrayUtil.WipeByteArray(tempHeader);

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
            ByteArrayUtil.WipeByteArray(unlockHeader);

            //System.Diagnostics.Debug.WriteLine($"Decrypting data:");
            //System.Diagnostics.Debug.WriteLine($"AES Encryption Key {string.Join(", ", decryptionKey)}");
            //System.Diagnostics.Debug.WriteLine($"IV {string.Join(", ", iv)}");
            //System.Diagnostics.Debug.WriteLine($"encrypted data {string.Join(", ", encryptedData)}");

            var payload = AesCbc.DecryptBytesFromBytes_Aes(encryptedPayload, randomKey, randomIv);
            ByteArrayUtil.WipeByteArray(randomKey);

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
            ByteArrayUtil.WipeByteArray(h);

            //System.Diagnostics.Debug.WriteLine($"Decrypting data:");
            //System.Diagnostics.Debug.WriteLine($"AES Encryption Key {string.Join(", ", decryptionKey)}");
            //System.Diagnostics.Debug.WriteLine($"IV {string.Join(", ", iv)}");
            //System.Diagnostics.Debug.WriteLine($"encrypted data {string.Join(", ", encryptedData)}");

            var data = AesCbc.DecryptBytesFromBytes_Aes(encryptedData, decryptionKey, iv);
            ByteArrayUtil.WipeByteArray(decryptionKey);

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
            var randomIv2 = ByteArrayUtil.GetRndByteArray(16);

            var newEncryptedUnlockHeader = AesCbc.EncryptBytesToBytes_Aes(unlockHeader, sharedSecret, randomIv2);
            ByteArrayUtil.WipeByteArray(unlockHeader);

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
            ByteArrayUtil.WipeByteArray(h);

            //System.Diagnostics.Debug.WriteLine($"Decrypting data:");
            //System.Diagnostics.Debug.WriteLine($"AES Encryption Key {string.Join(", ", decryptionKey)}");
            //System.Diagnostics.Debug.WriteLine($"IV {string.Join(", ", iv)}");
            //System.Diagnostics.Debug.WriteLine($"encrypted data {string.Join(", ", encryptedData)}");

            var aesEncryptedKey = AesCbc.EncryptBytesToBytes_Aes(decryptionKey, DeK, iv);

            ByteArrayUtil.WipeByteArray(decryptionKey);

            // To later retrieve the key used to encrypt the data to this:
            // var key = AesCbc.DecryptBytesFromBytes_Aes(newHeader, DeK, iv);

            return (iv, aesEncryptedKey);
        }
    }
}
