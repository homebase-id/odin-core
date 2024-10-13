#nullable enable
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Membership.CircleMembership;

namespace Odin.Services.Membership.Connections
{
    /// <summary>
    /// Manages the Icr keys
    /// </summary>
    public class IcrKeyService(TenantSystemStorage tenantSystemStorage, CircleMembershipService circleMembershipService)
    {
        private readonly CircleNetworkStorage _storage = new(tenantSystemStorage, circleMembershipService);

        /// <summary>
        /// Creates initial encryption keys
        /// </summary>
        internal async Task CreateInitialKeys(IOdinContext odinContext, IdentityDatabase db)
        {
            odinContext.Caller.AssertHasMasterKey();
            var masterKey = odinContext.Caller.GetMasterKey();
            _storage.CreateIcrKey(masterKey, db);
            await Task.CompletedTask;
        }

        public SensitiveByteArray GetDecryptedIcrKey(IOdinContext odinContext, IdentityDatabase db)
        {
            var masterKey = odinContext.Caller.GetMasterKey();
            return this.GetDecryptedIcrKeyInternal(masterKey, db);
        }

        public SymmetricKeyEncryptedAes GetMasterKeyEncryptedIcrKey(IdentityDatabase db)
        {
            var masterKeyEncryptedIcrKey = _storage.GetMasterKeyEncryptedIcrKey(db);
            return masterKeyEncryptedIcrKey;
        }

        public SymmetricKeyEncryptedAes ReEncryptIcrKey(SensitiveByteArray encryptionKey, SensitiveByteArray masterKey, IdentityDatabase db)
        {
            var rawIcrKey = GetDecryptedIcrKeyInternal(masterKey, db);
            var encryptedIcrKey = new SymmetricKeyEncryptedAes(encryptionKey, rawIcrKey);
            rawIcrKey.Wipe();
            return encryptedIcrKey;
        }

        public SymmetricKeyEncryptedAes ReEncryptIcrKey(SensitiveByteArray encryptionKey, IOdinContext odinContext, IdentityDatabase db)
        {
            var masterKey = odinContext.Caller.GetMasterKey();

            var rawIcrKey = GetDecryptedIcrKeyInternal(masterKey, db);
            var encryptedIcrKey = new SymmetricKeyEncryptedAes(encryptionKey, rawIcrKey);
            rawIcrKey.Wipe();
            return encryptedIcrKey;
        }

        public EncryptedClientAccessToken EncryptClientAccessTokenUsingIrcKey(ClientAccessToken clientAccessToken, IOdinContext odinContext,
            IdentityDatabase db)
        {
            var masterKey = odinContext.Caller.GetMasterKey();

            var rawIcrKey = GetDecryptedIcrKeyInternal(masterKey, db);
            var k = EncryptedClientAccessToken.Encrypt(rawIcrKey, clientAccessToken);
            rawIcrKey.Wipe();
            return k;
        }

        //

        private SensitiveByteArray GetDecryptedIcrKeyInternal(SensitiveByteArray masterKey, IdentityDatabase db)
        {
            var masterKeyEncryptedIcrKey = _storage.GetMasterKeyEncryptedIcrKey(db);
            return masterKeyEncryptedIcrKey.DecryptKeyClone(masterKey);
        }
    }
}