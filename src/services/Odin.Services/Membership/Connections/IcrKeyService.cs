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
        internal async Task CreateInitialKeys(IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();
            var masterKey = odinContext.Caller.GetMasterKey();
            _storage.CreateIcrKey(masterKey);
            await Task.CompletedTask;
        }

        public SensitiveByteArray GetDecryptedIcrKey(IOdinContext odinContext)
        {
            var masterKey = odinContext.Caller.GetMasterKey();
            return this.GetDecryptedIcrKeyInternal(masterKey);
        }

        public SymmetricKeyEncryptedAes GetMasterKeyEncryptedIcrKey()
        {
            var masterKeyEncryptedIcrKey = _storage.GetMasterKeyEncryptedIcrKey();
            return masterKeyEncryptedIcrKey;
        }

        public SymmetricKeyEncryptedAes ReEncryptIcrKey(SensitiveByteArray encryptionKey, SensitiveByteArray masterKey)
        {
            var rawIcrKey = GetDecryptedIcrKeyInternal(masterKey);
            var encryptedIcrKey = new SymmetricKeyEncryptedAes(encryptionKey, rawIcrKey);
            rawIcrKey.Wipe();
            return encryptedIcrKey;
        }

        public SymmetricKeyEncryptedAes ReEncryptIcrKey(SensitiveByteArray encryptionKey, IOdinContext odinContext)
        {
            var masterKey = odinContext.Caller.GetMasterKey();

            var rawIcrKey = GetDecryptedIcrKeyInternal(masterKey);
            var encryptedIcrKey = new SymmetricKeyEncryptedAes(encryptionKey, rawIcrKey);
            rawIcrKey.Wipe();
            return encryptedIcrKey;
        }

        public EncryptedClientAccessToken EncryptClientAccessTokenUsingIrcKey(ClientAccessToken clientAccessToken, IOdinContext odinContext)
        {
            var masterKey = odinContext.Caller.GetMasterKey();

            var rawIcrKey = GetDecryptedIcrKeyInternal(masterKey);
            var k = EncryptedClientAccessToken.Encrypt(rawIcrKey, clientAccessToken);
            rawIcrKey.Wipe();
            return k;
        }

        //

        private SensitiveByteArray GetDecryptedIcrKeyInternal(SensitiveByteArray masterKey)
        {
            var masterKeyEncryptedIcrKey = _storage.GetMasterKeyEncryptedIcrKey();
            return masterKeyEncryptedIcrKey.DecryptKeyClone(masterKey);
        }
    }
}