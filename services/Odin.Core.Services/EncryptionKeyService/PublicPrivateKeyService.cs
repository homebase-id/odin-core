using System;
using System.Linq;
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
    public class PublicPrivateKeyService : INotificationHandler<OwnerIsOnlineNotification>
    {
        private readonly Guid _offlineKeyStorageId = Guid.Parse("AAAAAAAF-0f85-EEEE-E77E-e8e0b06c2777");
        private readonly Guid _onlineKeyStorageId = Guid.Parse("fc187615-deb4-4222-bcb5-35411bae25e3");
        private readonly Guid _signingKeyStorageId = Guid.Parse("d61a2789-2bc0-46c9-b6b9-19dcb3d076ab");

        private readonly TenantSystemStorage _tenantSystemStorage;
        private readonly OdinContextAccessor _contextAccessor;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory;
        private readonly IMediator _mediator;

        private static readonly SemaphoreSlim RsaRecipientOfflinePublicKeyCacheLock = new(1, 1);
        private static readonly SemaphoreSlim RsaRecipientOnlinePublicKeyCacheLock = new(1, 1);
        private static readonly SemaphoreSlim OnlineKeyCreationLock = new(1, 1);
        private static readonly SemaphoreSlim KeyCreationLock = new(1, 1);
        private static readonly byte[] OfflinePrivateKeyEncryptionKey = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        public PublicPrivateKeyService(TenantSystemStorage tenantSystemStorage, OdinContextAccessor contextAccessor, IOdinHttpClientFactory odinHttpClientFactory,
            IMediator mediator)
        {
            _tenantSystemStorage = tenantSystemStorage;
            _contextAccessor = contextAccessor;
            _odinHttpClientFactory = odinHttpClientFactory;
            _mediator = mediator;
        }

        /// <summary>
        /// Destroys the cache item for the recipients public key so a new one will be retrieved
        /// </summary>
        /// <param name="recipient"></param>
        /// <returns></returns>
        public async Task InvalidateRecipientPublicKey(OdinId recipient)
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

        private async Task<RsaPublicKeyData> ResolveRecipientKey(RsaKeyType keyType, OdinId recipient, bool failIfCannotRetrieve = true)
        {
            await RsaRecipientOnlinePublicKeyCacheLock.WaitAsync();
            try
            {
                string prefix = keyType == RsaKeyType.OfflineKey ? "offline" :
                    keyType == RsaKeyType.OnlineKey ? "online" : throw new OdinSystemException("Unhandled key type");

                GuidId cacheKey = GuidId.FromString($"{prefix}{recipient.DomainName}");

                var cacheItem = _tenantSystemStorage.SingleKeyValueStorage.Get<RsaPublicKeyData>(cacheKey);

                if ((cacheItem == null || cacheItem.IsExpired()))
                {
                    var svc = _odinHttpClientFactory.CreateClient<IEncryptionKeyServiceHttpClient>(recipient);
                    var tpkResponse = await svc.GetPublicKey(keyType);

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

                    _tenantSystemStorage.SingleKeyValueStorage.Upsert(cacheKey, cacheItem);
                }

                if (null == cacheItem && failIfCannotRetrieve)
                {
                    throw new MissingDataException("Could not get recipients offline public key");
                }

                return cacheItem;
            }
            finally
            {
                RsaRecipientOnlinePublicKeyCacheLock.Release();
            }
        }

        public async Task<RsaEncryptedPayload> EncryptPayload(RsaKeyType keyType, byte[] payload)
        {
            RsaPublicKeyData pk;

            switch (keyType)
            {
                case RsaKeyType.OfflineKey:
                    pk = await this.GetOfflinePublicKey();
                    break;

                case RsaKeyType.OnlineKey:
                    pk = await this.GetOnlinePublicKey();
                    break;

                default:
                    throw new OdinSystemException("Unhandled RsaKeyType");
            }

            return Encrypt(pk, payload);
        }

        public async Task<RsaEncryptedPayload> EncryptPayloadForRecipient(RsaKeyType keyType, OdinId recipient, byte[] payload)
        {
            RsaPublicKeyData pk = await ResolveRecipientKey(keyType, recipient);
            return Encrypt(pk, payload);
        }

        public Task Handle(OwnerIsOnlineNotification notification, CancellationToken cancellationToken)
        {
            //TODO: add logic to ensure we only call this periodically 
            this.CreateOrRotateOnlineKeys().GetAwaiter().GetResult();
            return Task.CompletedTask;
        }

        public Task<EccPublicKeyData> GetSigningPublicKey()
        {
            var k = this.GetCurrentKeyFromStorage(_signingKeyStorageId);
            return Task.FromResult(EccPublicKeyData.FromDerEncodedPublicKey(k.publicKey));
        }

        public Task<RsaPublicKeyData> GetOnlinePublicKey()
        {
            var k = this.GetCurrentKeyFromStorage(_onlineKeyStorageId);
            return Task.FromResult(RsaPublicKeyData.FromDerEncodedPublicKey(k.publicKey));
        }

        /// <summary>
        /// Upgrades the key to the latest
        /// </summary>
        public async Task<RsaEncryptedPayload> UpgradeRsaKey(RsaKeyType keyType, RsaFullKeyData currentKey, SensitiveByteArray currentDecryptionKey,
            RsaEncryptedPayload payload)
        {
            //unwrap the rsa encrypted key header
            var keyHeader = await DecryptKeyHeaderInternal(currentKey, currentDecryptionKey, payload.RsaEncryptedKeyHeader);

            var pk = await this.GetPublicRsaKey(keyType);

            //re-encrypt the key header using the latest rsa key
            //Note: we do not need to re-encrypt the KeyHeaderEncryptedData because we never changed it
            return new RsaEncryptedPayload()
            {
                //Note: i leave out the key type here because the methods that receive
                //this must decide the encryption they expect
                Crc32 = pk.crc32c,
                RsaEncryptedKeyHeader = pk.Encrypt(keyHeader.Combine().GetKey()),
                KeyHeaderEncryptedData = payload.KeyHeaderEncryptedData
            };
        }

        public async Task<(bool IsValidPublicKey, byte[] DecryptedBytes)> DecryptPayload(RsaKeyType keyType, RsaEncryptedPayload payload)
        {
            var (isValidPublicKey, keyHeader) = await this.DecryptKeyHeader(keyType, payload.RsaEncryptedKeyHeader, payload.Crc32);

            if (!isValidPublicKey)
            {
                return (false, null);
            }

            var key = keyHeader.AesKey;
            var bytes = AesCbc.Decrypt(
                cipherText: payload.KeyHeaderEncryptedData,
                Key: ref key,
                IV: keyHeader.Iv);

            return (true, bytes);
        }

        public async Task<RsaPublicKeyData> GetPublicRsaKey(RsaKeyType keyType)
        {
            switch (keyType)
            {
                case RsaKeyType.OfflineKey:
                    return await this.GetOfflinePublicKey();
                case RsaKeyType.OnlineKey:
                    return await this.GetOnlinePublicKey();
                default:
                    throw new ArgumentOutOfRangeException(nameof(keyType), keyType, null);
            }
        }

        /// <summary>
        /// Creates the initial set of RSA keys required by an identity
        /// </summary>
        internal async Task CreateInitialKeys()
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            try
            {
                await KeyCreationLock.WaitAsync();
                var mk = _contextAccessor.GetCurrent().Caller.GetMasterKey();

                await this.CreateNewRsaKeys(mk, _onlineKeyStorageId);
                await this.CreateNewEccKeys(mk, _signingKeyStorageId);
                await this.CreateNewRsaKeys(OfflinePrivateKeyEncryptionKey.ToSensitiveByteArray(), _offlineKeyStorageId);
            }
            finally
            {
                KeyCreationLock.Release();
            }
        }

        private async Task<(bool, KeyHeader)> DecryptKeyHeader(RsaKeyType keyType, byte[] encryptedData, uint publicKeyCrc32,
            bool failIfNoMatchingPublicKey = true)
        {
            var (rsaKey, decryptionKey) = await ResolveKeyForDecryption(keyType, publicKeyCrc32);

            if (null == rsaKey)
            {
                if (failIfNoMatchingPublicKey)
                {
                    throw new OdinSecurityException("Invalid public key");
                }

                return (false, null);
            }

            var bytes = rsaKey.Decrypt(ref decryptionKey, encryptedData);
            var keyHeader = KeyHeader.FromCombinedBytes(bytes);

            return (true, keyHeader);
        }

        private Task<KeyHeader> DecryptKeyHeaderInternal(RsaFullKeyData rsaKey, SensitiveByteArray decryptionKey, byte[] encryptedData)
        {
            var bytes = rsaKey.Decrypt(ref decryptionKey, encryptedData);
            return Task.FromResult(KeyHeader.FromCombinedBytes(bytes));
        }

        private Task<(RsaFullKeyData RsaKey, SensitiveByteArray DecryptionKey)> ResolveKeyForDecryption(RsaKeyType keyType, uint crc32)
        {
            RsaFullKeyListData keyList;
            SensitiveByteArray decryptionKey;

            switch (keyType)
            {
                case RsaKeyType.OfflineKey:
                    keyList = this.GetKeyListFromStorage(_offlineKeyStorageId);
                    decryptionKey = OfflinePrivateKeyEncryptionKey.ToSensitiveByteArray();
                    break;

                case RsaKeyType.OnlineKey:
                    keyList = this.GetKeyListFromStorage(_onlineKeyStorageId);
                    decryptionKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(keyType), keyType, null);
            }

            var pk = RsaKeyListManagement.FindKey(keyList, crc32);
            return Task.FromResult((pk, decryptionKey));
        }

        /// <summary>
        /// Ensures online RSA keys exist and are update with the latest possible
        /// </summary>
        private async Task CreateOrRotateOnlineKeys()
        {
            await OnlineKeyCreationLock.WaitAsync();

            try
            {
                _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
                var keySet = _tenantSystemStorage.SingleKeyValueStorage.Get<RsaFullKeyListData>(_onlineKeyStorageId);
                var key = _contextAccessor.GetCurrent().Caller.GetMasterKey();
                if (RsaKeyListManagement.IsValidKeySet(keySet))
                {
                    bool shouldRotate = false; //TODO: what is the logic for this?
                    if (shouldRotate)
                    {
                        int onlineKeyTTL = 48; //TODO: config

                        RsaKeyListManagement.GenerateNewKey(key, keySet, onlineKeyTTL);
                        _tenantSystemStorage.SingleKeyValueStorage.Upsert(_onlineKeyStorageId, keySet);

                        // await _mediator.Publish(new RsaKeyRotatedNotification(RsaKeyType.OnlineKey, expiredKeys, keySet));
                    }
                }
                else
                {
                    await this.CreateNewRsaKeys(key, _onlineKeyStorageId);
                }
            }
            finally
            {
                OnlineKeyCreationLock.Release();
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
        
        private Task CreateNewEccKeys(SensitiveByteArray encryptionKey, Guid storageKey)
        {
            //create a new key list
            var eccKeyList = EccKeyListManagement.CreateEccKeyList(encryptionKey,
                EccKeyListManagement.DefaultMaxOnlineKeys,
                EccKeyListManagement.DefaultHoursOnlineKey);

            _tenantSystemStorage.SingleKeyValueStorage.Upsert(storageKey, eccKeyList);

            return Task.CompletedTask;
        }

        private RsaFullKeyData GetCurrentKeyFromStorage(Guid storageKey)
        {
            var keyList = GetKeyListFromStorage(storageKey);
            return RsaKeyListManagement.GetCurrentKey(keyList);
        }

        private RsaFullKeyListData GetKeyListFromStorage(Guid storageKey)
        {
            return _tenantSystemStorage.SingleKeyValueStorage.Get<RsaFullKeyListData>(storageKey);
        }

        private RsaEncryptedPayload Encrypt(RsaPublicKeyData pk, byte[] payload)
        {
            var keyHeader = KeyHeader.NewRandom16();
            return new RsaEncryptedPayload()
            {
                //Note: i leave out the key type here because the methods that receive
                //this must decide the encryption they expect
                Crc32 = pk.crc32c,
                RsaEncryptedKeyHeader = pk.Encrypt(keyHeader.Combine().GetKey()),
                KeyHeaderEncryptedData = keyHeader.EncryptDataAesAsStream(payload).ToByteArray()
            };
        }
    }
}