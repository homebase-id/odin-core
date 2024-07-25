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
        public async Task InvalidateRecipientRsaPublicKey(OdinId recipient, DatabaseConnection cn)
        {
            _storage.Delete(cn, GuidId.FromString(recipient.DomainName));
            await Task.CompletedTask;
        }

        /// <summary>
        /// Gets the latest effective offline public key
        /// </summary>
        public Task<RsaPublicKeyData> GetOfflineRsaPublicKey(DatabaseConnection cn)
        {
            var k = this.GetCurrentRsaKeyFromStorage(_offlineKeyStorageId, cn);
            return Task.FromResult(RsaPublicKeyData.FromDerEncodedPublicKey(k.publicKey));
        }

        private async Task<EccPublicKeyData> ResolveRecipientEccPublicKey(DatabaseConnection cn, PublicPrivateKeyType keyType, OdinId recipient, bool failIfCannotRetrieve = true)
        {
            string prefix = keyType == PublicPrivateKeyType.OfflineKey ? "aoffline" :
                keyType == PublicPrivateKeyType.OnlineKey ? "bonline" : throw new OdinSystemException("Unhandled key type");

            GuidId cacheKey = GuidId.FromString($"{prefix}{recipient.DomainName}");

            await EccRecipientOnlinePublicKeyCacheLock.WaitAsync();
            try
            {
                var cacheItem = _storage.Get<EccPublicKeyData>(cn, cacheKey);
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
                    _storage.Upsert(cn, cacheKey, cacheItem);
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

        private async Task<RsaPublicKeyData> ResolveRecipientRsaKey(DatabaseConnection cn, PublicPrivateKeyType keyType, OdinId recipient, bool failIfCannotRetrieve = true)
        {
            await RsaRecipientOnlinePublicKeyCacheLock.WaitAsync();
            try
            {
                string prefix = keyType == PublicPrivateKeyType.OfflineKey ? "offline" :
                    keyType == PublicPrivateKeyType.OnlineKey ? "online" : throw new OdinSystemException("Unhandled key type");

                GuidId cacheKey = GuidId.FromString($"{prefix}{recipient.DomainName}");

                var cacheItem = _storage.Get<RsaPublicKeyData>(cn, cacheKey);
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

                    _storage.Upsert(cn, cacheKey, cacheItem);
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

        public async Task<RsaEncryptedPayload> RsaEncryptPayload(PublicPrivateKeyType keyType, byte[] payload, DatabaseConnection cn)
        {
            RsaPublicKeyData pk;

            switch (keyType)
            {
                case PublicPrivateKeyType.OfflineKey:
                    pk = await this.GetOfflineRsaPublicKey(cn);
                    break;

                case PublicPrivateKeyType.OnlineKey:
                    pk = await this.GetOnlineRsaPublicKey(cn);
                    break;

                default:
                    throw new OdinSystemException("Unhandled RsaKeyType");
            }

            return Encrypt(pk, payload);
        }

        public async Task<RsaEncryptedPayload> RsaEncryptPayloadForRecipient(PublicPrivateKeyType keyType, OdinId recipient, byte[] payload, DatabaseConnection cn)
        {
            RsaPublicKeyData pk = await ResolveRecipientRsaKey(cn, keyType, recipient);
            return Encrypt(pk, payload);
        }


        public async Task<EccEncryptedPayload> EccEncryptPayloadForRecipient(PublicPrivateKeyType keyType, OdinId recipient, byte[] payload, DatabaseConnection cn)
        {
            EccPublicKeyData recipientPublicKey = await ResolveRecipientEccPublicKey(cn, keyType, recipient);

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

        public async Task<byte[]> EccDecryptPayload(EccEncryptedPayload payload, DatabaseConnection cn)
        {
            var fullEccKey = await this.GetEccFullKey(PublicPrivateKeyType.OfflineKey, cn);

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

        public Task<EccPublicKeyData> GetSigningPublicKey(DatabaseConnection cn)
        {
            var keyPair = this.GetCurrentEccKeyFromStorage(_signingKeyStorageId, cn);
            return Task.FromResult((EccPublicKeyData)keyPair);
        }

        public Task<EccPublicKeyData> GetOnlineEccPublicKey(DatabaseConnection cn)
        {
            var keyPair = this.GetCurrentEccKeyFromStorage(_onlineEccKeyStorageId, cn);
            return Task.FromResult((EccPublicKeyData)keyPair);
        }

        public Task<EccFullKeyData> GetEccFullKey(PublicPrivateKeyType keyType, DatabaseConnection cn)
        {
            switch (keyType)
            {
                case PublicPrivateKeyType.OfflineKey:
                    return Task.FromResult(GetCurrentEccKeyFromStorage(_offlineEccKeyStorageId, cn));

                case PublicPrivateKeyType.OnlineKey:
                    return Task.FromResult(GetCurrentEccKeyFromStorage(_onlineEccKeyStorageId, cn));

                default:
                    throw new ArgumentOutOfRangeException(nameof(keyType), keyType, null);
            }
        }

        public Task<EccPublicKeyData> GetOfflineEccPublicKey(DatabaseConnection cn)
        {
            var keyPair = this.GetCurrentEccKeyFromStorage(_offlineEccKeyStorageId, cn);
            return Task.FromResult((EccPublicKeyData)keyPair);
        }

        public Task<string> GetNotificationsEccPublicKey(DatabaseConnection cn)
        {
            return Task.FromResult(this.GetEccNotificationsKeys(cn).PublicKey64);
        }

        public NotificationEccKeys GetEccNotificationsKeys(DatabaseConnection cn)
        {
            var keys = _storage.Get<NotificationEccKeys>(cn, _offlineNotificationsKeyStorageId);
            return keys;
        }

        public Task<RsaPublicKeyData> GetOnlineRsaPublicKey(DatabaseConnection cn)
        {
            var keyPair = this.GetCurrentRsaKeyFromStorage(_onlineKeyStorageId, cn);
            return Task.FromResult(RsaPublicKeyData.FromDerEncodedPublicKey(keyPair.publicKey));
        }

        /// <summary>
        /// Upgrades the key to the latest
        /// </summary>
        public async Task<RsaEncryptedPayload> UpgradeRsaKey(PublicPrivateKeyType keyType, RsaFullKeyData currentKey, SensitiveByteArray currentDecryptionKey,
            RsaEncryptedPayload payload, DatabaseConnection cn)
        {
            //unwrap the rsa encrypted key header
            var keyHeader = await DecryptKeyHeaderInternal(currentKey, currentDecryptionKey, payload.RsaEncryptedKeyHeader);

            var pk = await this.GetPublicRsaKey(keyType, cn);

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

        public async Task<(bool IsValidPublicKey, byte[] DecryptedBytes)> RsaDecryptPayload(PublicPrivateKeyType keyType, RsaEncryptedPayload payload,
            IOdinContext odinContext, DatabaseConnection cn)
        {
            var (isValidPublicKey, keyHeader) = await this.RsaDecryptKeyHeader(keyType, payload.RsaEncryptedKeyHeader, payload.Crc32, odinContext, cn);

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

        public async Task<EccPublicKeyData> GetPublicEccKey(PublicPrivateKeyType keyType, DatabaseConnection cn)
        {
            switch (keyType)
            {
                case PublicPrivateKeyType.OfflineKey:
                    return await this.GetOfflineEccPublicKey(cn);

                case PublicPrivateKeyType.OnlineKey:
                    return await this.GetOnlineEccPublicKey(cn);

                default:
                    throw new ArgumentOutOfRangeException(nameof(keyType), keyType, null);
            }
        }


        public async Task<RsaPublicKeyData> GetPublicRsaKey(PublicPrivateKeyType keyType, DatabaseConnection cn)
        {
            switch (keyType)
            {
                case PublicPrivateKeyType.OfflineKey:
                    return await this.GetOfflineRsaPublicKey(cn);
                case PublicPrivateKeyType.OnlineKey:
                    return await this.GetOnlineRsaPublicKey(cn);
                default:
                    throw new ArgumentOutOfRangeException(nameof(keyType), keyType, null);
            }
        }


        /// <summary>
        /// Gets the Ecc key from the storage based on the CRC
        /// </summary>
        public Task<(EccFullKeyData EccKey, SensitiveByteArray DecryptionKey)> ResolveOnlineEccKeyForDecryption(uint crc32, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.Caller.AssertHasMasterKey();

            EccFullKeyListData keyList;
            SensitiveByteArray decryptionKey;

            keyList = this.GetEccKeyListFromStorage(_onlineEccKeyStorageId, cn);
            decryptionKey = odinContext.Caller.GetMasterKey();

            var pk = EccKeyListManagement.FindKey(keyList, crc32);
            return Task.FromResult((pk, decryptionKey));
        }

        /// <summary>
        /// Returns the latest Offline Ecc key
        /// </summary>
        public Task<(EccFullKeyData fullKey, SensitiveByteArray privateKey)> GetCurrentOfflineEccKey(DatabaseConnection cn)
        {
            var keyList = this.GetEccKeyListFromStorage(_offlineEccKeyStorageId, cn);
            var pk = EccKeyListManagement.GetCurrentKey(keyList);
            return Task.FromResult((pk, OfflinePrivateKeyEncryptionKey.ToSensitiveByteArray()));
        }


        /// <summary>
        /// Creates the initial set of RSA keys required by an identity
        /// </summary>
        internal async Task CreateInitialKeys(IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.Caller.AssertHasMasterKey();

            try
            {
                await KeyCreationLock.WaitAsync();
                var mk = odinContext.Caller.GetMasterKey();

                await this.CreateNewRsaKeys(mk, _onlineKeyStorageId, cn);

                await this.CreateNewEccKeys(mk, _signingKeyStorageId, cn);
                await this.CreateNewEccKeys(mk, _onlineEccKeyStorageId, cn);
                await this.CreateNewEccKeys(OfflinePrivateKeyEncryptionKey.ToSensitiveByteArray(), _offlineEccKeyStorageId, cn);

                await this.CreateNewRsaKeys(OfflinePrivateKeyEncryptionKey.ToSensitiveByteArray(), _offlineKeyStorageId, cn);

                await this.CreateNotificationEccKeys(cn);
            }
            finally
            {
                KeyCreationLock.Release();
            }
        }

        private async Task<(bool, KeyHeader)> RsaDecryptKeyHeader(PublicPrivateKeyType keyType, byte[] encryptedData, uint publicKeyCrc32,
            IOdinContext odinContext, DatabaseConnection cn,
            bool failIfNoMatchingPublicKey = true)
        {
            var (rsaKey, decryptionKey) = await ResolveRsaKeyForDecryption(keyType, publicKeyCrc32, odinContext, cn);

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

        private Task<KeyHeader> DecryptKeyHeaderInternal(RsaFullKeyData rsaKey, SensitiveByteArray decryptionKey, byte[] encryptedData)
        {
            var bytes = rsaKey.Decrypt(decryptionKey, encryptedData);
            return Task.FromResult(KeyHeader.FromCombinedBytes(bytes));
        }

        private Task<(RsaFullKeyData RsaKey, SensitiveByteArray DecryptionKey)> ResolveRsaKeyForDecryption(PublicPrivateKeyType keyType, uint crc32,
            IOdinContext odinContext, DatabaseConnection cn)
        {
            RsaFullKeyListData keyList;
            SensitiveByteArray decryptionKey;

            switch (keyType)
            {
                case PublicPrivateKeyType.OfflineKey:
                    keyList = this.GetRsaKeyListFromStorage(_offlineKeyStorageId, cn);
                    decryptionKey = OfflinePrivateKeyEncryptionKey.ToSensitiveByteArray();
                    break;

                case PublicPrivateKeyType.OnlineKey:
                    keyList = this.GetRsaKeyListFromStorage(_onlineKeyStorageId, cn);
                    decryptionKey = odinContext.Caller.GetMasterKey();
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(keyType), keyType, null);
            }

            var pk = RsaKeyListManagement.FindKey(keyList, crc32);
            return Task.FromResult((pk, decryptionKey));
        }

        private Task CreateNewRsaKeys(SensitiveByteArray encryptionKey, Guid storageKey, DatabaseConnection cn)
        {
            var existingKeys = _storage.Get<RsaFullKeyListData>(cn, storageKey);
            if (null != existingKeys)
            {
                throw new OdinSecurityException($"Rsa keys with storage key {storageKey} already exist.");
            }

            //create a new key list
            var rsaKeyList = RsaKeyListManagement.CreateRsaKeyList(encryptionKey,
                RsaKeyListManagement.DefaultMaxOnlineKeys,
                RsaKeyListManagement.DefaultHoursOnlineKey);

            _storage.Upsert(cn, storageKey, rsaKeyList);

            return Task.CompletedTask;
        }

        private Task CreateNotificationEccKeys(DatabaseConnection cn)
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
            _storage.Upsert(cn, storageKey, new NotificationEccKeys
            {
                PublicKey64 = vapidKeys.PublicKey,
                PrivateKey64 = vapidKeys.PrivateKey
            });

            return Task.CompletedTask;
        }

        private Task CreateNewEccKeys(SensitiveByteArray encryptionKey, Guid storageKey, DatabaseConnection cn)
        {
            var existingKeys = _storage.Get<EccFullKeyListData>(cn, storageKey);

            if (null != existingKeys)
            {
                throw new OdinSecurityException($"Ecc keys with storage key {storageKey} already exist.");
            }

            //create a new key list
            var eccKeyList = EccKeyListManagement.CreateEccKeyList(encryptionKey,
                EccKeyListManagement.DefaultMaxOnlineKeys,
                EccKeyListManagement.DefaultHoursOnlineKey);

            _storage.Upsert(cn, storageKey, eccKeyList);

            return Task.CompletedTask;
        }

        private RsaFullKeyData GetCurrentRsaKeyFromStorage(Guid storageKey, DatabaseConnection cn)
        {
            var keyList = GetRsaKeyListFromStorage(storageKey, cn);
            return RsaKeyListManagement.GetCurrentKey(keyList);
        }

        private RsaFullKeyListData GetRsaKeyListFromStorage(Guid storageKey, DatabaseConnection cn)
        {
            return _storage.Get<RsaFullKeyListData>(cn, storageKey);
        }

        private EccFullKeyData GetCurrentEccKeyFromStorage(Guid storageKey, DatabaseConnection cn)
        {
            var keyList = GetEccKeyListFromStorage(storageKey, cn);
            if (null == keyList)
            {
                return null;
            }

            return EccKeyListManagement.GetCurrentKey(keyList);
        }

        private EccFullKeyListData GetEccKeyListFromStorage(Guid storageKey, DatabaseConnection cn)
        {
            return _storage.Get<EccFullKeyListData>(cn, storageKey);
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