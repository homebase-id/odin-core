using DotYou.AdminClient.Extensions;
using System;
using System.Security.Cryptography;
using System.Text;

namespace DotYou.Kernel.Cryptography
{
    struct keyPair {
        public string publicKey;
        public string privateKey; // Can we allow it to be encrypted?
        public UInt32 crc32c;     // The CRC32C of the public key
        public UInt64 expiration; // Time when this key expires
    }

    public class RsaKeyMgmt
    {
        private keyPair currentKey;
        private keyPair previousKey;

        public UInt64 getExpiration()
        {
            return currentKey.expiration;
        }

        public string getPublicKey(UInt32 crc)
        {
            if (currentKey.crc32c == crc)
                return currentKey.publicKey;
            else if (previousKey.crc32c == crc)
                return previousKey.publicKey;
            else
                return null;
        }

        public string getPrivateKey(UInt32 crc)
        {
            if (currentKey.crc32c == crc)
                return currentKey.privateKey;
            else if (previousKey.crc32c == crc)
                return previousKey.privateKey;
            else
                return null;
        }

        // We should have a convention that if there is less than e.g. an hour to 
        // key expiration then the requestor should request a new key.
        // The host should create a new key when there is less than two hours. 
        // The precise timing depends on how quickly we want keys to expire,
        // maybe the minimum is 24 hours.
        public void generateNewKey(UInt64 hours)
        {
            if (hours < 24)
                throw new Exception("RSA key must live for at least 24 hours");

            previousKey = currentKey; // Make the current key the old key

            RSACryptoServiceProvider rsaGenKeys = new RSACryptoServiceProvider(2048);
            currentKey.privateKey = rsaGenKeys.ToXmlString(true);
            currentKey.publicKey = rsaGenKeys.ToXmlString(false);
            currentKey.crc32c = CRC32C.CalculateCRC32C(0, Encoding.ASCII.GetBytes(currentKey.publicKey));
            currentKey.expiration = DateTimeExtensions.ToDateTimeOffsetSec((Int64) hours * 60 * 60); // Find that Unix function I made
        }


        // Make a function to extract the key data from the ToXmlString, that we can CRC32C on.
        // Maybe just the (public) key. Make sure the JS version does it the same way.
        //

    }
}
