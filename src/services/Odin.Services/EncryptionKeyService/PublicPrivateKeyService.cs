using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Membership.Connections;
using WebPush;

namespace Odin.Services.EncryptionKeyService
{
    public class NotificationEccKeys
    {
        public string PublicKey64 { get; set; }
        public string PrivateKey64 { get; set; }
    }

    public class PublicPrivateKeyService
    {
        private readonly Guid _signingKeyStorageId = Guid.Parse("d61a2789-2bc0-46c9-b6b9-19dcb3d076ab");
        private readonly Guid _onlineEccKeyStorageId = Guid.Parse("0d4cbb31-bd2e-4910-806c-42a516e63174");

        private readonly Guid _onlineIcrEncryptedEccKeyStorageId = Guid.Parse("f1b4601c-6c50-4443-9113-624b55a8c636");

        private readonly Guid _offlineEccKeyStorageId = Guid.Parse("09529956-bf97-43e8-9822-ad3ecf26819d");
        private readonly Guid _offlineNotificationsKeyStorageId = Guid.Parse("22165337-1ff5-4e92-87f2-95fc9ce424c3");

        private readonly IcrKeyService _icrKeyService;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory;
        private readonly ILogger<PublicPrivateKeyService> _logger;
        private readonly TableKeyValueCached _tblKeyValue;

        private static readonly SemaphoreSlim EccRecipientOnlinePublicKeyCacheLock = new(1, 1);
        private static readonly SemaphoreSlim KeyCreationLock = new(1, 1);
        public static readonly byte[] OfflinePrivateKeyEncryptionKey = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        private readonly SingleKeyValueStorage _storage;

        public PublicPrivateKeyService(
            IcrKeyService icrKeyService,
            IOdinHttpClientFactory odinHttpClientFactory,
            ILogger<PublicPrivateKeyService> logger,
            TableKeyValueCached tblKeyValue)
        {
            _icrKeyService = icrKeyService;
            _odinHttpClientFactory = odinHttpClientFactory;
            _logger = logger;
            _tblKeyValue = tblKeyValue;

            const string keyCacheStorageContextKey = "a61dfbbb-1086-445f-8bfb-e8f3bd04a939";
            _storage = TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(keyCacheStorageContextKey));
        }

        /// <summary>
        /// Destroys the cache item for the recipients public key so a new one will be retrieved
        /// </summary>
        public async Task InvalidateRecipientEccPublicKeyAsync(PublicPrivateKeyType keyType, OdinId recipient)
        {
            GuidId cacheKey = GetEccCacheKey(keyType, recipient.DomainName);
            await _storage.DeleteAsync(_tblKeyValue, cacheKey);
        }

        private async Task<EccPublicKeyData> ResolveRecipientEccPublicKeyAsync(PublicPrivateKeyType keyType, OdinId recipient,
            bool failIfCannotRetrieve = true)
        {
            GuidId cacheKey = GetEccCacheKey(keyType, recipient.DomainName);

            await EccRecipientOnlinePublicKeyCacheLock.WaitAsync();
            try
            {
                var cacheItem = await _storage.GetAsync<EccPublicKeyData>(_tblKeyValue, cacheKey);
                if (cacheItem == null || cacheItem.IsExpired())
                {
                    var svc = await _odinHttpClientFactory.CreateClientAsync<IPeerEncryptionKeyServiceHttpClient>(recipient);
                    var getPkResponse = await svc.GetEccPublicKey(keyType);

                    if (getPkResponse.Content == null || !getPkResponse.IsSuccessStatusCode)
                    {
                        return null;
                    }

                    var content = getPkResponse.Content;
                    cacheItem = EccPublicKeyData.FromJwkPublicKey(content.PublicKeyJwkBase64Url);
                    cacheItem.expiration = new UnixTimeUtc(content.Expiration);
                    cacheItem.crc32c = content.CRC32c;

                    _logger.LogDebug("Updating ecc public key cache record for recipient: {r} with cacheKey: {k}", recipient, cacheKey);
                    _logger.LogDebug("Updated ecc public key: {k}", cacheItem);
                    await _storage.UpsertAsync(_tblKeyValue, cacheKey, cacheItem);
                }

                if (null == cacheItem && failIfCannotRetrieve)
                {
                    throw new MissingDataException("Could not get recipients key");
                }

                return cacheItem;
            }
            finally
            {
                EccRecipientOnlinePublicKeyCacheLock.Release();
            }
        }

