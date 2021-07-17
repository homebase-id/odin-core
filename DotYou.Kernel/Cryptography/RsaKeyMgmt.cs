using DotYou.AdminClient.Extensions;
using System;
using System.Security.Cryptography;
using System.Text;

namespace DotYou.Kernel.Cryptography
{
    // So it's slightly messy to mix up the version with encrypted and unencrypted private key.
    // Not sure if I should break it into two almost identical classes.

    struct keyPair {
        public string publicKey;
        public string privateKey; // Can we allow it to be encrypted?
        public UInt32 crc32c;     // The CRC32C of the public key
        public UInt64 expiration; // Time when this key expires
        public Guid iv; // If encrypted, this will hold the IV
        public bool encrypted; // If false then privateKey is the XML, otherwise it's AES-CBC base64 encrypted
    }

    public class RsaKeyMgmt
    {
        private keyPair currentKey;
        private keyPair previousKey;

        public UInt64 getExpiration()
        {
            return currentKey.expiration;
        }

        public string getPublicKey(UInt32 publicKeyCrc)
        {
            if (currentKey.crc32c == publicKeyCrc)
                return currentKey.publicKey;
            else if (previousKey.crc32c == publicKeyCrc)
                return previousKey.publicKey;
            else
                return null;
        }

        public string getPrivateKey(UInt32 publicKeyCrc)
        {
            if (currentKey.crc32c == publicKeyCrc)
                return currentKey.privateKey;
            else if (previousKey.crc32c == publicKeyCrc)
                return previousKey.privateKey;
            else
                return null;
        }

        // We should have a convention that if there is less than e.g. an hour to 
        // key expiration then the requestor should request a new key.
        // The host should create a new key when there is less than two hours. 
        // The precise timing depends on how quickly we want keys to expire,
        // maybe the minimum is 24 hours. Generating a new key takes a significant
        // amount of CPU.
        public void generateNewKey(UInt64 hours)
        {
            if (hours < 24)
                throw new Exception("RSA key must live for at least 24 hours");

            previousKey = currentKey; // Make the current key the old key

            currentKey.encrypted = false;
            currentKey.iv = Guid.Empty;

            RSACryptoServiceProvider rsaGenKeys = new RSACryptoServiceProvider(2048);
            currentKey.privateKey = rsaGenKeys.ToXmlString(true);
            currentKey.publicKey = rsaGenKeys.ToXmlString(false);
            currentKey.crc32c = CRC32C.CalculateCRC32C(0, Encoding.ASCII.GetBytes(currentKey.publicKey));
            currentKey.expiration = DateTimeExtensions.ToDateTimeOffsetSec((Int64) hours * 60 * 60); // Find that Unix function I made
        }


        public void generateNewEncryptedKey(UInt64 hours, byte[] key)
        {
            if (hours < 24)
                throw new Exception("RSA key must live for at least 24 hours");

            previousKey = currentKey; // Make the current key the old key

            currentKey.encrypted = true;

            RSACryptoServiceProvider rsaGenKeys = new RSACryptoServiceProvider(2048);
            currentKey.privateKey = rsaGenKeys.ToXmlString(true);
            var s = rsaGenKeys.ToXmlString(false);
            var (IV, cipher) = AesCbc.EncryptStringToBytes_Aes(s, key);
            currentKey.publicKey = Convert.ToBase64String(cipher);
            currentKey.iv = new Guid(IV);

            // Would be nice to clear s

            currentKey.crc32c = CRC32C.CalculateCRC32C(0, Encoding.ASCII.GetBytes(currentKey.publicKey));
            currentKey.expiration = DateTimeExtensions.ToDateTimeOffsetSec((Int64)hours * 60 * 60); // Find that Unix function I made
        }
    }
}
