using System;
using System.Collections.Generic;
using Odin.Core.Cryptography.Data;
using Odin.Core;

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

        public const int DefaultHoursOfflineKey = 1 * 24;   // 1 day
        public const int DefaultHoursOnlineKey = 365 * 24;  // 2 years
        public const int DefaultHoursSignatureKey = 5 * 365 * 24;  // 5 years

        public const int DefaultMaxOfflineKeys = 2;
        public const int DefaultMaxOnlineKeys = 2;
        public const int DefaultMaxSignatureKeys = 2;

        private const int MinimumKeyHours = 24;


        public static RsaFullKeyListData CreateRsaKeyList(SensitiveByteArray key, int maxKeysInList, int hours)
        {
            if (maxKeysInList < 1)
                throw new Exception("Max cannot be less than 1");

            if (hours < MinimumKeyHours)
                throw new Exception("Hours cannot be less than 24");

            var rkl = new RsaFullKeyListData();
            rkl.ListRSA = new List<RsaFullKeyData>();
            rkl.MaxKeys = maxKeysInList;

            GenerateNewKey(key, rkl, hours);

            return rkl;
        }

        // REMEMBER TO SAVE THE LIST WHEN YOU CHANGE IT
        //
        // We should have a convention that if there is less than e.g. an hour to 
        // key expiration then the requestor should request a new key.
        // The host should create a new key when there is less than two hours. 
        // The precise timing depends on how quickly we want keys to expire,
        // maybe the minimum is 24 hours. Generating a new key takes a significant
        // amount of CPU.
        public static void GenerateNewKey(SensitiveByteArray key, RsaFullKeyListData listRsa, int hours)
        {
            if (hours < MinimumKeyHours)
                throw new Exception("RSA key must live for at least 24 hours");

            lock (listRsa)
            {
                var rsa = new RsaFullKeyData(ref key, hours);

                listRsa.ListRSA.Insert(0, rsa);
                if (listRsa.ListRSA.Count > listRsa.MaxKeys)
                    listRsa.ListRSA.RemoveAt(listRsa.ListRSA.Count - 1); // Remove last
            }
        }


        public static RsaFullKeyData GetCurrentKey(RsaFullKeyListData listRsa)
        {
            if (listRsa.ListRSA == null)
                throw new Exception("List shouldn't be null");

            lock (listRsa)
            {
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

                for (int i = 0; i < listRsa.ListRSA.Count; i++)
                {
                    if (listRsa.ListRSA[i].crc32c == publicKeyCrc)
                        return listRsa.ListRSA[i];

                }

                return null;
            }
        }
    }
}