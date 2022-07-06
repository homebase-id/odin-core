using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrantRedux;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Authentication.YouAuth
{
    public sealed class YouAuthRegistrationService : IYouAuthRegistrationService
    {
        private readonly ILogger<YouAuthRegistrationService> _logger;
        private readonly IYouAuthRegistrationStorage _youAuthRegistrationStorage;
        private readonly ExchangeGrantServiceRedux _exchangeGrantService;
        private readonly object _mutex = new();

        public YouAuthRegistrationService(ILogger<YouAuthRegistrationService> logger, IYouAuthRegistrationStorage youAuthRegistrationStorage, ExchangeGrantServiceRedux exchangeGrantService)
        {
            _logger = logger;
            _youAuthRegistrationStorage = youAuthRegistrationStorage;
            _exchangeGrantService = exchangeGrantService;
        }

        //

        public ValueTask<ClientAccessToken> RegisterYouAuthAccess(string dotYouId, ClientAuthenticationToken remoteIcrClientAuthToken)
        {
            if (string.IsNullOrWhiteSpace(dotYouId))
            {
                throw new YouAuthException("Invalid subject");
            }

            // NOTE: this lock only works because litedb isn't async
            lock (_mutex)
            {
                var registration = GetOrCreateRegistration(dotYouId);
                var (accessRegistration, clientAccessToken) = _exchangeGrantService.CreateClientAccessToken(registration.Grant, null).GetAwaiter().GetResult();

                var client = new YouAuthClient(accessRegistration.Id, (DotYouIdentity)dotYouId, accessRegistration);
                _youAuthRegistrationStorage.SaveClient(client);

                return new ValueTask<ClientAccessToken>(clientAccessToken);
            }
        }

        public ValueTask<YouAuthRegistration?> LoadFromId(Guid id)
        {
            var session = _youAuthRegistrationStorage.LoadFromId(id);

            if (session != null)
            {
                _youAuthRegistrationStorage.Delete(session);
                session = null;
            }

            return new ValueTask<YouAuthRegistration?>(session);
        }

        //

        public ValueTask<YouAuthRegistration?> LoadFromSubject(string subject)
        {
            var session = _youAuthRegistrationStorage.LoadFromSubject(subject);

            if (session != null)
            {
                _youAuthRegistrationStorage.Delete(session);
                session = null;
            }

            return new ValueTask<YouAuthRegistration?>(session);
        }

        //

        public ValueTask DeleteFromSubject(string subject)
        {
            var session = _youAuthRegistrationStorage.LoadFromSubject(subject);
            if (session != null)
            {
                _youAuthRegistrationStorage.Delete(session);
            }

            return new ValueTask();
        }

        public PermissionContext GetContext(ClientAccessToken authToken)
        {
            var client = _youAuthRegistrationStorage.GetYouAuthClient(authToken.Id);
            var accessReg = client?.AccessRegistration;
            
            if (null == accessReg)
            {
                throw new YouverseSecurityException("Invalid token");
            }

            var registration = _youAuthRegistrationStorage.LoadFromSubject(client.DotYouId);

            if (null == registration || null == registration.Grant)
            {
                throw new YouverseSecurityException("Invalid token");
            }

            if (accessReg.IsRevoked || registration.Grant.IsRevoked)
            {
                throw new YouverseSecurityException("Invalid token");
            }

            //TODO: Need to decide if we store shared secret clear text or decrypt just in time.
            var key = authToken.AccessTokenHalfKey;
            var accessKey = accessReg.ClientAccessKeyEncryptedKeyStoreKey.DecryptKeyClone(ref key);
            var sharedSecret = accessReg.AccessKeyStoreKeyEncryptedSharedSecret.DecryptKeyClone(ref accessKey);

            var grantKeyStoreKey = accessReg.GetGrantKeyStoreKey(accessKey);
            accessKey.Wipe();

            return new PermissionContext(
                driveGrants: registration.Grant.KeyStoreKeyEncryptedDriveGrants,
                permissionSet: registration.Grant.PermissionSet,
                driveDecryptionKey: grantKeyStoreKey,
                sharedSecretKey: sharedSecret,
                exchangeGrantId: accessReg.GrantId,
                accessRegistrationId: accessReg.Id,
                isOwner: false
            );
        }

        //

        private YouAuthRegistration GetOrCreateRegistration(string dotYouId)
        {
            YouAuthRegistration registration = _youAuthRegistrationStorage.LoadFromSubject(dotYouId);

            if (registration == null)
            {
                var grant = _exchangeGrantService.CreateExchangeGrant(null, null, null).GetAwaiter().GetResult();
                registration = new YouAuthRegistration(Guid.NewGuid(), dotYouId, grant);
                _youAuthRegistrationStorage.Save(registration);
            }

            return registration;
        }
    }
}