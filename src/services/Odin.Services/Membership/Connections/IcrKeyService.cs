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
    public class IcrKeyService(
        CircleNetworkStorage circleNetworkStorage)
    {

        /// <summary>
        /// Creates initial encryption keys
        /// </summary>
        internal async Task CreateInitialKeysAsync(IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();
            var masterKey = odinContext.Caller.GetMasterKey();
            await circleNetworkStorage.CreateIcrKeyAsync(masterKey);
        }

        public async Task<SensitiveByteArray> GetDecryptedIcrKeyAsync(IOdinContext odinContext)
        {
            var masterKey = odinContext.Caller.GetMasterKey();
            return await GetDecryptedIcrKeyInternalAsync(masterKey);
        }

        public async Task<SymmetricKeyEncryptedAes> GetMasterKeyEncryptedIcrKeyAsync()
        {
            var masterKeyEncryptedIcrKey = await circleNetworkStorage.GetMasterKeyEncryptedIcrKeyAsync();
            return masterKeyEncryptedIcrKey;
        }

        public async Task<SymmetricKeyEncryptedAes> ReEncryptIcrKeyAsync(SensitiveByteArray encryptionKey, SensitiveByteArray masterKey)
        {
            var rawIcrKey = await GetDecryptedIcrKeyInternalAsync(masterKey);
            var encryptedIcrKey = new SymmetricKeyEncryptedAes(encryptionKey, rawIcrKey);
            rawIcrKey.Wipe();
            return encryptedIcrKey;
        }

        public async Task<SymmetricKeyEncryptedAes> ReEncryptIcrKeyAsync(SensitiveByteArray encryptionKey, IOdinContext odinContext)
        {
            var masterKey = odinContext.Caller.GetMasterKey();

            var rawIcrKey = await GetDecryptedIcrKeyInternalAsync(masterKey);
            var encryptedIcrKey = new SymmetricKeyEncryptedAes(encryptionKey, rawIcrKey);
            rawIcrKey.Wipe();
            return encryptedIcrKey;
        }

        public async Task<EncryptedClientAccessToken> EncryptClientAccessTokenUsingIrcKeyAsync(ClientAccessToken clientAccessToken, IOdinContext odinContext)
        {
            var masterKey = odinContext.Caller.GetMasterKey();
            var rawIcrKey = await GetDecryptedIcrKeyInternalAsync(masterKey);
            var k = EncryptedClientAccessToken.Encrypt(rawIcrKey, clientAccessToken);
            rawIcrKey.Wipe();
            return k;
        }

        //

        private async Task<SensitiveByteArray> GetDecryptedIcrKeyInternalAsync(SensitiveByteArray masterKey)
        {
            var masterKeyEncryptedIcrKey = await circleNetworkStorage.GetMasterKeyEncryptedIcrKeyAsync();
            return masterKeyEncryptedIcrKey.DecryptKeyClone(masterKey);
        }
    }
}