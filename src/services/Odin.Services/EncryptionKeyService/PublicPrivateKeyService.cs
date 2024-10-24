using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Mediator.Owner;
using Odin.Services.Peer.Encryption;
using WebPush;

namespace Odin.Services.EncryptionKeyService
{
    public class NotificationEccKeys
    {
        public string PublicKey64 { get; set; }
        public string PrivateKey64 { get; set; }
    }

    public class PublicPrivateKeyService : INotificationHandler<OwnerIsOnlineNotification>
    {
        private readonly Guid _offlineKeyStorageId = Guid.Parse("AAAAAAAF-0f85-EEEE-E77E-e8e0b06c2777");
        private readonly Guid _onlineKeyStorageId = Guid.Parse("fc187615-deb4-4222-bcb5-35411bae25e3");
        private readonly Guid _signingKeyStorageId = Guid.Parse("d61a2789-2bc0-46c9-b6b9-19dcb3d076ab");
        private readonly Guid _onlineEccKeyStorageId = Guid.Parse("0d4cbb31-bd2e-4910-806c-42a516e63174");
        private readonly Guid _offlineEccKeyStorageId = Guid.Parse("09529956-bf97-43e8-9822-ad3ecf26819d");
        private readonly Guid _offlineNotificationsKeyStorageId = Guid.Parse("22165337-1ff5-4e92-87f2-95fc9ce424c3");

        private readonly IOdinHttpClientFactory _odinHttpClientFactory;
        private readonly ILogger<PublicPrivateKeyService> _logger;

        private static readonly SemaphoreSlim RsaRecipientOnlinePublicKeyCacheLock = new(1, 1);
        private static readonly SemaphoreSlim EccRecipientOnlinePublicKeyCacheLock = new(1, 1);
        private static readonly SemaphoreSlim KeyCreationLock = new(1, 1);
        public static readonly byte[] OfflinePrivateKeyEncryptionKey = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        public static readonly byte[] NotificationPrivateEncryptionKey =
            { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        private readonly SingleKeyValueStorage _storage;

        public PublicPrivateKeyService(TenantSystemStorage tenantSystemStorage, IOdinHttpClientFactory odinHttpClientFactory,
            ILogger<PublicPrivateKeyService> logger)
        {
            _odinHttpClientFactory = odinHttpClientFactory;
            _logger = logger;

            const string keyCacheStorageContextKey = "a61dfbbb-1086-445f-8bfb-e8f3bd04a939";
            _storage = tenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(keyCacheStorageContextKey));
        }

        /// <summary>
        /// Destroys the cache item for the recipients public key so a new one will be retrieved
        /// </summary>
        /// <param name="recipient"></param>
        /// <returns></returns>
        public async Task InvalidateRecipientRsaPublicKeyAsync(OdinId recipient, IdentityDatabase db)
        {
            await _storage.DeleteAsync(db, GuidId.FromString(recipient.DomainName));
        }

        /// <summary>
        /// Gets the latest effective offline public key
        /// </summary>
        public async Task<RsaPublicKeyData> GetOfflineRsaPublicKeyAsync(IdentityDatabase db)
        {
            var k = await GetCurrentRsaKeyFromStorageAsync(_offlineKeyStorageId, db);
            return RsaPublicKeyData.FromDerEncodedPublicKey(k.publicKey);
        }

