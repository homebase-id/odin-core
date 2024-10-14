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
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Encryption;
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
        private readonly Guid _offlineKeyStorageId = Guid.Parse("AAAAAAAF-0f85-EEEE-E77E-e8e0b06c2777");
        private readonly Guid _onlineKeyStorageId = Guid.Parse("fc187615-deb4-4222-bcb5-35411bae25e3");
        private readonly Guid _signingKeyStorageId = Guid.Parse("d61a2789-2bc0-46c9-b6b9-19dcb3d076ab");
        private readonly Guid _onlineEccKeyStorageId = Guid.Parse("0d4cbb31-bd2e-4910-806c-42a516e63174");

        private readonly Guid _onlineIcrEncryptedEccKeyStorageId = Guid.Parse("f1b4601c-6c50-4443-9113-624b55a8c636");

        private readonly Guid _offlineEccKeyStorageId = Guid.Parse("09529956-bf97-43e8-9822-ad3ecf26819d");
        private readonly Guid _offlineNotificationsKeyStorageId = Guid.Parse("22165337-1ff5-4e92-87f2-95fc9ce424c3");

        private readonly IcrKeyService _icrKeyService;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory;
        private readonly ILogger<PublicPrivateKeyService> _logger;
        private readonly TenantSystemStorage _tenantSystemStorage;

        private static readonly SemaphoreSlim EccRecipientOnlinePublicKeyCacheLock = new(1, 1);
        private static readonly SemaphoreSlim KeyCreationLock = new(1, 1);
        public static readonly byte[] OfflinePrivateKeyEncryptionKey = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        private readonly SingleKeyValueStorage _storage;

        public PublicPrivateKeyService(TenantSystemStorage tenantSystemStorage, IcrKeyService icrKeyService, IOdinHttpClientFactory odinHttpClientFactory,
            ILogger<PublicPrivateKeyService> logger)
        {
            _tenantSystemStorage = tenantSystemStorage;
            _icrKeyService = icrKeyService;
            _odinHttpClientFactory = odinHttpClientFactory;
            _logger = logger;

            const string keyCacheStorageContextKey = "a61dfbbb-1086-445f-8bfb-e8f3bd04a939";
            _storage = tenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(keyCacheStorageContextKey));
        }

        /// <summary>
        /// Destroys the cache item for the recipients public key so a new one will be retrieved
        /// </summary>
        public async Task InvalidateRecipientEccPublicKey(PublicPrivateKeyType keyType, OdinId recipient)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            GuidId cacheKey = GetEccCacheKey(keyType, recipient.DomainName);
            _storage.Delete(db, cacheKey);
            await Task.CompletedTask;
        }

        /// <summary>
        /// Gets the latest effective offline public key
        /// </summary>
        public Task<RsaPublicKeyData> GetOfflineRsaPublicKey()
        {
            var k = this.GetCurrentRsaKeyFromStorage(_offlineKeyStorageId);
            return Task.FromResult(RsaPublicKeyData.FromDerEncodedPublicKey(k.publicKey));
        }

        private async Task<EccPublicKeyData> ResolveRecipientEccPublicKey(IdentityDatabase db, PublicPrivateKeyType keyType, OdinId recipient,
            bool failIfCannotRetrieve = true)
        {
            GuidId cacheKey = GetEccCacheKey(keyType, recipient.DomainName);

            await EccRecipientOnlinePublicKeyCacheLock.WaitAsync();
            try
            {
                var cacheItem = _storage.Get<EccPublicKeyData>(db, cacheKey);
                if (cacheItem == null || cacheItem.IsExpired())
                {
                    var svc = _odinHttpClientFactory.CreateClient<IPeerEncryptionKeyServiceHttpClient>(recipient);
                    var getPkResponse = await svc.GetEccPublicKey(keyType);

                    if (getPkResponse.Content == null || !getPkResponse.IsSuccessStatusCode)
                    {
                        return null;
                    }

                    var content = getPkResponse.Content;
                    cacheItem = EccPublicKeyData.FromJwkPublicKey(content.PublicKeyJwk);
                    cacheItem.expiration = new UnixTimeUtc(content.Expiration);
                    cacheItem.crc32c = content.CRC32c;
                    
                    _logger.LogDebug("Updating ecc public key cache record for recipient: {r} with cacheKey: {k}", recipient, cacheKey);
                    _logger.LogDebug("Updated ecc public key: {k}", cacheItem);
                    _storage.Upsert(db, cacheKey, cacheItem);
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

        public async Task<EccEncryptedPayload> EccEncryptPayloadForRecipient(PublicPrivateKeyType keyType, OdinId recipient, byte[] payload)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            EccPublicKeyData recipientPublicKey = await ResolveRecipientEccPublicKey(db, keyType, recipient);

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
                PublicKey = senderEccFullKey.PublicKeyJwk(),  //reminder, this must be the sender's public key
                Iv = iv,
                EncryptedData = AesCbc.Encrypt(payload, transferSharedSecret, iv),
                Salt = randomSalt,
                EncryptionPublicKeyCrc32 = recipientPublicKey.crc32c
            };
        }

        public async Task<EccEncryptedPayload> EccEncryptPayload(PublicPrivateKeyType keyType, byte[] payload)
        {
            EccPublicKeyData publicEccKey = await this.GetPublicEccKey(keyType);

            //note: here we are throwing a way the full key intentionally
            SensitiveByteArray pwd = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
            EccFullKeyData fullKey = new EccFullKeyData(pwd, EccKeySize.P384, 2);

            var randomSalt = ByteArrayUtil.GetRndByteArray(16);
            var ss = fullKey.GetEcdhSharedSecret(pwd, publicEccKey, randomSalt);
            var iv = ByteArrayUtil.GetRndByteArray(16);

            return new EccEncryptedPayload
            {
                PublicKey = fullKey.PublicKeyJwk(),
                Iv = iv,
                EncryptedData = AesCbc.Encrypt(payload, ss, iv),
                Salt = randomSalt,
                EncryptionPublicKeyCrc32 = publicEccKey.crc32c
            };
        }

        public async Task<byte[]> EccDecryptPayload(PublicPrivateKeyType keyType, EccEncryptedPayload payload, IOdinContext odinContext)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            var publicKey = EccPublicKeyData.FromJwkPublicKey(payload.PublicKey);

            if (!await IsValidEccPublicKey(keyType, payload.EncryptionPublicKeyCrc32, odinContext))
            {
                throw new OdinClientException("Encrypted Payload Public Key does not match");
            }

            var fullEccKey = await GetEccFullKey(keyType);

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

            var transferSharedSecret = fullEccKey.GetEcdhSharedSecret(key, publicKey, payload.Salt);
            return AesCbc.Decrypt(payload.EncryptedData, transferSharedSecret, payload.Iv);
        }

        public async Task<bool> IsValidEccPublicKey(PublicPrivateKeyType keyType, uint publicKeyCrc32C, IOdinContext odinContext)
        {
            var fullEccKey = await this.GetEccFullKey(keyType);
            return fullEccKey.crc32c == publicKeyCrc32C;
        }

        public Task<EccPublicKeyData> GetSigningPublicKey()
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            var keyPair = this.GetCurrentEccKeyFromStorage(_signingKeyStorageId);
            return Task.FromResult((EccPublicKeyData)keyPair);
        }

        public Task<EccPublicKeyData> GetOnlineEccPublicKey()
        {
            var keyPair = this.GetCurrentEccKeyFromStorage(_onlineEccKeyStorageId);
            return Task.FromResult((EccPublicKeyData)keyPair);
        }

        public Task<EccPublicKeyData> GetOnlineIcrEncryptedEccPublicKey()
        {
            var fullKey = this.GetCurrentEccKeyFromStorage(_onlineIcrEncryptedEccKeyStorageId);
            var publicKey = (EccPublicKeyData)fullKey;
            return Task.FromResult(publicKey);
        }

        public Task<EccFullKeyData> GetEccFullKey(PublicPrivateKeyType keyType)
        {
            switch (keyType)
            {
                case PublicPrivateKeyType.OfflineKey:
                    return Task.FromResult(GetCurrentEccKeyFromStorage(_offlineEccKeyStorageId));

                case PublicPrivateKeyType.OnlineKey:
                    return Task.FromResult(GetCurrentEccKeyFromStorage(_onlineEccKeyStorageId));

                case PublicPrivateKeyType.OnlineIcrEncryptedKey:
                    return Task.FromResult(GetCurrentEccKeyFromStorage(_onlineIcrEncryptedEccKeyStorageId));

                default:
                    throw new ArgumentOutOfRangeException(nameof(keyType), keyType, null);
            }
        }

        public Task<EccPublicKeyData> GetOfflineEccPublicKey()
        {
            var keyPair = this.GetCurrentEccKeyFromStorage(_offlineEccKeyStorageId);
            return Task.FromResult((EccPublicKeyData)keyPair);
        }

        public Task<string> GetNotificationsEccPublicKey()
        {
            return Task.FromResult(this.GetEccNotificationsKeys().PublicKey64);
        }

        public NotificationEccKeys GetEccNotificationsKeys()
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            var keys = _storage.Get<NotificationEccKeys>(db, _offlineNotificationsKeyStorageId);
            return keys;
        }

        public Task<RsaPublicKeyData> GetOnlineRsaPublicKey()
        {
            var keyPair = this.GetCurrentRsaKeyFromStorage(_onlineKeyStorageId);
            return Task.FromResult(RsaPublicKeyData.FromDerEncodedPublicKey(keyPair.publicKey));
        }

        public async Task<(bool IsValidPublicKey, byte[] DecryptedBytes)> RsaDecryptPayload(PublicPrivateKeyType keyType, RsaEncryptedPayload payload,
            IOdinContext odinContext)
        {
            var (isValidPublicKey, keyHeader) = await this.RsaDecryptKeyHeader(keyType, payload.RsaEncryptedKeyHeader, payload.Crc32, odinContext);

            if (!isValidPublicKey)
            {
                return (false, null);
            }

            var key = keyHeader.AesKey;
            var bytes = AesCbc.Decrypt(
                cipherText: payload.KeyHeaderEncryptedData,
                key: key,
                iv: keyHeader.Iv);

            return (true, bytes);
        }

        public async Task<EccPublicKeyData> GetPublicEccKey(PublicPrivateKeyType keyType)
        {
            switch (keyType)
            {
                case PublicPrivateKeyType.OfflineKey:
                    return await this.GetOfflineEccPublicKey();

                case PublicPrivateKeyType.OnlineKey:
                    return await this.GetOnlineEccPublicKey();

                case PublicPrivateKeyType.OnlineIcrEncryptedKey:
                    return await this.GetOnlineIcrEncryptedEccPublicKey();

                default:
                    throw new ArgumentOutOfRangeException(nameof(keyType), keyType, null);
            }
        }


        public async Task<RsaPublicKeyData> GetPublicRsaKey(PublicPrivateKeyType keyType)
        {
            switch (keyType)
            {
                case PublicPrivateKeyType.OfflineKey:
                    return await this.GetOfflineRsaPublicKey();
                case PublicPrivateKeyType.OnlineKey:
                    return await this.GetOnlineRsaPublicKey();
                default:
                    throw new ArgumentOutOfRangeException(nameof(keyType), keyType, null);
            }
        }


        /// <summary>
        /// Gets the Ecc key from the storage based on the CRC
        /// </summary>
        public Task<(EccFullKeyData EccKey, SensitiveByteArray DecryptionKey)> ResolveOnlineEccKeyForDecryption(uint crc32, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            EccFullKeyListData keyList;
            SensitiveByteArray decryptionKey;

            keyList = this.GetEccKeyListFromStorage(_onlineEccKeyStorageId);
            decryptionKey = odinContext.Caller.GetMasterKey();

            var pk = EccKeyListManagement.FindKey(keyList, crc32);
            return Task.FromResult((pk, decryptionKey));
        }

        /// <summary>
        /// Returns the latest Offline Ecc key
        /// </summary>
        public Task<(EccFullKeyData fullKey, SensitiveByteArray privateKey)> GetCurrentOfflineEccKey()
        {
            var keyList = this.GetEccKeyListFromStorage(_offlineEccKeyStorageId);
            var pk = EccKeyListManagement.GetCurrentKey(keyList);
            return Task.FromResult((pk, OfflinePrivateKeyEncryptionKey.ToSensitiveByteArray()));
        }

        /// <summary>
        /// Creates the initial set of RSA keys required by an identity
        /// </summary>
        internal async Task CreateInitialKeys(IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            try
            {
                await KeyCreationLock.WaitAsync();
                var mk = odinContext.Caller.GetMasterKey();

                await this.CreateNewRsaKeys(mk, _onlineKeyStorageId);

                await this.CreateNewEccKeys(mk, _signingKeyStorageId);
                await this.CreateNewEccKeys(mk, _onlineEccKeyStorageId);
                await this.CreateNewEccKeys(OfflinePrivateKeyEncryptionKey.ToSensitiveByteArray(), _offlineEccKeyStorageId);

                // Note: we use the service since OdinContext might not have the ICR key
                // yet as it was just created during identity-init
                var icrKey = _icrKeyService.GetDecryptedIcrKey(odinContext);
                await this.CreateNewEccKeys(icrKey, _onlineIcrEncryptedEccKeyStorageId);

                await this.CreateNewRsaKeys(OfflinePrivateKeyEncryptionKey.ToSensitiveByteArray(), _offlineKeyStorageId);

                await this.CreateNotificationEccKeys();
            }
            finally
            {
                KeyCreationLock.Release();
            }
        }

        private async Task<(bool, KeyHeader)> RsaDecryptKeyHeader(PublicPrivateKeyType keyType, byte[] encryptedData, uint publicKeyCrc32,
            IOdinContext odinContext,
            bool failIfNoMatchingPublicKey = true)
        {
            var (rsaKey, decryptionKey) = await ResolveRsaKeyForDecryption(keyType, publicKeyCrc32, odinContext);

            if (null == rsaKey)
            {
                if (failIfNoMatchingPublicKey)
                {
                    throw new OdinSecurityException("Invalid public key");
                }

                return (false, null);
            }

            var bytes = rsaKey.Decrypt(decryptionKey, encryptedData);
            var keyHeader = KeyHeader.FromCombinedBytes(bytes);

            return (true, keyHeader);
        }

        private Task<(RsaFullKeyData RsaKey, SensitiveByteArray DecryptionKey)> ResolveRsaKeyForDecryption(PublicPrivateKeyType keyType, uint crc32,
            IOdinContext odinContext)
        {
            RsaFullKeyListData keyList;
            SensitiveByteArray decryptionKey;

            switch (keyType)
            {
                case PublicPrivateKeyType.OfflineKey:
                    keyList = this.GetRsaKeyListFromStorage(_offlineKeyStorageId);
                    decryptionKey = OfflinePrivateKeyEncryptionKey.ToSensitiveByteArray();
                    break;

                case PublicPrivateKeyType.OnlineKey:
                    keyList = this.GetRsaKeyListFromStorage(_onlineKeyStorageId);
                    decryptionKey = odinContext.Caller.GetMasterKey();
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(keyType), keyType, null);
            }

            var pk = RsaKeyListManagement.FindKey(keyList, crc32);
            return Task.FromResult((pk, decryptionKey));
        }

        private Task CreateNewRsaKeys(SensitiveByteArray encryptionKey, Guid storageKey)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            var existingKeys = _storage.Get<RsaFullKeyListData>(db, storageKey);
            if (null != existingKeys)
            {
                _logger.LogInformation("Attempt to create new RSA keys with storage key {storageKey}.  Already exist; ignoring request", storageKey);
                return Task.CompletedTask;
            }

            //create a new key list
            var rsaKeyList = RsaKeyListManagement.CreateRsaKeyList(encryptionKey,
                RsaKeyListManagement.DefaultMaxOnlineKeys,
                RsaKeyListManagement.DefaultHoursOnlineKey);

            _storage.Upsert(db, storageKey, rsaKeyList);

            return Task.CompletedTask;
        }

        private Task CreateNotificationEccKeys()
        {
            var storageKey = _offlineNotificationsKeyStorageId;
            // var existingKeys = _storage.Get<EccFullKeyListData>(storageKey);
            //
            // if (null != existingKeys)
            // {
            //     throw new OdinSecurityException($"Ecc keys with storage key {storageKey} already exist.");
            // }
            //
            // //create a new key list
            // var eccKeyList = EccKeyListManagement.CreateEccKeyList(NotificationPrivateEncryptionKey.ToSensitiveByteArray(),
            //     EccKeyListManagement.DefaultMaxOnlineKeys,
            //     EccKeyListManagement.DefaultHoursOnlineKey, EccFullKeyData.EccKeySize.P256);

            // _storage.Upsert(storageKey, eccKeyList);
            var db = _tenantSystemStorage.IdentityDatabase;
            VapidDetails vapidKeys = VapidHelper.GenerateVapidKeys();
            _storage.Upsert(db, storageKey, new NotificationEccKeys
            {
                PublicKey64 = vapidKeys.PublicKey,
                PrivateKey64 = vapidKeys.PrivateKey
            });

            return Task.CompletedTask;
        }

        private Task CreateNewEccKeys(SensitiveByteArray encryptionKey, Guid storageKey)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            var existingKeys = _storage.Get<EccFullKeyListData>(db, storageKey);

            if (null != existingKeys)
            {
                // throw new OdinSecurityException($"Ecc keys with storage key {storageKey} already exist.");
                _logger.LogInformation("Attempt to create new ECC keys with storage key {storageKey}.  Already exist; ignoring request", storageKey);
                return Task.CompletedTask;
            }

            //create a new key list
            var eccKeyList = EccKeyListManagement.CreateEccKeyList(encryptionKey,
                EccKeyListManagement.DefaultMaxOnlineKeys,
                EccKeyListManagement.DefaultHoursOnlineKey);

            _storage.Upsert(db, storageKey, eccKeyList);

            return Task.CompletedTask;
        }

        private RsaFullKeyData GetCurrentRsaKeyFromStorage(Guid storageKey)
        {
            var keyList = GetRsaKeyListFromStorage(storageKey);
            return RsaKeyListManagement.GetCurrentKey(keyList);
        }

        private RsaFullKeyListData GetRsaKeyListFromStorage(Guid storageKey)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            return _storage.Get<RsaFullKeyListData>(db, storageKey);
        }

        private EccFullKeyData GetCurrentEccKeyFromStorage(Guid storageKey)
        {
            var keyList = GetEccKeyListFromStorage(storageKey);
            if (null == keyList)
            {
                return null;
            }

            return EccKeyListManagement.GetCurrentKey(keyList);
        }

        private EccFullKeyListData GetEccKeyListFromStorage(Guid storageKey)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            return _storage.Get<EccFullKeyListData>(db, storageKey);
        }
    }
}