        private GuidId GetEccCacheKey(PublicPrivateKeyType keyType, string domainName)
        {
            return GuidId.FromString($"ecc2_{Enum.GetName(keyType)}_{domainName}");
        }

        public async Task<EccEncryptedPayload> EccEncryptPayloadForRecipientAsync(PublicPrivateKeyType keyType, OdinId recipient,
            byte[] payload)
        {
            EccPublicKeyData recipientPublicKey = await ResolveRecipientEccPublicKeyAsync(keyType, recipient);

            if (null == recipientPublicKey)
            {
                _logger.LogDebug("Could not get public Ecc key (type: {kt}) for recipient: {recipient}", keyType, recipient);
                throw new OdinRemoteIdentityException("Could not get public Ecc key for recipient");
            }

            // Note: here we are throwing a way the full key intentionally
            SensitiveByteArray pwd = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
            EccFullKeyData senderEccFullKey = new EccFullKeyData(pwd, EccKeySize.P384, 2);

            var randomSalt = ByteArrayUtil.GetRndByteArray(16);
            var transferSharedSecret = senderEccFullKey.GetEcdhSharedSecret(pwd, recipientPublicKey, randomSalt);
            var iv = ByteArrayUtil.GetRndByteArray(16);

            return new EccEncryptedPayload
            {
                RemotePublicKeyJwk = senderEccFullKey.PublicKeyJwk(), //reminder, this must be the sender's public key
                Iv = iv,
                EncryptedData = AesGcm.Encrypt(payload, transferSharedSecret, iv),
                Salt = randomSalt,
                EncryptionPublicKeyCrc32 = recipientPublicKey.crc32c,
                KeyType = keyType
            };
        }

        public async Task<EccEncryptedPayload> EccEncryptPayload(PublicPrivateKeyType keyType, byte[] payload)
        {
            EccPublicKeyData recipientPublicEccKey = await this.GetPublicEccKeyAsync(keyType);

            //note: here we are throwing a way the full key intentionally
            SensitiveByteArray pwd = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
            EccFullKeyData senderFullKey = new EccFullKeyData(pwd, EccKeySize.P384, 2);

            var randomSalt = ByteArrayUtil.GetRndByteArray(16);
            var ss = senderFullKey.GetEcdhSharedSecret(pwd, recipientPublicEccKey, randomSalt);
            var iv = ByteArrayUtil.GetRndByteArray(16);

            return new EccEncryptedPayload
            {
                RemotePublicKeyJwk = senderFullKey.PublicKeyJwk(),
                Iv = iv,
                EncryptedData = AesGcm.Encrypt(payload, ss, iv),
                Salt = randomSalt,
                EncryptionPublicKeyCrc32 = recipientPublicEccKey.crc32c,
                KeyType = keyType,
            };
        }

        public async Task<byte[]> EccDecryptPayload(EccEncryptedPayload payload, IOdinContext odinContext)
        {
            // PublicPrivateKeyType keyType,
            var keyType = payload.KeyType;
            var remotePublicKey = EccPublicKeyData.FromJwkPublicKey(payload.RemotePublicKeyJwk);

            if (!await IsValidEccPublicKeyAsync(keyType, payload.EncryptionPublicKeyCrc32))
            {
                throw new OdinClientException("Encrypted Payload Public Key does not match");
            }

            var recipientFullEccKey = await GetEccFullKeyAsync(keyType);

            SensitiveByteArray key;
            switch (keyType)
            {
                case PublicPrivateKeyType.OfflineKey:
                    key = OfflinePrivateKeyEncryptionKey.ToSensitiveByteArray();
                    break;
                case PublicPrivateKeyType.OnlineKey:
                    key = odinContext.Caller.GetMasterKey();
                    break;
                case PublicPrivateKeyType.OnlineIcrEncryptedKey:
                    key = odinContext.PermissionsContext.GetIcrKey();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(keyType), keyType, null);
            }

