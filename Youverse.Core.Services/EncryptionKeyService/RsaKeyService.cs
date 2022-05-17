using System;
using System.Threading.Tasks;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.EncryptionKeyService
{
    public class RsaKeyService : IPublicKeyService
    {
        private const string RecipientPublicOfflineKeyCache = "pkocache";
        private const string RsaOfflineKeyStorage = "rks";
        private readonly Guid RSA_KEY_STORAGE_ID = Guid.Parse("FFFFFFCF-0f85-DDDD-a7eb-e8e0b06c2555");

        private readonly ISystemStorage _systemStorage;
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDotYouHttpClientFactory _dotYouHttpClientFactory;

        public RsaKeyService(ISystemStorage systemStorage, DotYouContextAccessor contextAccessor, IDotYouHttpClientFactory dotYouHttpClientFactory)
        {
            _systemStorage = systemStorage;
            _contextAccessor = contextAccessor;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
        }

        public async Task<byte[]> DecryptKeyHeaderUsingOfflineKey(byte[] encryptedData, uint publicKeyCrc32, bool failIfNoMatchingPublicKey = true)
        {
            var rsaKey = await this.GetOfflineKeyInternal();
            var key = GetOfflineKeyDecryptionKey();
            var keyList = rsaKey.Keys;
            var pk = RsaKeyListManagement.FindKey(keyList, publicKeyCrc32);

            if (null == pk)
            {
                if (failIfNoMatchingPublicKey)
                {
                    throw new YouverseSecurityException("Invalid public key");
                }

                return null;
            }

            var bytes = pk.Decrypt(ref key, encryptedData);
            return bytes;
        }

        public async Task<byte[]> DecryptPayloadUsingOfflineKey(RsaEncryptedPayload payload)
        {
            var keyHeaderBytes = await this.DecryptKeyHeaderUsingOfflineKey(payload.KeyHeader, payload.Crc32);
            var keyHeader = KeyHeader.FromCombinedBytes(keyHeaderBytes);

            var key = keyHeader.AesKey;
            var bytes = Cryptography.Crypto.AesCbc.Decrypt(
                cipherText: payload.Data,
                Key: ref key,
                IV: keyHeader.Iv);

            return bytes;
        }

        public async Task<bool> IsValidPublicKey(UInt32 crc)
        {
            return null != await GetOfflinePublicKey(crc);
        }

        public async Task<RsaFullKeyData> GetOfflinePublicKey(UInt32 crc)
        {
            var keySet = await this.GetOfflineKeyInternal();
            var key = RsaKeyListManagement.FindKey(keySet.Keys, crc);
            return key;
        }

        public async Task<RsaPublicKeyData> GetOfflinePublicKey()
        {
            var rsaKey = await this.GetOfflineKeyInternal();

            var key = GetOfflineKeyDecryptionKey();
            var keyList = rsaKey.Keys;
            var pk = RsaKeyListManagement.GetCurrentKey(ref key, ref keyList, out var keyListWasUpdated); // TODO
            if (keyListWasUpdated)
            {
                rsaKey.Keys = keyList;
                _systemStorage.WithTenantSystemStorage<RsaOfflineKeySet>(RsaOfflineKeyStorage, s => s.Save(rsaKey));
            }

            return pk;
        }


        public async Task<RsaPublicKeyData> GetRecipientOfflinePublicKey(DotYouIdentity recipient, bool lookupIfInvalid = true, bool failIfCannotRetrieve = true)
        {
            //TODO: need to clean up the cache for expired items

            //TODO: optimize by reading a dictionary cache
            var cacheItem = await _systemStorage.WithTenantSystemStorageReturnSingle<RecipientPublicKeyCacheItem>(RecipientPublicOfflineKeyCache, s => s.Get(recipient));

            if ((cacheItem == null || cacheItem.PublicKeyData.IsExpired()) && lookupIfInvalid)
            {
                var svc = _dotYouHttpClientFactory.CreateClient<IEncryptionKeyServiceHttpClient>(recipient);
                var tpkResponse = await svc.GetOfflinePublicKey();

                if (tpkResponse.Content == null || !tpkResponse.IsSuccessStatusCode)
                {
                    // this._logger.LogWarning("Transit public key is invalid");
                    return null;
                }

                var publicKeyData = tpkResponse.Content;
                cacheItem = new RecipientPublicKeyCacheItem()
                {
                    Id = recipient,
                    PublicKeyData = publicKeyData
                };

                _systemStorage.WithTenantSystemStorage<RecipientPublicKeyCacheItem>(RecipientPublicOfflineKeyCache, s => s.Save(cacheItem));
            }

            if (null == cacheItem && failIfCannotRetrieve)
            {
                throw new MissingDataException("Could not get recipients offline public key");
            }

            return cacheItem?.PublicKeyData;
        }

        public async Task<RsaEncryptedPayload> EncryptPayloadForRecipient(string recipient, byte[] payload)
        {
            var pk = await this.GetRecipientOfflinePublicKey((DotYouIdentity) recipient);
            var keyHeader = KeyHeader.NewRandom16();
            return new RsaEncryptedPayload()
            {
                Crc32 = pk.crc32c,
                KeyHeader = pk.Encrypt(keyHeader.Combine().GetKey()),
                Data = keyHeader.GetEncryptedStreamAes(payload).ToByteArray()
            };
        }


        /// 
        private async Task<RsaOfflineKeySet> GetRsaHeader(string storage)
        {
            var result = await _systemStorage.WithTenantSystemStorageReturnSingle<RsaOfflineKeySet>(storage, s => s.Get(RSA_KEY_STORAGE_ID));
            return result;
        }

        private async Task<RsaOfflineKeySet> GetOfflineKeyInternal()
        {
            var result = await _systemStorage.WithTenantSystemStorageReturnSingle<RsaOfflineKeySet>(RsaOfflineKeyStorage, s => s.Get(RSA_KEY_STORAGE_ID));

            if (result == null || result.Keys?.ListRSA == null)
            {
                var key = GetOfflineKeyDecryptionKey();
                var rsaKeyList = RsaKeyListManagement.CreateRsaKeyList(ref key, 2);

                var rsa = new RsaOfflineKeySet()
                {
                    Id = RSA_KEY_STORAGE_ID,
                    Keys = rsaKeyList
                };

                _systemStorage.WithTenantSystemStorage<RsaOfflineKeySet>(RsaOfflineKeyStorage, s => s.Save(rsa));

                return rsa;
            }

            return result;
        }

        private SensitiveByteArray GetOfflineKeyDecryptionKey()
        {
            //fixed key
            byte[] keyBytes = new byte[16];
            Array.Fill<byte>(keyBytes, 1);
            return keyBytes.ToSensitiveByteArray();
        }
    }
}