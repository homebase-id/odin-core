﻿using System;
using System.Collections.Generic;
using Odin.Core;
using Odin.Core.Cryptography.Data;

namespace Odin.Core.Cryptography.Crypto
{
    // So it's slightly messy to mix up the version with encrypted and unencrypted private key.
    // Not sure if I should break it into two almost identical classes.

    // So I hacked this from a linked list to an array (for ease of storage)
    // So it might be a bit counter intuitive. I'll have to cycle back and clean it up
    // but it'll morph anyway when I consider how to support other key types.

    public static class EccKeyListManagement
    {
        public static readonly byte[] zero16 = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        public static SensitiveByteArray zeroSensitiveKey = new SensitiveByteArray(zero16);

        public const int DefaultHoursOfflineKey = 1 * 24; // 1 day
        public const int DefaultHoursOnlineKey = 365 * 24; // 2 years
        public const int DefaultHoursSignatureKey = 5 * 365 * 24; // 5 years

        public const int DefaultMaxOfflineKeys = 2;
        public const int DefaultMaxOnlineKeys = 2;
        public const int DefaultMaxSignatureKeys = 2;

        private const int MinimumKeyHours = 24;


        public static EccFullKeyListData CreateEccKeyList(SensitiveByteArray key, int maxKeysInList, int hours)
        {
            if (maxKeysInList < 1)
                throw new Exception("Max cannot be less than 1");

            if (hours < MinimumKeyHours)
                throw new Exception("Hours cannot be less than 24");

            var rkl = new EccFullKeyListData();
            rkl.ListEcc = new List<EccFullKeyData>();
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
        public static void GenerateNewKey(SensitiveByteArray key, EccFullKeyListData listEcc, int hours)
        {
            if (hours < MinimumKeyHours)
                throw new Exception("Ecc key must live for at least 24 hours");

            lock (listEcc)
            {
                var ecc = new EccFullKeyData(key, EccKeySize.P384, hours);

                listEcc.ListEcc.Insert(0, ecc);
                if (listEcc.ListEcc.Count > listEcc.MaxKeys)
                    listEcc.ListEcc.RemoveAt(listEcc.ListEcc.Count - 1); // Remove last
            }
        }


        public static EccFullKeyData GetCurrentKey(EccFullKeyListData listEcc)
        {
            if (listEcc.ListEcc == null)
                throw new Exception("List shouldn't be null");

            lock (listEcc)
            {
                return listEcc.ListEcc[0]; // First
            }
        }


        /// <summary>
        /// Will return a valid or expired key, but remove any dead keys
        /// </summary>
        /// <param name="listEcc"></param>
        /// <param name="publicKeyCrc"></param>
        /// <returns></returns>
        public static EccFullKeyData FindKey(EccFullKeyListData listEcc, UInt32 publicKeyCrc)
        {
            if (listEcc.ListEcc == null)
                throw new Exception("List shouldn't be null");

            lock (listEcc)
            {
                if (listEcc.ListEcc.Count < 1)
                    return null;

                for (int i = 0; i < listEcc.ListEcc.Count; i++)
                {
                    if (listEcc.ListEcc[i].crc32c == publicKeyCrc)
                        return listEcc.ListEcc[i];
                }

                return null;
            }
        }
    }
}