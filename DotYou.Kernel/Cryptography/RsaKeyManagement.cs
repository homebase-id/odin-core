using DotYou.AdminClient.Extensions;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace DotYou.Kernel.Cryptography
{
    public class RsaKey
    {
        public byte[] publicKey;
        public byte[] privateKey;   // Can we allow it to be encrypted?
        public UInt32 crc32c;       // The CRC32C of the public key
        public UInt64 expiration;   // Time when this key expires
        public UInt64 instantiated; // Time when this key was made available
        public Guid iv;             // If encrypted, this will hold the IV
        public bool encrypted;      // If false then privateKey is the XML, otherwise it's AES-CBC base64 encrypted
    }

    // This class should be stored on the identity
    public class RsaKeyList
    {
        public LinkedList<RsaKey> listRSA;  // List.first is the current key, the rest are historic
        public int maxKeys; // At least 1. 

        public RsaKeyList(int max) { listRSA = new LinkedList<RsaKey>(); maxKeys = max; }
    }


    // So it's slightly messy to mix up the version with encrypted and unencrypted private key.
    // Not sure if I should break it into two almost identical classes.
    public static class RsaKeyManagement
    {
        // ==== RsaKey Helpers here
        public static RSACryptoServiceProvider KeyPublic(RsaKey key)
        {
            int nBytesRead;

            RSACryptoServiceProvider rsaPublic = new RSACryptoServiceProvider();
            rsaPublic.ImportRSAPublicKey(key.publicKey, out nBytesRead);

            return rsaPublic;
        }

        public static RSACryptoServiceProvider KeyPrivate(RsaKey key)
        {
            int nBytesRead;

            RSACryptoServiceProvider rsaFull = new RSACryptoServiceProvider();
            rsaFull.ImportRSAPrivateKey(key.privateKey, out nBytesRead);

            return rsaFull;
        }

        public static UInt32 KeyCRC(RsaKey key)
        {
            return CRC32C.CalculateCRC32C(0, KeyPublic(key).ExportRSAPublicKey());
        }

        public static UInt32 KeyCRC(RSACryptoServiceProvider rsa)
        {
            return CRC32C.CalculateCRC32C(0, rsa.ExportRSAPublicKey());
        }

        public static string publicPem(RsaKey key)
        {
            // Either -- BEGIN RSA PUBLIC KEY -- and ExportRSAPublicKey
            // Or use -- BEGIN PUBLIC KEY -- and ExportSubjectPublicKeyInfo
            return "-----BEGIN PUBLIC KEY-----\n" + Convert.ToBase64String(KeyPublic(key).ExportSubjectPublicKeyInfo()) + "\n-----END PUBLIC KEY-----";
        }

        // privatePEM needs work in case it's encrypted
        public static string privatePem(RsaKey key)
        {
            // Either -----BEGIN RSA PRIVATE KEY----- and ExportRSAPrivateKey()
            // Or use -- BEGIN PRIVATE KEY -- and ExportPkcs8PrivateKey
            return "-----BEGIN PRIVATE KEY-----\n" + Convert.ToBase64String(KeyPrivate(key).ExportPkcs8PrivateKey()) + "\n-----END PRIVATE KEY-----";
        }


        // Work to do here. OAEP or for signing? Encrypted private?
        public static RsaKey NewKey(int hours)
        {
            var rsa = new RsaKey();

            rsa.encrypted = false;
            rsa.iv = Guid.Empty;

            RSACryptoServiceProvider rsaGenKeys = new RSACryptoServiceProvider(2048);
            rsa.privateKey = rsaGenKeys.ExportRSAPrivateKey();
            rsa.publicKey = rsaGenKeys.ExportRSAPublicKey();
            rsa.crc32c = KeyCRC(rsaGenKeys);
            rsa.expiration = DateTimeExtensions.ToDateTimeOffsetSec((Int64) hours * 60 * 60);

            return rsa;
        }



        // ==========================
        // ==== RsaKeyList below ====

        public static bool CanGenerateNewKey(RsaKeyList listRsa)
        {
            // Do a check here. If there are any queued packages with 
            // pair.previous then return false
            // Add function to extend the lifetime of the current key
            // if the previous is blocking
            //
            return true;
        }

        public static RsaKey GetCurrentKey(RsaKeyList listRsa)
        {
            if (listRsa.listRSA.Count < 1)
                throw new Exception("List is empty, no public key");

            return listRsa.listRSA.First.Value;
        }

        public static RsaKey findKey(RsaKeyList listRsa, UInt32 publicKeyCrc)
        {
            if (listRsa.listRSA.Count < 1)
                throw new Exception("List is empty, no public key");

            if (listRsa.listRSA.First.Value.crc32c == publicKeyCrc)
                return listRsa.listRSA.First.Value;
            else
            {
                // Check if the previous key matches (but don't check further)
                if (listRsa.listRSA.First.Next != null)
                {
                    if (listRsa.listRSA.First.Next.Value.crc32c == publicKeyCrc)
                    {
                        // XXX TODO: Add some timeout sanity check here.
                        // E.g. don't accept it older than 1 hour expired or whatever
                        return listRsa.listRSA.First.Next.Value;
                    }
                }
            }

            return null;
        }

        public static UInt64 getExpiration(RsaKeyList listRsa)
        {
            return GetCurrentKey(listRsa).expiration;
        }


        public static string GetCurrentPublicKeyPem(RsaKeyList listRsa)
        {
            if (listRsa.listRSA.Count < 1)
                throw new Exception("List is empty, no public key");

            return publicPem(GetCurrentKey(listRsa));
        }



        public static RSACryptoServiceProvider findKeyPublic(RsaKeyList listRsa, UInt32 publicKeyCrc)
        {
            var key = findKey(listRsa, publicKeyCrc);

            return KeyPublic(key);
        }

        public static RSACryptoServiceProvider findKeyPrivate(RsaKeyList listRsa, UInt32 publicKeyCrc)
        {
            var key = findKey(listRsa, publicKeyCrc);

            return KeyPrivate(key);
        }



        // We should have a convention that if there is less than e.g. an hour to 
        // key expiration then the requestor should request a new key.
        // The host should create a new key when there is less than two hours. 
        // The precise timing depends on how quickly we want keys to expire,
        // maybe the minimum is 24 hours. Generating a new key takes a significant
        // amount of CPU.
        public static void generateNewKey(RsaKeyList listRsa, int hours)
        {
            if (hours < 24)
                throw new Exception("RSA key must live for at least 24 hours");

            if (CanGenerateNewKey(listRsa) == false)
                throw new Exception("Cannot generate new RSA key because the previous is in use");
                // Maybe extend the current key

            var rsa = NewKey(hours);
            listRsa.listRSA.AddFirst(rsa);
            if (listRsa.listRSA.Count > listRsa.maxKeys)
                listRsa.listRSA.RemoveLast();
        }
    }
}
