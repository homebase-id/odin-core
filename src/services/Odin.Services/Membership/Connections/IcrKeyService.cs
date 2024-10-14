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
        internal async Task CreateInitialKeys(IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();
            var masterKey = odinContext.Caller.GetMasterKey();
            _storage.CreateIcrKey(masterKey);
            await Task.CompletedTask;
        }

        public SensitiveByteArray GetDecryptedIcrKey(IOdinContext odinContext)
        {
            return this.GetDecryptedIcrKeyInternal(odinContext);
        }

        public SymmetricKeyEncryptedAes GetMasterKeyEncryptedIcrKey()
        {
            var masterKeyEncryptedIcrKey = _storage.GetMasterKeyEncryptedIcrKey();
            return masterKeyEncryptedIcrKey;
        }

        public SymmetricKeyEncryptedAes ReEncryptIcrKey(SensitiveByteArray encryptionKey, IOdinContext odinContext)
        {
            var rawIcrKey = GetDecryptedIcrKeyInternal(odinContext);
            var encryptedIcrKey = new SymmetricKeyEncryptedAes(encryptionKey, rawIcrKey);
            rawIcrKey.Wipe();
            return encryptedIcrKey;
        }

        public EncryptedClientAccessToken EncryptClientAccessTokenUsingIrcKey(ClientAccessToken clientAccessToken, IOdinContext odinContext)
        {
            var rawIcrKey = GetDecryptedIcrKeyInternal(odinContext);
            var k = EncryptedClientAccessToken.Encrypt(rawIcrKey, clientAccessToken);
            rawIcrKey.Wipe();
            return k;
        }

        //

        private SensitiveByteArray GetDecryptedIcrKeyInternal(IOdinContext odinContext)
        {
            var masterKey = odinContext.Caller.GetMasterKey();
            var masterKeyEncryptedIcrKey = _storage.GetMasterKeyEncryptedIcrKey();
            return masterKeyEncryptedIcrKey.DecryptKeyClone(masterKey);
        }
    }
}