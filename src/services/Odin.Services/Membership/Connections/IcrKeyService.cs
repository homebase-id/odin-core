#nullable enable
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
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
        internal async Task CreateInitialKeysAsync(IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();
            var masterKey = odinContext.Caller.GetMasterKey();
            await _storage.CreateIcrKeyAsync(masterKey);
        }

        public async Task<SensitiveByteArray> GetDecryptedIcrKeyAsync(IOdinContext odinContext)
        {
            return await GetDecryptedIcrKeyInternalAsync(odinContext);
        }

        public async Task<SymmetricKeyEncryptedAes> GetMasterKeyEncryptedIcrKeyAsync()
        {
            var masterKeyEncryptedIcrKey = await _storage.GetMasterKeyEncryptedIcrKeyAsync();
            return masterKeyEncryptedIcrKey;
        }

        public async Task<SymmetricKeyEncryptedAes> ReEncryptIcrKeyAsync(SensitiveByteArray encryptionKey, IOdinContext odinContext)
        {
            var rawIcrKey = await GetDecryptedIcrKeyInternalAsync(odinContext);
            var encryptedIcrKey = new SymmetricKeyEncryptedAes(encryptionKey, rawIcrKey);
            rawIcrKey.Wipe();
            return encryptedIcrKey;
        }

        public async Task<EncryptedClientAccessToken> EncryptClientAccessTokenUsingIrcKeyAsync(ClientAccessToken clientAccessToken, IOdinContext odinContext)
        {
            var rawIcrKey = await GetDecryptedIcrKeyInternalAsync(odinContext);
            var k = EncryptedClientAccessToken.Encrypt(rawIcrKey, clientAccessToken);
            rawIcrKey.Wipe();
            return k;
        }

        //

        private async Task<SensitiveByteArray> GetDecryptedIcrKeyInternalAsync(IOdinContext odinContext)
        {
            var masterKey = odinContext.Caller.GetMasterKey();
            var masterKeyEncryptedIcrKey = await _storage.GetMasterKeyEncryptedIcrKeyAsync();
            return masterKeyEncryptedIcrKey.DecryptKeyClone(masterKey);
        }
    }
}