        private async Task<EccPublicKeyData> ResolveRecipientEccPublicKeyAsync(IdentityDatabase db, PublicPrivateKeyType keyType, OdinId recipient, bool failIfCannotRetrieve = true)
        {
            string prefix = keyType == PublicPrivateKeyType.OfflineKey ? "aoffline" :
                keyType == PublicPrivateKeyType.OnlineKey ? "bonline" : throw new OdinSystemException("Unhandled key type");

            GuidId cacheKey = GuidId.FromString($"{prefix}{recipient.DomainName}");

            await EccRecipientOnlinePublicKeyCacheLock.WaitAsync();
            try
            {
                var cacheItem = await _storage.GetAsync<EccPublicKeyData>(db, cacheKey);
                if ((cacheItem == null || cacheItem.IsExpired()))
                {
                    var svc = _odinHttpClientFactory.CreateClient<IPeerEncryptionKeyServiceHttpClient>(recipient);
                    var getPkResponse = await svc.GetEccPublicKey(keyType);

                    if (getPkResponse.Content == null || !getPkResponse.IsSuccessStatusCode)
                    {
                        return null;
                    }

                    cacheItem = EccPublicKeyData.FromJwkPublicKey(getPkResponse.Content.PublicKey.ToStringFromUtf8Bytes());
                    cacheItem.expiration = getPkResponse.Content.Expiration; //use the actual expiration from the remote server

                    _logger.LogDebug("Updating ecc public key cache record for recipient: {r} with cacheKey: {k}", recipient, cacheKey);
                    await _storage.UpsertAsync(db, cacheKey, cacheItem);
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

        private async Task<RsaPublicKeyData> ResolveRecipientRsaKeyAsync(IdentityDatabase db, PublicPrivateKeyType keyType, OdinId recipient, bool failIfCannotRetrieve = true)
        {
            await RsaRecipientOnlinePublicKeyCacheLock.WaitAsync();
            try
            {
                string prefix = keyType == PublicPrivateKeyType.OfflineKey ? "offline" :
                    keyType == PublicPrivateKeyType.OnlineKey ? "online" : throw new OdinSystemException("Unhandled key type");

                GuidId cacheKey = GuidId.FromString($"{prefix}{recipient.DomainName}");

                var cacheItem = await _storage.GetAsync<RsaPublicKeyData>(db, cacheKey);
                if ((cacheItem == null || cacheItem.IsExpired()))
                {
                    var svc = _odinHttpClientFactory.CreateClient<IPeerEncryptionKeyServiceHttpClient>(recipient);
                    var tpkResponse = await svc.GetRsaPublicKey(keyType);

                    if (tpkResponse.Content == null || !tpkResponse.IsSuccessStatusCode)
                    {
                        // SEB:NOTE this can happen in dev environments where a production peer does 
                        // not accept certificates from letsencrypt staging CA. 
                        var errorMessage = tpkResponse.Error?.Message ?? "unknown error";
                        throw new OdinSystemException($"ResolveRecipientRsaKey failed for {recipient}: {errorMessage}");
                    }

                    cacheItem = new RsaPublicKeyData()
                    {
                        publicKey = tpkResponse.Content.PublicKey,
                        crc32c = tpkResponse.Content.Crc32,
                        expiration = new UnixTimeUtc(tpkResponse.Content.Expiration)
                    };

                    await _storage.UpsertAsync(db, cacheKey, cacheItem);
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

        public async Task<RsaEncryptedPayload> RsaEncryptPayloadAsync(PublicPrivateKeyType keyType, byte[] payload, IdentityDatabase db)
        {
            RsaPublicKeyData pk;

            switch (keyType)
            {
                case PublicPrivateKeyType.OfflineKey:
                    pk = await GetOfflineRsaPublicKeyAsync(db);
                    break;

                case PublicPrivateKeyType.OnlineKey:
                    pk = await GetOnlineRsaPublicKeyAsync(db);
                    break;

                default:
                    throw new OdinSystemException("Unhandled RsaKeyType");
            }

            return Encrypt(pk, payload);
        }

        public async Task<RsaEncryptedPayload> RsaEncryptPayloadForRecipientAsync(PublicPrivateKeyType keyType, OdinId recipient, byte[] payload, IdentityDatabase db)
        {
            RsaPublicKeyData pk = await ResolveRecipientRsaKeyAsync(db, keyType, recipient);
            return Encrypt(pk, payload);
        }


        public async Task<EccEncryptedPayload> EccEncryptPayloadForRecipientAsync(PublicPrivateKeyType keyType, OdinId recipient, byte[] payload, IdentityDatabase db)
        {
            EccPublicKeyData recipientPublicKey = await ResolveRecipientEccPublicKeyAsync(db, keyType, recipient);

            if (null == recipientPublicKey)
            {
                _logger.LogDebug("Could not get public Ecc key for recipient: {recipient}", recipient);
                throw new OdinSystemException("Could not get public Ecc key for recipient");
            }

            //note: here we are throwing a way the full key intentionally
            SensitiveByteArray pwd = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
            EccFullKeyData senderEccFullKey = new EccFullKeyData(pwd, EccKeySize.P384, 2);

            var randomSalt = ByteArrayUtil.GetRndByteArray(16);
            var transferSharedSecret = senderEccFullKey.GetEcdhSharedSecret(pwd, recipientPublicKey, randomSalt);
            var iv = ByteArrayUtil.GetRndByteArray(16);

            return new EccEncryptedPayload
            {
                PublicKey = senderEccFullKey.PublicKeyJwk(),
                Iv = iv,
                EncryptedData = AesCbc.Encrypt(payload, transferSharedSecret, iv),
                Salt = randomSalt,
            };
        }

        public async Task<byte[]> EccDecryptPayloadAsync(EccEncryptedPayload payload, IdentityDatabase db)
        {
            var fullEccKey = await this.GetEccFullKeyAsync(PublicPrivateKeyType.OfflineKey, db);

            _logger.LogDebug("local recipient key [{local}]",  fullEccKey.PublicKeyJwk());

            var senderPublicKey = EccPublicKeyData.FromJwkPublicKey(payload.PublicKey);
            var transferSharedSecret = fullEccKey.GetEcdhSharedSecret(OfflinePrivateKeyEncryptionKey.ToSensitiveByteArray(), senderPublicKey, payload.Salt);
            return AesCbc.Decrypt(payload.EncryptedData, transferSharedSecret, payload.Iv);
        }

        public Task Handle(OwnerIsOnlineNotification notification, CancellationToken cancellationToken)
        {
            //TODO: add logic to ensure we only call this periodically 
            // await this.CreateOrRotateOnlineKeys();
            return Task.CompletedTask;
        }

        public async Task<EccPublicKeyData> GetSigningPublicKeyAsync(IdentityDatabase db)
        {
            var keyPair = await GetCurrentEccKeyFromStorageAsync(_signingKeyStorageId, db);
            return (EccPublicKeyData)keyPair;
        }

        public async Task<EccPublicKeyData> GetOnlineEccPublicKeyAsync(IdentityDatabase db)
        {
            var keyPair = await GetCurrentEccKeyFromStorageAsync(_onlineEccKeyStorageId, db);
            return keyPair;
        }

        public async Task<EccFullKeyData> GetEccFullKeyAsync(PublicPrivateKeyType keyType, IdentityDatabase db)
        {
            switch (keyType)
            {
                case PublicPrivateKeyType.OfflineKey:
                    return await GetCurrentEccKeyFromStorageAsync(_offlineEccKeyStorageId, db);

                case PublicPrivateKeyType.OnlineKey:
                    return await GetCurrentEccKeyFromStorageAsync(_onlineEccKeyStorageId, db);

                default:
                    throw new ArgumentOutOfRangeException(nameof(keyType), keyType, null);
            }
        }

        public async Task<EccPublicKeyData> GetOfflineEccPublicKeyAsync(IdentityDatabase db)
        {
            var keyPair = await GetCurrentEccKeyFromStorageAsync(_offlineEccKeyStorageId, db);
            return keyPair;
        }

        public async Task<string> GetNotificationsEccPublicKeyAsync(IdentityDatabase db)
        {
            var key = await GetEccNotificationsKeysAsync(db); 
            return key.PublicKey64;
        }

        public async Task<NotificationEccKeys> GetEccNotificationsKeysAsync(IdentityDatabase db)
        {
            var keys = await _storage.GetAsync<NotificationEccKeys>(db, _offlineNotificationsKeyStorageId);
            return keys;
        }

        public async Task<RsaPublicKeyData> GetOnlineRsaPublicKeyAsync(IdentityDatabase db)
        {
            var keyPair = await GetCurrentRsaKeyFromStorageAsync(_onlineKeyStorageId, db);
            return RsaPublicKeyData.FromDerEncodedPublicKey(keyPair.publicKey);
        }

        /// <summary>
        /// Upgrades the key to the latest
        /// </summary>
        public async Task<RsaEncryptedPayload> UpgradeRsaKeyAsync(PublicPrivateKeyType keyType, RsaFullKeyData currentKey, SensitiveByteArray currentDecryptionKey,
            RsaEncryptedPayload payload, IdentityDatabase db)
        {
            //unwrap the rsa encrypted key header
            var keyHeader = DecryptKeyHeaderInternal(currentKey, currentDecryptionKey, payload.RsaEncryptedKeyHeader);

            var pk = await GetPublicRsaKeyAsync(keyType, db);

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

        public async Task<(bool IsValidPublicKey, byte[] DecryptedBytes)> RsaDecryptPayloadAsync(PublicPrivateKeyType keyType, RsaEncryptedPayload payload,
            IOdinContext odinContext, IdentityDatabase db)
        {
            var (isValidPublicKey, keyHeader) = await this.RsaDecryptKeyHeaderAsync(keyType, payload.RsaEncryptedKeyHeader, payload.Crc32, odinContext, db);

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

        public async Task<EccPublicKeyData> GetPublicEccKeyAsync(PublicPrivateKeyType keyType, IdentityDatabase db)
        {
            switch (keyType)
            {
                case PublicPrivateKeyType.OfflineKey:
                    return await this.GetOfflineEccPublicKeyAsync(db);

                case PublicPrivateKeyType.OnlineKey:
                    return await this.GetOnlineEccPublicKeyAsync(db);

                default:
                    throw new ArgumentOutOfRangeException(nameof(keyType), keyType, null);
            }
        }


        public async Task<RsaPublicKeyData> GetPublicRsaKeyAsync(PublicPrivateKeyType keyType, IdentityDatabase db)
        {
            switch (keyType)
            {
                case PublicPrivateKeyType.OfflineKey:
                    return await this.GetOfflineRsaPublicKeyAsync(db);
                case PublicPrivateKeyType.OnlineKey:
                    return await this.GetOnlineRsaPublicKeyAsync(db);
                default:
                    throw new ArgumentOutOfRangeException(nameof(keyType), keyType, null);
            }
        }


        /// <summary>
        /// Gets the Ecc key from the storage based on the CRC
        /// </summary>
        public async Task<(EccFullKeyData EccKey, SensitiveByteArray DecryptionKey)> ResolveOnlineEccKeyForDecryptionAsync(uint crc32, IOdinContext odinContext, IdentityDatabase db)
        {
            odinContext.Caller.AssertHasMasterKey();

            var keyList = await GetEccKeyListFromStorageAsync(_onlineEccKeyStorageId, db);
            var decryptionKey = odinContext.Caller.GetMasterKey();

            var pk = EccKeyListManagement.FindKey(keyList, crc32);
            return (pk, decryptionKey);
        }

        /// <summary>
        /// Returns the latest Offline Ecc key
        /// </summary>
        public async Task<(EccFullKeyData fullKey, SensitiveByteArray privateKey)> GetCurrentOfflineEccKeyAsync(IdentityDatabase db)
        {
            var keyList = await GetEccKeyListFromStorageAsync(_offlineEccKeyStorageId, db);
            var pk = EccKeyListManagement.GetCurrentKey(keyList);
            return (pk, OfflinePrivateKeyEncryptionKey.ToSensitiveByteArray());
        }


        /// <summary>
        /// Creates the initial set of RSA keys required by an identity
        /// </summary>
        internal async Task CreateInitialKeysAsync(IOdinContext odinContext, IdentityDatabase db)
        {
            odinContext.Caller.AssertHasMasterKey();

            try
            {
                await KeyCreationLock.WaitAsync();
                var mk = odinContext.Caller.GetMasterKey();

                await this.CreateNewRsaKeysAsync(mk, _onlineKeyStorageId, db);

                await this.CreateNewEccKeysAsync(mk, _signingKeyStorageId, db);
                await this.CreateNewEccKeysAsync(mk, _onlineEccKeyStorageId, db);
                await this.CreateNewEccKeysAsync(OfflinePrivateKeyEncryptionKey.ToSensitiveByteArray(), _offlineEccKeyStorageId, db);

                await this.CreateNewRsaKeysAsync(OfflinePrivateKeyEncryptionKey.ToSensitiveByteArray(), _offlineKeyStorageId, db);

                await this.CreateNotificationEccKeysAsync(db);
            }
            finally
            {
                KeyCreationLock.Release();
            }
        }

        private async Task<(bool, KeyHeader)> RsaDecryptKeyHeaderAsync(PublicPrivateKeyType keyType, byte[] encryptedData, uint publicKeyCrc32,
            IOdinContext odinContext, IdentityDatabase db,
            bool failIfNoMatchingPublicKey = true)
        {
            var (rsaKey, decryptionKey) = await ResolveRsaKeyForDecryptionAsync(keyType, publicKeyCrc32, odinContext, db);

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

        private KeyHeader DecryptKeyHeaderInternal(RsaFullKeyData rsaKey, SensitiveByteArray decryptionKey, byte[] encryptedData)
        {
            var bytes = rsaKey.Decrypt(decryptionKey, encryptedData);
            return KeyHeader.FromCombinedBytes(bytes);
        }

        private async Task<(RsaFullKeyData RsaKey, SensitiveByteArray DecryptionKey)> ResolveRsaKeyForDecryptionAsync(PublicPrivateKeyType keyType, uint crc32,
            IOdinContext odinContext, IdentityDatabase db)
        {
            RsaFullKeyListData keyList;
            SensitiveByteArray decryptionKey;

            switch (keyType)
            {
                case PublicPrivateKeyType.OfflineKey:
                    keyList = await GetRsaKeyListFromStorageAsync(_offlineKeyStorageId, db);
                    decryptionKey = OfflinePrivateKeyEncryptionKey.ToSensitiveByteArray();
                    break;

                case PublicPrivateKeyType.OnlineKey:
                    keyList = await GetRsaKeyListFromStorageAsync(_onlineKeyStorageId, db);
                    decryptionKey = odinContext.Caller.GetMasterKey();
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(keyType), keyType, null);
            }

            var pk = RsaKeyListManagement.FindKey(keyList, crc32);
            return (pk, decryptionKey);
        }

        private async Task CreateNewRsaKeysAsync(SensitiveByteArray encryptionKey, Guid storageKey, IdentityDatabase db)
        {
            var existingKeys = await _storage.GetAsync<RsaFullKeyListData>(db, storageKey);
            if (null != existingKeys)
            {
                throw new OdinSecurityException($"Rsa keys with storage key {storageKey} already exist.");
            }

            //create a new key list
            var rsaKeyList = RsaKeyListManagement.CreateRsaKeyList(encryptionKey,
                RsaKeyListManagement.DefaultMaxOnlineKeys,
                RsaKeyListManagement.DefaultHoursOnlineKey);

            await _storage.UpsertAsync(db, storageKey, rsaKeyList);
        }

        private async Task CreateNotificationEccKeysAsync(IdentityDatabase db)
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

            VapidDetails vapidKeys = VapidHelper.GenerateVapidKeys();
            await _storage.UpsertAsync(db, storageKey, new NotificationEccKeys
            {
                PublicKey64 = vapidKeys.PublicKey,
                PrivateKey64 = vapidKeys.PrivateKey
            });
        }

        private async Task CreateNewEccKeysAsync(SensitiveByteArray encryptionKey, Guid storageKey, IdentityDatabase db)
        {
            var existingKeys = await _storage.GetAsync<EccFullKeyListData>(db, storageKey);

            if (null != existingKeys)
            {
                throw new OdinSecurityException($"Ecc keys with storage key {storageKey} already exist.");
            }

            //create a new key list
            var eccKeyList = EccKeyListManagement.CreateEccKeyList(encryptionKey,
                EccKeyListManagement.DefaultMaxOnlineKeys,
                EccKeyListManagement.DefaultHoursOnlineKey);

            await _storage.UpsertAsync(db, storageKey, eccKeyList);
        }

        private async Task<RsaFullKeyData> GetCurrentRsaKeyFromStorageAsync(Guid storageKey, IdentityDatabase db)
        {
            var keyList = await GetRsaKeyListFromStorageAsync(storageKey, db);
            return RsaKeyListManagement.GetCurrentKey(keyList);
        }

        private async Task<RsaFullKeyListData> GetRsaKeyListFromStorageAsync(Guid storageKey, IdentityDatabase db)
        {
            return await _storage.GetAsync<RsaFullKeyListData>(db, storageKey);
        }

        private async Task<EccFullKeyData> GetCurrentEccKeyFromStorageAsync(Guid storageKey, IdentityDatabase db)
        {
            var keyList = await GetEccKeyListFromStorageAsync(storageKey, db);
            if (null == keyList)
            {
                return null;
            }

            return EccKeyListManagement.GetCurrentKey(keyList);
        }

        private async Task<EccFullKeyListData> GetEccKeyListFromStorageAsync(Guid storageKey, IdentityDatabase db)
        {
            return await _storage.GetAsync<EccFullKeyListData>(db, storageKey);
        }

        private RsaEncryptedPayload Encrypt(RsaPublicKeyData pk, byte[] payload)
        {
            var keyHeader = KeyHeader.NewRandom16();
            return new RsaEncryptedPayload()
            {
                //Note: i exclude the key type here because the methods that receive
                //this must decide the encryption they expect
                Crc32 = pk.crc32c,
                RsaEncryptedKeyHeader = pk.Encrypt(keyHeader.Combine().GetKey()),
                KeyHeaderEncryptedData = keyHeader.EncryptDataAesAsStream(payload).ToByteArray()
            };
        }
    }
}