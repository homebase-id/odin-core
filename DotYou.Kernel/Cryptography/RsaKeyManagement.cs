using DotYou.AdminClient.Extensions;
using System;
using System.Security.Cryptography;
using System.Text;

namespace DotYou.Kernel.Cryptography
{
    public class RsaKey
    {
        public string publicKey; // Can I get them as byte arrays instead?
        public string privateKey; // Can we allow it to be encrypted?
        public UInt32 crc32c;     // The CRC32C of the public key
        public UInt64 expiration; // Time when this key expires
        public Guid iv; // If encrypted, this will hold the IV
        public bool encrypted; // If false then privateKey is the XML, otherwise it's AES-CBC base64 encrypted
    }

    // This class should be stored on the identity
    public class RsaKeyPair
    {
        public RsaKey currentKey;
        public RsaKey previousKey;
    }


    // So it's slightly messy to mix up the version with encrypted and unencrypted private key.
    // Not sure if I should break it into two almost identical classes.
    public class RsaKeyManagement
    {
        public bool CanGenerateNewKey(RsaKeyPair pair)
        {
            // Do a check here. If there are any queued packages with 
            // pair.previous then return false
            // Add function to extend the lifetime of the current key
            // if the previous is blocking
            //
            return true;
        }

        public UInt64 getExpiration(RsaKeyPair pair)
        {
            return pair.currentKey.expiration;
        }

        public string getPublicKey(RsaKeyPair pair, UInt32 publicKeyCrc)
        {
            if (pair.currentKey.crc32c == publicKeyCrc)
                return pair.currentKey.publicKey;
            else if (pair.previousKey.crc32c == publicKeyCrc)
                return pair.previousKey.publicKey;
            else
                return null;
        }

        public string getPrivateKey(RsaKeyPair pair, UInt32 publicKeyCrc)
        {
            if (pair.currentKey.crc32c == publicKeyCrc)
                return pair.currentKey.privateKey;
            else if (pair.previousKey.crc32c == publicKeyCrc)
                return pair.previousKey.privateKey;
            else
                return null;
        }

        // We should have a convention that if there is less than e.g. an hour to 
        // key expiration then the requestor should request a new key.
        // The host should create a new key when there is less than two hours. 
        // The precise timing depends on how quickly we want keys to expire,
        // maybe the minimum is 24 hours. Generating a new key takes a significant
        // amount of CPU.
        public void generateNewKey(RsaKeyPair pair, UInt64 hours)
        {
            if (hours < 24)
                throw new Exception("RSA key must live for at least 24 hours");

            if (CanGenerateNewKey(pair) == false)
                throw new Exception("Cannot generate new RSA key because the previous is in use");

            pair.previousKey = pair.currentKey; // Make the current key the old key

            pair.currentKey.encrypted = false;
            pair.currentKey.iv = Guid.Empty;

            RSACryptoServiceProvider rsaGenKeys = new RSACryptoServiceProvider(2048);
            pair.currentKey.privateKey = rsaGenKeys.ToXmlString(true);
            pair.currentKey.publicKey = rsaGenKeys.ToXmlString(false);
            pair.currentKey.crc32c = CRC32C.CalculateCRC32C(0, Encoding.ASCII.GetBytes(pair.currentKey.publicKey));
            pair.currentKey.expiration = DateTimeExtensions.ToDateTimeOffsetSec((Int64) hours * 60 * 60); // Find that Unix function I made
        }


        public void generateNewEncryptedKey(RsaKeyPair pair, UInt64 hours, byte[] key)
        {
            if (hours < 24)
                throw new Exception("RSA key must live for at least 24 hours");

            pair.previousKey = pair.currentKey; // Make the current key the old key

            pair.currentKey.encrypted = true;

            RSACryptoServiceProvider rsaGenKeys = new RSACryptoServiceProvider(2048);
            pair.currentKey.privateKey = rsaGenKeys.ToXmlString(true);
            var s = rsaGenKeys.ToXmlString(false);
            var (IV, cipher) = AesCbc.EncryptStringToBytes_Aes(s, key);
            pair.currentKey.publicKey = Convert.ToBase64String(cipher);
            pair.currentKey.iv = new Guid(IV);

            // Would be nice to clear s

            pair.currentKey.crc32c = CRC32C.CalculateCRC32C(0, Encoding.ASCII.GetBytes(pair.currentKey.publicKey));
            pair.currentKey.expiration = DateTimeExtensions.ToDateTimeOffsetSec((Int64)hours * 60 * 60); // Find that Unix function I made
        }
    }
}
