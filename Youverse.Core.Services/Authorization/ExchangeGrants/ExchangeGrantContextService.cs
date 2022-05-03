using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core.Cryptography;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authentication;
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

        public async Task<(bool isValid, AccessRegistration registration, ExchangeGrant grant)> ValidateClientAuthToken(ClientAuthToken authToken)
        {
            var (registration, grant) = await _exchangeGrantService.GetAccessAndGrant(authToken);
            if (registration.IsRevoked || grant.IsRevoked)
            {
                return (false, null, null);
            }

            return (true, registration, grant);
        }

        public async Task<PermissionContext> GetContext(ClientAuthToken token)
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
            var grantKeyStoreKey = accessReg.AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey.DecryptKeyClone(ref accessKey);
            accessKey.Wipe();
                
            return new PermissionContext(
                driveGrants: grant.KeyStoreKeyEncryptedDriveGrants,
                permissionSet: grant.PermissionSet,
                driveDecryptionKey: grantKeyStoreKey,
                sharedSecretKey: sharedSecret,
                exchangeGrantId: accessReg.GrantId,
                accessRegistrationId: accessReg.Id,
                _contextAccessor.GetCurrent().Caller.IsOwner
            );
        }
    }
}