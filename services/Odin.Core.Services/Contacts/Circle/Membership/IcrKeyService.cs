#nullable enable
using System.Threading.Tasks;
using Odin.Core.Cryptography.Data;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;

namespace Odin.Core.Services.Contacts.Circle.Membership
{
    /// <summary>
    /// Manages the Icr keys
    /// </summary>
    public class IcrKeyService
    {
        private readonly OdinContextAccessor _contextAccessor;
        private readonly CircleNetworkStorage _storage;

        public IcrKeyService(OdinContextAccessor contextAccessor, TenantSystemStorage tenantSystemStorage)
        {
            _contextAccessor = contextAccessor;
            _storage = new CircleNetworkStorage(tenantSystemStorage);
        }

        /// <summary>
        /// Creates initial encryption keys
        /// </summary>
        public async Task CreateInitialKeys()
        {
            var mk = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            _storage.CreateIcrKey(mk);
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
            var encryptedIcrKey = new SymmetricKeyEncryptedAes(ref encryptionKey, ref rawIcrKey);
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
            return masterKeyEncryptedIcrKey.DecryptKeyClone(ref masterKey);
        }
    }
}