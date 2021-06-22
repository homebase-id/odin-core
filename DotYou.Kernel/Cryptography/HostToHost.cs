using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace DotYou.Kernel.Cryptography
{
    public static class HostToHost
    {
        public static (byte[] header, byte[] payload) EncryptPacket(byte[] data, string recipientPublicKey)
        {
            UInt32 crcRecipientPublicEncryptionKey = 0x11223344; // A. Calculate with CRC library
            var AesEncryptionKey = YFByteArray.GetRndByteArray(16); // B. Encryption key

            // Q. The Aes encrypted message, and C. the iv
            var (iv, encryptedPayload) = AesCbc.EncryptBytesToBytes_Aes(data, AesEncryptionKey);

            System.Diagnostics.Debug.WriteLine($"Encrypting data:");
            System.Diagnostics.Debug.WriteLine($"AES Encryption Key {string.Join(", ", AesEncryptionKey)}");
            System.Diagnostics.Debug.WriteLine($"IV {string.Join(", ", iv)}");
            System.Diagnostics.Debug.WriteLine($"encrypted payload {string.Join(", ", encryptedPayload)}");

            var packetHeader = new byte[16 + 16];
            int i = 0;

            AesEncryptionKey.CopyTo(packetHeader, i);
            i += AesEncryptionKey.Length;
            YFByteArray.WipeByteArray(AesEncryptionKey);
            iv.CopyTo(packetHeader, i);
            // i += iv.Length;

            // Now we got 32 bytes to encrypt with the recipient's public key
            RSACryptoServiceProvider rsaPublic = new RSACryptoServiceProvider();
            rsaPublic.FromXmlString(recipientPublicKey);
            byte[] encryptedHeader = rsaPublic.Encrypt(packetHeader, true);

            var finalHeader = new byte[4 + encryptedHeader.Length];
            finalHeader[0] = (byte) ((crcRecipientPublicEncryptionKey & 0xFF000000) >> 24);
            finalHeader[1] = (byte) ((crcRecipientPublicEncryptionKey & 0x00FF0000) >> 16);
            finalHeader[2] = (byte) ((crcRecipientPublicEncryptionKey & 0x0000FF00) >>  8);
            finalHeader[3] = (byte) ((crcRecipientPublicEncryptionKey & 0x000000FF));
            encryptedHeader.CopyTo(finalHeader, 4);

            return (finalHeader, encryptedPayload);
        }

        public static byte[] DecryptPacket(byte[] encryptedHeader, byte[] encryptedData, string recipientSecretKey)
        {
            if (encryptedHeader.Length < 5)
                throw new Exception();

            UInt32 crcRecipientPublicEncryptionKey = ((UInt32) (encryptedHeader[0]) << 24) | ((UInt32) (encryptedHeader[1]) << 16) | ((UInt32) (encryptedHeader[2]) << 8) | ((UInt32) encryptedHeader[3]);
            if (crcRecipientPublicEncryptionKey != 0x11223344)
                throw new Exception();

            //Decode with private key
            var rsaPrivate = new RSACryptoServiceProvider(2048);
            rsaPrivate.FromXmlString(recipientSecretKey);

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
                iv[i] = decryptedHeader[i+16];
            }

            System.Diagnostics.Debug.WriteLine($"Decrypting data:");
            System.Diagnostics.Debug.WriteLine($"AES Encryption Key {string.Join(", ", decryptionKey)}");
            System.Diagnostics.Debug.WriteLine($"IV {string.Join(", ", iv)}");
            System.Diagnostics.Debug.WriteLine($"encrypted data {string.Join(", ", encryptedData)}");

            var data = AesCbc.DecryptBytesFromBytes_Aes(encryptedData, decryptionKey, iv);

            return data;
        }
    }
}
