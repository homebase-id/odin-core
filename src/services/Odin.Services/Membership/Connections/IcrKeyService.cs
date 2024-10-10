#nullable enable
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
    public class IcrKeyService(TenantSystemStorage tenantSystemStorage, CircleMembershipService circleMembershipService)
    {
        private readonly CircleNetworkStorage _storage = new(tenantSystemStorage, circleMembershipService);

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
            var masterKey = odinContext.Caller.GetMasterKey();
            return this.GetDecryptedIcrKeyInternal(masterKey, cn);
        }

        public SymmetricKeyEncryptedAes GetMasterKeyEncryptedIcrKey(DatabaseConnection cn)
        {
            var masterKeyEncryptedIcrKey = _storage.GetMasterKeyEncryptedIcrKey(cn);
            return masterKeyEncryptedIcrKey;
        }

        public SymmetricKeyEncryptedAes ReEncryptIcrKey(SensitiveByteArray encryptionKey, SensitiveByteArray masterKey, DatabaseConnection cn)
        {
            var rawIcrKey = GetDecryptedIcrKeyInternal(masterKey, cn);
            var encryptedIcrKey = new SymmetricKeyEncryptedAes(encryptionKey, rawIcrKey);
            rawIcrKey.Wipe();
            return encryptedIcrKey;
        }

        public SymmetricKeyEncryptedAes ReEncryptIcrKey(SensitiveByteArray encryptionKey, IOdinContext odinContext, DatabaseConnection cn)
        {
            var masterKey = odinContext.Caller.GetMasterKey();

            var rawIcrKey = GetDecryptedIcrKeyInternal(masterKey, cn);
            var encryptedIcrKey = new SymmetricKeyEncryptedAes(encryptionKey, rawIcrKey);
            rawIcrKey.Wipe();
            return encryptedIcrKey;
        }

        public EncryptedClientAccessToken EncryptClientAccessTokenUsingIrcKey(ClientAccessToken clientAccessToken, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            var masterKey = odinContext.Caller.GetMasterKey();

            var rawIcrKey = GetDecryptedIcrKeyInternal(masterKey, cn);
            var k = EncryptedClientAccessToken.Encrypt(rawIcrKey, clientAccessToken);
            rawIcrKey.Wipe();
            return k;
        }

        //

        private SensitiveByteArray GetDecryptedIcrKeyInternal(SensitiveByteArray masterKey, DatabaseConnection cn)
        {
            var masterKeyEncryptedIcrKey = _storage.GetMasterKeyEncryptedIcrKey(cn);
            return masterKeyEncryptedIcrKey.DecryptKeyClone(masterKey);
        }
    }
}