            var transferSharedSecret = recipientFullEccKey.GetEcdhSharedSecret(key, remotePublicKey, payload.Salt);
            return AesGcm.Decrypt(payload.EncryptedData, transferSharedSecret, payload.Iv);
        }

        public async Task<bool> IsValidEccPublicKeyAsync(PublicPrivateKeyType keyType, uint publicKeyCrc32C)
        {
            var fullEccKey = await GetEccFullKeyAsync(keyType);
            return fullEccKey.crc32c == publicKeyCrc32C;
        }

        public async Task<EccPublicKeyData> GetSigningPublicKeyAsync()
        {
            var keyPair = await GetCurrentEccKeyFromStorageAsync(_signingKeyStorageId);
            return keyPair;
        }

        public async Task<EccPublicKeyData> GetOnlineEccPublicKeyAsync()
        {
            var keyPair = await GetCurrentEccKeyFromStorageAsync(_onlineEccKeyStorageId);
            return keyPair;
        }

        public async Task<EccPublicKeyData> GetOnlineIcrEncryptedEccPublicKeyAsync()
        {
            var fullKey = await GetCurrentEccKeyFromStorageAsync(_onlineIcrEncryptedEccKeyStorageId);
            var publicKey = (EccPublicKeyData)fullKey;
            return publicKey;
        }

        public async Task<EccFullKeyData> GetEccFullKeyAsync(PublicPrivateKeyType keyType)
        {
            switch (keyType)
            {
                case PublicPrivateKeyType.OfflineKey:
                    return await GetCurrentEccKeyFromStorageAsync(_offlineEccKeyStorageId);

                case PublicPrivateKeyType.OnlineKey:
                    return await GetCurrentEccKeyFromStorageAsync(_onlineEccKeyStorageId);

                case PublicPrivateKeyType.OnlineIcrEncryptedKey:
                    return await GetCurrentEccKeyFromStorageAsync(_onlineIcrEncryptedEccKeyStorageId);

                default:
                    throw new ArgumentOutOfRangeException(nameof(keyType), keyType, null);
            }
        }

        public async Task<EccPublicKeyData> GetOfflineEccPublicKeyAsync()
        {
            var keyPair = await GetCurrentEccKeyFromStorageAsync(_offlineEccKeyStorageId);
            return keyPair;
        }

        public async Task<string> GetNotificationsEccPublicKeyAsync()
        {
            return (await GetEccNotificationsKeysAsync()).PublicKey64;
        }

        public async Task<NotificationEccKeys> GetEccNotificationsKeysAsync()
        {
            var keys = await _storage.GetAsync<NotificationEccKeys>(_tblKeyValue, _offlineNotificationsKeyStorageId);
            return keys;
        }
        
        public async Task<EccPublicKeyData> GetPublicEccKeyAsync(PublicPrivateKeyType keyType)
        {
            switch (keyType)
            {
                case PublicPrivateKeyType.OfflineKey:
                    return await this.GetOfflineEccPublicKeyAsync();

                case PublicPrivateKeyType.OnlineKey:
                    return await this.GetOnlineEccPublicKeyAsync();

                case PublicPrivateKeyType.OnlineIcrEncryptedKey:
                    return await this.GetOnlineIcrEncryptedEccPublicKeyAsync();

                default:
                    throw new ArgumentOutOfRangeException(nameof(keyType), keyType, null);
            }
        }
        
        /// <summary>
        /// Gets the Ecc key from the storage based on the CRC
        /// </summary>
        public async Task<(EccFullKeyData EccKey, SensitiveByteArray DecryptionKey)> ResolveOnlineEccKeyForDecryptionAsync(uint crc32,
            IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            EccFullKeyListData keyList;
            SensitiveByteArray decryptionKey;

            keyList = await GetEccKeyListFromStorageAsync(_onlineEccKeyStorageId);
            decryptionKey = odinContext.Caller.GetMasterKey();

            var pk = EccKeyListManagement.FindKey(keyList, crc32);
            return (pk, decryptionKey);
        }

        /// <summary>
        /// Returns the latest Offline Ecc key
        /// </summary>
        public async Task<(EccFullKeyData fullKey, SensitiveByteArray privateKey)> GetCurrentOfflineEccKeyAsync()
        {
            var keyList = await this.GetEccKeyListFromStorageAsync(_offlineEccKeyStorageId);
            var pk = EccKeyListManagement.GetCurrentKey(keyList);
            return (pk, OfflinePrivateKeyEncryptionKey.ToSensitiveByteArray());
        }

        /// <summary>
        /// Creates the initial set of keys required by an identity
        /// </summary>
        internal async Task CreateInitialKeysAsync(IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            try
            {
                await KeyCreationLock.WaitAsync();
                var mk = odinContext.Caller.GetMasterKey();

                await this.CreateNewEccKeysAsync(mk, _signingKeyStorageId);
                await this.CreateNewEccKeysAsync(mk, _onlineEccKeyStorageId);
                await this.CreateNewEccKeysAsync(OfflinePrivateKeyEncryptionKey.ToSensitiveByteArray(), _offlineEccKeyStorageId);

                // Note: we use the service since OdinContext might not have the ICR key
                // yet as it was just created during identity-init
                var icrKey = await _icrKeyService.GetDecryptedIcrKeyAsync(odinContext);
                await this.CreateNewEccKeysAsync(icrKey, _onlineIcrEncryptedEccKeyStorageId);

                await this.CreateNotificationEccKeysAsync();
            }
            finally
            {
                KeyCreationLock.Release();
            }
        }

        private async Task CreateNotificationEccKeysAsync()
        {
            var storageKey = _offlineNotificationsKeyStorageId;


            var existingKey = await _storage.GetAsync<NotificationEccKeys>(_tblKeyValue, storageKey);

            if (null == existingKey)
            {
                VapidDetails vapidKeys = VapidHelper.GenerateVapidKeys();

                await _storage.UpsertAsync(_tblKeyValue, storageKey, new NotificationEccKeys
                {
                    PublicKey64 = vapidKeys.PublicKey,
                    PrivateKey64 = vapidKeys.PrivateKey
                });
            }
        }

        private async Task CreateNewEccKeysAsync(SensitiveByteArray encryptionKey, Guid storageKey)
        {
            var existingKeys = await _storage.GetAsync<EccFullKeyListData>(_tblKeyValue, storageKey);

            if (null != existingKeys)
            {
                // throw new OdinSecurityException($"Ecc keys with storage key {storageKey} already exist.");
                _logger.LogInformation("Attempt to create new ECC keys with storage key {storageKey}.  Already exist; ignoring request",
                    storageKey);
                return;
            }

            //create a new key list
            var eccKeyList = EccKeyListManagement.CreateEccKeyList(encryptionKey,
                EccKeyListManagement.DefaultMaxOnlineKeys,
                EccKeyListManagement.DefaultHoursOnlineKey);

            await _storage.UpsertAsync(_tblKeyValue, storageKey, eccKeyList);
        }

        private async Task<EccFullKeyData> GetCurrentEccKeyFromStorageAsync(Guid storageKey)
        {
            var keyList = await GetEccKeyListFromStorageAsync(storageKey);
            if (null == keyList)
            {
                return null;
            }

            return EccKeyListManagement.GetCurrentKey(keyList);
        }

        private async Task<EccFullKeyListData> GetEccKeyListFromStorageAsync(Guid storageKey)
        {
            return await _storage.GetAsync<EccFullKeyListData>(_tblKeyValue, storageKey);
        }
    }
}