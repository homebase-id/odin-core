#nullable enable
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Data;
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
        private readonly OdinContextAccessor _contextAccessor;
        private readonly CircleNetworkStorage _storage;

        public IcrKeyService(OdinContextAccessor contextAccessor, TenantSystemStorage tenantSystemStorage, CircleMembershipService circleMembershipService)
        {
            _contextAccessor = contextAccessor;
            _storage = new CircleNetworkStorage(tenantSystemStorage, circleMembershipService);
        }

        /// <summary>
        /// Creates initial encryption keys
        /// </summary>
        internal async Task CreateInitialKeys()
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            _storage.CreateIcrKey(masterKey);
            await Task.CompletedTask;
        }

        public SensitiveByteArray GetDecryptedIcrKey()
        {
            return this.GetDecryptedIcrKeyInternal();
        }

        public SymmetricKeyEncryptedAes GetMasterKeyEncryptedIcrKey()
        {
            var masterKeyEncryptedIcrKey = _storage.GetMasterKeyEncryptedIcrKey();
            return masterKeyEncryptedIcrKey;
        }

        public SymmetricKeyEncryptedAes ReEncryptIcrKey(SensitiveByteArray encryptionKey)
        {
            var rawIcrKey = GetDecryptedIcrKeyInternal();
            var encryptedIcrKey = new SymmetricKeyEncryptedAes(encryptionKey, rawIcrKey);
            rawIcrKey.Wipe();
            return encryptedIcrKey;
        }

        public EncryptedClientAccessToken EncryptClientAccessTokenUsingIrcKey(ClientAccessToken clientAccessToken)
        {
            var rawIcrKey = GetDecryptedIcrKeyInternal();
            var k = EncryptedClientAccessToken.Encrypt(rawIcrKey, clientAccessToken);
            rawIcrKey.Wipe();
            return k;
        }

        //

        private SensitiveByteArray GetDecryptedIcrKeyInternal()
        {
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var masterKeyEncryptedIcrKey = _storage.GetMasterKeyEncryptedIcrKey();
            return masterKeyEncryptedIcrKey.DecryptKeyClone(masterKey);
        }
    }
}