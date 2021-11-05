using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Cryptography.Crypto
{
    // So it's slightly messy to mix up the version with encrypted and unencrypted private key.
    // Not sure if I should break it into two almost identical classes.

    // So I hacked this from a linked list to an array (for ease of storage)
    // So it might be a bit counter intuitive. I'll have to cycle back and clean it up
    // but it'll morph anyway when I consider how to support other key types.
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
            rkl.ListRSA = new List<RsaKeyData>();
            rkl.MaxKeys = max;

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

            var rsa = RsaKeyManagement.CreateKey(hours);

            listRsa.ListRSA.Insert(0, rsa);
            if (listRsa.ListRSA.Count > listRsa.MaxKeys)
                listRsa.ListRSA.RemoveAt(listRsa.ListRSA.Count - 1); // Remove last
        }


        public static RsaKeyData GetCurrentKey(ref RsaKeyListData listRsa, out bool wasUpdated)
        {
            wasUpdated = false;
            
            if (listRsa.ListRSA == null)
                throw new Exception("List shouldn't be null");

            if (RsaKeyManagement.IsDead(listRsa.ListRSA[0]))
            {
                listRsa.ListRSA.RemoveAt(0); // Remove First
                GenerateNewKey(listRsa, DefaultKeyHours);
                wasUpdated = true;
            }

            if (listRsa.ListRSA.Count < 1)
            {
                GenerateNewKey(listRsa, DefaultKeyHours);
                wasUpdated = true;
            }

            return listRsa.ListRSA[0]; // First
        }

        /// <summary>
        /// Will return a valid or expired key, but remove any dead keys
        /// </summary>
        /// <param name="listRsa"></param>
        /// <param name="publicKeyCrc"></param>
        /// <returns></returns>
        public static RsaKeyData FindKey(RsaKeyListData listRsa, UInt32 publicKeyCrc)
        {
            if (listRsa.ListRSA == null)
                throw new Exception("List shouldn't be null");

            if (listRsa.ListRSA[0] != null)
            {
                if (RsaKeyManagement.IsDead(listRsa.ListRSA[0]))
                {
                    listRsa.ListRSA.RemoveAt(0);
                    return FindKey(listRsa, publicKeyCrc);
                }

                if (listRsa.ListRSA[0].crc32c == publicKeyCrc)
                    return listRsa.ListRSA[0];
            }

            // Check if the previous key matches (but don't check further)
            if (listRsa.ListRSA.Count >= 2)
            {
                if (RsaKeyManagement.IsDead(listRsa.ListRSA[1]))
                {
                    listRsa.ListRSA.RemoveAt(1);
                    return null;
                }

                if (listRsa.ListRSA[1].crc32c == publicKeyCrc)
                {
                    // XXX TODO: Add some timeout sanity check here.
                    // E.g. don't accept it older than 1 hour expired or whatever
                    return listRsa.ListRSA[1];
                }
            }

            return null;
        }

        public static UInt64 GetExpiration(RsaKeyListData listRsa)
        {
            return GetCurrentKey(ref listRsa, out var _).expiration;
        }


        public static string GetCurrentPublicKeyPem(RsaKeyListData listRsa)
        {
            return RsaKeyManagement.publicPem(GetCurrentKey(ref listRsa, out var _));
        }

        public static RSACng FindKeyPublic(RsaKeyListData listRsa, UInt32 publicKeyCrc)
        {
            var key = FindKey(listRsa, publicKeyCrc);

            return RsaKeyManagement.KeyPublic(key);
        }

        public static RSACng FindKeyPrivate(RsaKeyListData listRsa, UInt32 publicKeyCrc)
        {
            var key = FindKey(listRsa, publicKeyCrc);

            return RsaKeyManagement.KeyPrivate(key);
        }
    }
}