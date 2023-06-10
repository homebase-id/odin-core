using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Services.Base;
using Odin.Core.Services.Mediator.Owner;
using Odin.Core.Services.Transit.Encryption;
using Odin.Core.Time;

namespace Odin.Core.Services.EncryptionKeyService
{
    public class RsaKeyService : INotificationHandler<OwnerIsOnlineNotification>
    {
        private readonly Guid _offlineKeyStorageId = Guid.Parse("AAAAAAAF-0f85-EEEE-E77E-e8e0b06c2777");
        private readonly Guid _onlineKeyStorageId = Guid.Parse("fc187615-deb4-4222-bcb5-35411bae25e3");
        private readonly Guid _signingKeyStorageId = Guid.Parse("d61a2789-2bc0-46c9-b6b9-19dcb3d076ab");

        private readonly TenantSystemStorage _tenantSystemStorage;
        private readonly OdinContextAccessor _contextAccessor;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory;
        private static readonly SemaphoreSlim _rsaRecipientPublicKeyCacheLock = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim _onlineKeyCreationLock = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim _keyCreationLock = new SemaphoreSlim(1, 1);

        public RsaKeyService(TenantSystemStorage tenantSystemStorage, OdinContextAccessor contextAccessor, IOdinHttpClientFactory odinHttpClientFactory)
        {
            _tenantSystemStorage = tenantSystemStorage;
            _contextAccessor = contextAccessor;
            _odinHttpClientFactory = odinHttpClientFactory;
        }

        public async Task<(bool, byte[])> DecryptKeyHeaderUsingOfflineKey(byte[] encryptedData, uint publicKeyCrc32, bool failIfNoMatchingPublicKey = true)
        {
            throw new NotImplementedException("TODO: switch to using online key and rename");
            // var keys = await this.GetOfflineKeyInternal();
            // var key = GetOfflineKeyDecryptionKey();
            // var pk = RsaKeyListManagement.FindKey(keys, publicKeyCrc32);
            //
            // if (null == pk)
            // {
            //     if (failIfNoMatchingPublicKey)
            //     {
            //         throw new OdinSecurityException("Invalid public key");
            //     }
            //
            //     return (false, null);
            // }
            //
            // var bytes = pk.Decrypt(ref key, encryptedData);
            // return (true, bytes);
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

        /// <summary>
        /// Destroys the cache item for the recipients public key so a new one will be retrieved
        /// </summary>
        /// <param name="recipient"></param>
        /// <returns></returns>
        public async Task InvalidatePublicKey(OdinId recipient)
        {
            _tenantSystemStorage.SingleKeyValueStorage.Delete(GuidId.FromString(recipient.DomainName));
            await Task.CompletedTask;
        }

        /// <summary>
        /// Gets the latest effective offline public key
        /// </summary>
        public Task<RsaPublicKeyData> GetOfflinePublicKey()
        {
            var k = this.GetCurrentKeyFromStorage(_offlineKeyStorageId);
            return Task.FromResult(RsaPublicKeyData.FromDerEncodedPublicKey(k.publicKey));
        }

        public async Task<RsaPublicKeyData> GetRecipientOfflinePublicKey(OdinId recipient, bool lookupIfInvalid = true, bool failIfCannotRetrieve = true)
        {
            //TODO: need to clean up the cache for expired items
            //TODO: optimize by reading a dictionary cache

            await _rsaRecipientPublicKeyCacheLock.WaitAsync();
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
                _rsaRecipientPublicKeyCacheLock.Release();
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

        public Task Handle(OwnerIsOnlineNotification notification, CancellationToken cancellationToken)
        {
            //TODO: add logic to ensure we only call this periodically 
            this.CreateOrRotateOnlineKeys().GetAwaiter().GetResult();
            return Task.CompletedTask;
        }

        public Task<RsaPublicKeyData> GetSigningPublicKey()
        {
            var k = this.GetCurrentKeyFromStorage(_signingKeyStorageId);
            return Task.FromResult(RsaPublicKeyData.FromDerEncodedPublicKey(k.publicKey));
        }

        public Task<RsaPublicKeyData> GetOnlinePublicKey()
        {
            var k = this.GetCurrentKeyFromStorage(_onlineKeyStorageId);
            return Task.FromResult(RsaPublicKeyData.FromDerEncodedPublicKey(k.publicKey));
        }

        /// <summary>
        /// Creates the initial set of RSA keys required by an identity
        /// </summary>
        internal async Task CreateInitialKeys()
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            try
            {
                await _keyCreationLock.WaitAsync();
                var mk = _contextAccessor.GetCurrent().Caller.GetMasterKey();

                await this.CreateNewRsaKeys(mk, _onlineKeyStorageId);
                await this.CreateNewRsaKeys(mk, _signingKeyStorageId);
                await this.CreateNewRsaKeys(GetOfflineKeyDecryptionKey(), _offlineKeyStorageId);
            }
            finally
            {
                _keyCreationLock.Release();
            }
        }

        private SensitiveByteArray GetOfflineKeyDecryptionKey()
        {
            //fixed key
            byte[] keyBytes = new byte[16];
            Array.Fill<byte>(keyBytes, 1);
            return keyBytes.ToSensitiveByteArray();
        }

        /// <summary>
        /// Ensures online RSA keys exist and are update with the latest possible
        /// </summary>
        private async Task CreateOrRotateOnlineKeys()
        {
            await _onlineKeyCreationLock.WaitAsync();

            try
            {
                _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
                var keySet = _tenantSystemStorage.SingleKeyValueStorage.Get<RsaFullKeyListData>(_onlineKeyStorageId);
                var key = _contextAccessor.GetCurrent().Caller.GetMasterKey();
                if (RsaKeyListManagement.IsValidKeySet(keySet))
                {
                    bool shouldRotate = true; //TODO: what is the logic for this?
                    if (shouldRotate)
                    {
                        int onlineKeyTTL = 48; //TODO: config
                        RsaKeyListManagement.GenerateNewKey(key, keySet, onlineKeyTTL);
                        _tenantSystemStorage.SingleKeyValueStorage.Upsert(_onlineKeyStorageId, keySet);
                    }
                }
                else
                {
                    await this.CreateNewRsaKeys(key, _onlineKeyStorageId);
                }
            }
            finally
            {
                _onlineKeyCreationLock.Release();
            }
        }

        private Task CreateNewRsaKeys(SensitiveByteArray encryptionKey, Guid storageKey)
        {
            //create a new key list
            var rsaKeyList = RsaKeyListManagement.CreateRsaKeyList(encryptionKey,
                RsaKeyListManagement.DefaultMaxOnlineKeys,
                RsaKeyListManagement.DefaultHoursOnlineKey);

            _tenantSystemStorage.SingleKeyValueStorage.Upsert(storageKey, rsaKeyList);

            return Task.CompletedTask;
        }

        private RsaFullKeyData GetCurrentKeyFromStorage(Guid storageKey)
        {
            var k = _tenantSystemStorage.SingleKeyValueStorage.Get<RsaFullKeyListData>(storageKey);
            return RsaKeyListManagement.GetCurrentKey(k);
        }
    }
}