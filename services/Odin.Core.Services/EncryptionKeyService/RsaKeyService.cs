using System;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Services.Base;
using Odin.Core.Services.Transit.Encryption;
using Odin.Core.Time;

namespace Odin.Core.Services.EncryptionKeyService
{
    public class RsaKeyService : IPublicKeyService
    {
        private readonly Guid _rsaKeyStorageId = Guid.Parse("AAAAAAAF-0f85-EEEE-E77E-e8e0b06c2777");

        private readonly TenantSystemStorage _tenantSystemStorage;
        private readonly OdinContextAccessor _contextAccessor;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory;

        public RsaKeyService(TenantSystemStorage tenantSystemStorage, OdinContextAccessor contextAccessor, IOdinHttpClientFactory odinHttpClientFactory)
        {
            _tenantSystemStorage = tenantSystemStorage;
            _contextAccessor = contextAccessor;
            _odinHttpClientFactory = odinHttpClientFactory;
        }

        public async Task<(bool, byte[])> DecryptKeyHeaderUsingOfflineKey(byte[] encryptedData, uint publicKeyCrc32, bool failIfNoMatchingPublicKey = true)
        {
            var keys = await this.GetOfflineKeyInternal();
            var key = GetOfflineKeyDecryptionKey();
            var pk = RsaKeyListManagement.FindKey(keys, publicKeyCrc32);

            if (null == pk)
            {
                if (failIfNoMatchingPublicKey)
                {
                    throw new OdinSecurityException("Invalid public key");
                }

                return (false, null);
            }

            var bytes = pk.Decrypt(ref key, encryptedData);
            return (true, bytes);
        }

        public async Task<(bool, byte[])> DecryptPayloadUsingOfflineKey(RsaEncryptedPayload payload)
        {
            var (isValidPublicKey, keyHeaderBytes) = await this.DecryptKeyHeaderUsingOfflineKey(payload.KeyHeader, payload.Crc32);
            var keyHeader = KeyHeader.FromCombinedBytes(keyHeaderBytes);

            if (!isValidPublicKey)
            {
                return (false, null);
            }

            var key = keyHeader.AesKey;
            var bytes = AesCbc.Decrypt(
                cipherText: payload.Data,
                Key: ref key,
                IV: keyHeader.Iv);

            return (isValidPublicKey, bytes);
        }

        public async Task InvalidatePublicKey(OdinId recipient)
        {
            _tenantSystemStorage.SingleKeyValueStorage.Delete(GuidId.FromString(recipient.DomainName));
            await Task.CompletedTask;
        }

        public async Task<bool> IsValidPublicKey(UInt32 crc)
        {
            return null != await GetOfflinePublicKey(crc);
        }

        public async Task<RsaFullKeyData> GetOfflinePublicKey(UInt32 crc)
        {
            var keys = await this.GetOfflineKeyInternal();
            var key = RsaKeyListManagement.FindKey(keys, crc);
            return key;
        }

        public async Task<RsaPublicKeyData> GetOfflinePublicKey()
        {
            var keys = await this.GetOfflineKeyInternal();

            var key = GetOfflineKeyDecryptionKey();

            var pk = RsaKeyListManagement.GetCurrentKey(keys);
            /* TODD TODO RSA LIST - REMEBER TO CREATE & SAVE SOMEWHERE ELSE
            if (keyListWasUpdated)
            {
                _tenantSystemStorage.SingleKeyValueStorage.Upsert(_rsaKeyStorageId, keys);
            }*/

            return pk;
        }


        private static readonly SemaphoreSlim _rsaPublicKeyCacheLock = new SemaphoreSlim(1, 1);

        public async Task<RsaPublicKeyData> GetRecipientOfflinePublicKey(OdinId recipient, bool lookupIfInvalid = true, bool failIfCannotRetrieve = true)
        {
            //TODO: need to clean up the cache for expired items
            //TODO: optimize by reading a dictionary cache

            await _rsaPublicKeyCacheLock.WaitAsync();
            try
            {
                var cacheItem = _tenantSystemStorage.SingleKeyValueStorage.Get<RsaPublicKeyData>(GuidId.FromString(recipient.DomainName));

                if ((cacheItem == null || cacheItem.IsExpired()) && lookupIfInvalid)
                {

                    var svc = _odinHttpClientFactory.CreateClient<IEncryptionKeyServiceHttpClient>(recipient);
                    var tpkResponse = await svc.GetOfflinePublicKey();

                    if (tpkResponse.Content == null || !tpkResponse.IsSuccessStatusCode)
                    {
                        // this._logger.LogWarning("Transit public key is invalid");
                        return null;
                    }

                    cacheItem = new RsaPublicKeyData()
                    {
                        publicKey = tpkResponse.Content.PublicKey,
                        crc32c = tpkResponse.Content.Crc32,
                        expiration = new UnixTimeUtc(tpkResponse.Content.Expiration)
                    };

                    _tenantSystemStorage.SingleKeyValueStorage.Upsert(GuidId.FromString(recipient.DomainName), cacheItem);
                }

                if (null == cacheItem && failIfCannotRetrieve)
                {
                    throw new MissingDataException("Could not get recipients offline public key");
                }

                return cacheItem;
            }
            finally
            {
                _rsaPublicKeyCacheLock.Release();
            }
        }

        public async Task<RsaEncryptedPayload> EncryptPayloadForRecipient(string recipient, byte[] payload)
        {
            var pk = await this.GetRecipientOfflinePublicKey((OdinId)recipient);
            var keyHeader = KeyHeader.NewRandom16();
            return new RsaEncryptedPayload()
            {
                Crc32 = pk.crc32c,
                KeyHeader = pk.Encrypt(keyHeader.Combine().GetKey()),
                Data = keyHeader.EncryptDataAesAsStream(payload).ToByteArray()
            };
        }

        private Task<RsaFullKeyListData> GetOfflineKeyInternal()
        {
            var result = _tenantSystemStorage.SingleKeyValueStorage.Get<RsaFullKeyListData>(_rsaKeyStorageId);

            if (result == null || result.ListRSA == null)
            {
                var key = GetOfflineKeyDecryptionKey();
                var rsaKeyList = RsaKeyListManagement.CreateRsaKeyList(key, 2, RsaKeyListManagement.DefaultHoursOfflineKey);

                _tenantSystemStorage.SingleKeyValueStorage.Upsert(_rsaKeyStorageId, rsaKeyList);
                return Task.FromResult(rsaKeyList);
            }

            return Task.FromResult(result);
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