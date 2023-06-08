using System;
using System.Collections.Generic;
using Odin.Core.Cryptography.Data;

namespace Odin.Core.Cryptography.Crypto
{
    // So it's slightly messy to mix up the version with encrypted and unencrypted private key.
    // Not sure if I should break it into two almost identical classes.

    // So I hacked this from a linked list to an array (for ease of storage)
    // So it might be a bit counter intuitive. I'll have to cycle back and clean it up
    // but it'll morph anyway when I consider how to support other key types.


    public static class RsaKeyListManagement
    {
        public static readonly byte[] zero16 = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        public static SensitiveByteArray zeroSensitiveKey = new SensitiveByteArray(zero16);

        const int DefaultKeyHours = 24;

        public static RsaFullKeyListData CreateRsaKeyList(ref SensitiveByteArray key, int max, int hours = DefaultKeyHours)
        {
            if (max < 1)
                throw new Exception("Max cannot be less than 1");

            if (hours < 24)
                throw new Exception("Hours cannot be less than 24");

            var rkl = new RsaFullKeyListData();
            rkl.ListRSA = new List<RsaFullKeyData>();
            rkl.MaxKeys = max;

            GenerateNewKey(ref key, rkl, hours);

            return rkl;
        }

        public static bool CanGenerateNewKey(RsaFullKeyListData listRsa)
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
        public static void GenerateNewKey(ref SensitiveByteArray key, RsaFullKeyListData listRsa, int hours)
        {
            if (hours < 24)
                throw new Exception("RSA key must live for at least 24 hours");

            lock (listRsa)
            {
                if (CanGenerateNewKey(listRsa) == false)
                    throw new Exception("Cannot generate new RSA key because the previous is in use");

                var rsa = new RsaFullKeyData(ref key, hours);

                listRsa.ListRSA.Insert(0, rsa);
                if (listRsa.ListRSA.Count > listRsa.MaxKeys)
                    listRsa.ListRSA.RemoveAt(listRsa.ListRSA.Count - 1); // Remove last
            }
        }

        /// <summary>
        /// Go through the list of RSA keys and remove all dead instances (dead = expired & 24 hours passed).
        /// </summary>
        /// <param name="listRsa">The list to check for dead keys</param>
        /// <param name="wasUpdated">Will be set to true if any entires were removed</param>
        private static void ScrubDeadKeys(RsaFullKeyListData listRsa, out bool wasUpdated)
        {
            wasUpdated = false;

            // Remove backwards because RemoveAt will change the index.
            //
            for (int i = listRsa.ListRSA.Count - 1; i >= 0; i--)
            {
                if (listRsa.ListRSA[i].IsDead())
                {
                    listRsa.ListRSA.RemoveAt(i);
                    wasUpdated = true;
                    RsaKeyManagement.noKeysExpired++;
                }
            }
        }


        public static RsaFullKeyData GetCurrentKey(ref RsaFullKeyListData listRsa, out bool wasUpdated)
        {
            wasUpdated = false;

            if (listRsa.ListRSA == null)
                throw new Exception("List shouldn't be null");

            lock (listRsa)
            {
                ScrubDeadKeys(listRsa, out wasUpdated);

                if (listRsa.ListRSA.Count < 1)
                    throw new Exception("Key list is empty");

                if (!listRsa.ListRSA[0].IsValid())
                {
                    // Key expired. We'll extend it for 23 hours
                    listRsa.ListRSA[0].Extend(24);
                    wasUpdated = true;
                }

                return listRsa.ListRSA[0]; // First
            }
        }


        public static RsaFullKeyData GetCurrentKey(ref SensitiveByteArray key, ref RsaFullKeyListData listRsa, out bool wasUpdated)
        {
            wasUpdated = false;

            if (listRsa.ListRSA == null)
                throw new Exception("List shouldn't be null");

            lock (listRsa)
            {
                ScrubDeadKeys(listRsa, out wasUpdated);

                if (listRsa.ListRSA.Count < 1)
                {
                    GenerateNewKey(ref key, listRsa, DefaultKeyHours);
                    wasUpdated = true;
                    return listRsa.ListRSA[0];
                }

                return listRsa.ListRSA[0]; // First
            }
        }

        /// <summary>
        /// Will return a valid or expired key, but remove any dead keys
        /// </summary>
        /// <param name="listRsa"></param>
        /// <param name="publicKeyCrc"></param>
        /// <returns></returns>
        public static RsaFullKeyData FindKey(RsaFullKeyListData listRsa, UInt32 publicKeyCrc)
        {
            if (listRsa.ListRSA == null)
                throw new Exception("List shouldn't be null");

            lock (listRsa)
            {
                if (listRsa.ListRSA.Count < 1)
                    return null;

                // var k = listRsa.ListRSA.SingleOrDefault(x => x.IsDead() == false && x.crc32c == publicKeyCrc);
                // return k;

                if (listRsa.ListRSA[0] != null)
                {
                    if (listRsa.ListRSA[0].IsDead())
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
                    if (listRsa.ListRSA[1].IsDead())
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
        }

/*        public static UInt64 GetExpiration(RsaKeyListData listRsa)
        {
            return GetCurrentKey(ref listRsa, out var _).expiration;
        }


        public static string GetCurrentPublicKeyPem(RsaKeyListData listRsa)
        {
            return GetCurrentKey(ref listRsa, out var _).publicPem();
        }*/
    }
}