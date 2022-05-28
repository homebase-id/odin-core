using System.Threading.Tasks;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Authorization.ExchangeGrants
{
    public class ExchangeGrantContextService
    {
        private readonly ExchangeGrantService _exchangeGrantService;
        private readonly IDriveService _driveService;
        private readonly DotYouContextAccessor _contextAccessor;

        public ExchangeGrantContextService(ExchangeGrantService exchangeGrantService, IDriveService driveService, DotYouContextAccessor contextAccessor)
        {
            _exchangeGrantService = exchangeGrantService;
            _driveService = driveService;
            _contextAccessor = contextAccessor;
        }

        public async Task<(bool isValid, AccessRegistration registration, ExchangeGrantBase grant)> ValidateClientAuthToken(string sharedSecretEncryptedClientAuthToken64)
        {
            //TODO: decrypt using  _contextAccessor.GetCurrent().AppContext.ClientSharedSecret and IV?
            var decryptedCat = sharedSecretEncryptedClientAuthToken64;
            var cat = ClientAuthenticationToken.Parse(decryptedCat);
            return await this.ValidateClientAuthToken(cat);
        }

        public async Task<(bool isValid, AccessRegistration registration, ExchangeGrantBase grant)> ValidateClientAuthToken(ClientAuthenticationToken authenticationToken)
        {
            var (registration, grant) = await _exchangeGrantService.GetAccessAndGrant(authenticationToken);

            if (null == registration || null == grant)
            {
                return (false, null, null);
            }
            
            if (registration.IsRevoked || grant.IsRevoked)
            {
                return (false, null, null);
            }

            return (true, registration, grant);
        }

        public async Task<PermissionContext> GetContext(ClientAuthenticationToken token)
        {
            var (isValid, accessReg, grant) = await this.ValidateClientAuthToken(token);

            if (!isValid)
            {
                throw new YouverseSecurityException("Invalid token");
            }

            //TODO: Need to decide if we store shared secret clear text or decrypt just in time.
            var key = token.AccessTokenHalfKey;
            var accessKey = accessReg.ClientAccessKeyEncryptedKeyStoreKey.DecryptKeyClone(ref key);
            var sharedSecret = accessReg.AccessKeyStoreKeyEncryptedSharedSecret.DecryptKeyClone(ref accessKey);
            
            var grantKeyStoreKey = accessReg.GetGrantKeyStoreKey(accessKey);
            accessKey.Wipe();

            return new PermissionContext(
                driveGrants: grant.KeyStoreKeyEncryptedDriveGrants,
                permissionSet: grant.PermissionSet,
                driveDecryptionKey: grantKeyStoreKey,
                sharedSecretKey: sharedSecret,
                exchangeGrantId: accessReg.GrantId,
                accessRegistrationId: accessReg.Id,
                isOwner: _contextAccessor.GetCurrent().Caller.IsOwner
            );
        }
        
        /// <summary>
        /// Gets context for requests coming in from YouAuth
        /// </summary>
        public async Task<PermissionContext> GetYouAuthContext(ClientAuthenticationToken token)
        {
            var (isValid, accessReg, grant) = await this.ValidateClientAuthToken(token);

            if (!isValid)
            {
                throw new YouverseSecurityException("Invalid token");
            }

            //TODO: Need to decide if we store shared secret clear text or decrypt just in time.
            var key = token.AccessTokenHalfKey;
            var accessKey = accessReg.ClientAccessKeyEncryptedKeyStoreKey.DecryptKeyClone(ref key);
            var sharedSecret = accessReg.AccessKeyStoreKeyEncryptedSharedSecret.DecryptKeyClone(ref accessKey);
            
            var grantKeyStoreKey = accessReg.GetGrantKeyStoreKey(accessKey);
            accessKey.Wipe();

            return new PermissionContext(
                driveGrants: grant.KeyStoreKeyEncryptedDriveGrants,
                permissionSet: grant.PermissionSet,
                driveDecryptionKey: grantKeyStoreKey,
                sharedSecretKey: sharedSecret,
                exchangeGrantId: accessReg.GrantId,
                accessRegistrationId: accessReg.Id,
                isOwner: _contextAccessor.GetCurrent().Caller.IsOwner
            );
        }
    }
}