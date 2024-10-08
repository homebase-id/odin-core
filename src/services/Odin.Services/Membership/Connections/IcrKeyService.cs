﻿#nullable enable
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Storage.SQLite;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Membership.CircleMembership;

namespace Odin.Services.Membership.Connections
{
    /// <summary>
    /// Manages the Icr keys
    /// </summary>
    public class IcrKeyService
    {
        private readonly CircleNetworkStorage _storage;

        public IcrKeyService(TenantSystemStorage tenantSystemStorage, CircleMembershipService circleMembershipService)
        {
            _storage = new CircleNetworkStorage(tenantSystemStorage, circleMembershipService);
        }

        /// <summary>
        /// Creates initial encryption keys
        /// </summary>
        internal async Task CreateInitialKeys(IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.Caller.AssertHasMasterKey();
            var masterKey = odinContext.Caller.GetMasterKey();
            _storage.CreateIcrKey(masterKey, cn);
            await Task.CompletedTask;
        }

        public SensitiveByteArray GetDecryptedIcrKey(IOdinContext odinContext, DatabaseConnection cn)
        {
            return this.GetDecryptedIcrKeyInternal(odinContext, cn);
        }

        public SymmetricKeyEncryptedAes GetMasterKeyEncryptedIcrKey(DatabaseConnection cn)
        {
            var masterKeyEncryptedIcrKey = _storage.GetMasterKeyEncryptedIcrKey(cn);
            return masterKeyEncryptedIcrKey;
        }

        public SymmetricKeyEncryptedAes ReEncryptIcrKey(SensitiveByteArray encryptionKey, IOdinContext odinContext, DatabaseConnection cn)
        {
            var rawIcrKey = GetDecryptedIcrKeyInternal(odinContext, cn);
            var encryptedIcrKey = new SymmetricKeyEncryptedAes(encryptionKey, rawIcrKey);
            rawIcrKey.Wipe();
            return encryptedIcrKey;
        }

        public EncryptedClientAccessToken EncryptClientAccessTokenUsingIrcKey(ClientAccessToken clientAccessToken, IOdinContext odinContext, DatabaseConnection cn)
        {
            var rawIcrKey = GetDecryptedIcrKeyInternal(odinContext, cn);
            var k = EncryptedClientAccessToken.Encrypt(rawIcrKey, clientAccessToken);
            rawIcrKey.Wipe();
            return k;
        }

        //

        private SensitiveByteArray GetDecryptedIcrKeyInternal(IOdinContext odinContext, DatabaseConnection cn)
        {
            var masterKey = odinContext.Caller.GetMasterKey();
            var masterKeyEncryptedIcrKey = _storage.GetMasterKeyEncryptedIcrKey(cn);
            return masterKeyEncryptedIcrKey.DecryptKeyClone(masterKey);
        }
    }
}