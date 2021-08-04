using DotYou.AdminClient.Extensions;
using DotYou.Kernel.Services.Admin.Authentication;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace DotYou.Kernel.Cryptography
{

    // So it's slightly messy to mix up the version with encrypted and unencrypted private key.
    // Not sure if I should break it into two almost identical classes.
    public static class RsaKeyListManagement
    {
        const int DefaultKeyHours = 24;

        public static RsaKeyListData CreateRsaKeyList(int max, int hours = DefaultKeyHours)
        {
            if (max < 1)
                throw new Exception("Max cannot be less than 1");

            if (hours < 24)
                throw new Exception("Hours cannot be less than 24");

            var rkl = new RsaKeyListData();
            rkl.listRSA = new LinkedList<RsaKeyData>();
            rkl.maxKeys = max;

            GenerateNewKey(rkl, hours);

            return rkl;
        }

        public static bool CanGenerateNewKey(RsaKeyListData listRsa)
        {
            // Do a check here. If there are any queued packages with 
            // pair.previous then return false
            // Add function to extend the lifetime of the current key
            // if the previous is blocking
            //
            return true;
        }

        // We should have a convention that if there is less than e.g. an hour to 
        // key expiration then the requestor should request a new key.
        // The host should create a new key when there is less than two hours. 
        // The precise timing depends on how quickly we want keys to expire,
        // maybe the minimum is 24 hours. Generating a new key takes a significant
        // amount of CPU.
        public static void GenerateNewKey(RsaKeyListData listRsa, int hours)
        {
            if (hours < 24)
                throw new Exception("RSA key must live for at least 24 hours");

            if (CanGenerateNewKey(listRsa) == false)
                throw new Exception("Cannot generate new RSA key because the previous is in use");

            var rsa = RsaKeyManagement.NewKey(hours);

            listRsa.listRSA.AddFirst(rsa);
            if (listRsa.listRSA.Count > listRsa.maxKeys)
                listRsa.listRSA.RemoveLast();
        }


        public static RsaKeyData GetCurrentKey(RsaKeyListData listRsa)
        {
            if ((listRsa.listRSA == null) || (listRsa.listRSA.Count < 1))
                throw new Exception("List shouldn't be null / empty");

            return listRsa.listRSA.First.Value;
        }

        public static RsaKeyData FindKey(RsaKeyListData listRsa, UInt32 publicKeyCrc)
        {
            var RsaKey = GetCurrentKey(listRsa); // Will create a key if none are present

            if (RsaKey.crc32c == publicKeyCrc)
                return RsaKey;
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

        public static UInt64 GetExpiration(RsaKeyListData listRsa)
        {
            return GetCurrentKey(listRsa).expiration;
        }


        public static string GetCurrentPublicKeyPem(RsaKeyListData listRsa)
        {
            return RsaKeyManagement.publicPem(GetCurrentKey(listRsa));
        }



        public static RSACryptoServiceProvider FindKeyPublic(RsaKeyListData listRsa, UInt32 publicKeyCrc)
        {
            var key = FindKey(listRsa, publicKeyCrc);

            return RsaKeyManagement.KeyPublic(key);
        }

        public static RSACryptoServiceProvider FindKeyPrivate(RsaKeyListData listRsa, UInt32 publicKeyCrc)
        {
            var key = FindKey(listRsa, publicKeyCrc);

            return RsaKeyManagement.KeyPrivate(key);
        }
    }
}
