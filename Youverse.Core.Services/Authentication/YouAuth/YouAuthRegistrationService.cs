using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Authentication.YouAuth
{
    public sealed class YouAuthRegistrationService : IYouAuthRegistrationService
    {
        private readonly ILogger<YouAuthRegistrationService> _logger;
        private readonly IYouAuthRegistrationStorage _youAuthRegistrationStorage;
        private readonly ExchangeGrantService _exchangeGrantService;
        private readonly object _mutex = new();

        public YouAuthRegistrationService(ILogger<YouAuthRegistrationService> logger, IYouAuthRegistrationStorage youAuthRegistrationStorage, ExchangeGrantService exchangeGrantService)
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

        //
        
        public ValueTask<(bool isValid, YouAuthClient? client, YouAuthRegistration registration)> ValidateClientAuthToken(ClientAuthenticationToken authToken)
        {
            var client = _youAuthRegistrationStorage.GetYouAuthClient(authToken.Id);
            var accessReg = client?.AccessRegistration;

            if (null == accessReg)
            {
                return new ValueTask<(bool isValid, YouAuthClient client, YouAuthRegistration registration)>((false, null, null));
            }

            var registration = _youAuthRegistrationStorage.LoadFromSubject(client.DotYouId);

            if (null == registration || null == registration.Grant)
            {
                return new ValueTask<(bool isValid, YouAuthClient client, YouAuthRegistration registration)>((false, null, null));
            }

            if (accessReg.IsRevoked || registration.Grant.IsRevoked)
            {
                return new ValueTask<(bool isValid, YouAuthClient client, YouAuthRegistration registration)>((false, null, null));
            }

            return new ValueTask<(bool isValid, YouAuthClient client, YouAuthRegistration registration)>((true, client, registration));
        }

        //
        
        public ValueTask<PermissionContext> GetPermissionContext(ClientAuthenticationToken authToken)
        {
            var (isValid, client, registration) = this.ValidateClientAuthToken(authToken).GetAwaiter().GetResult();

            if (!isValid)
            {
                throw new YouverseSecurityException("Invalid token");
            }
            
            var permissionCtx = _exchangeGrantService.CreatePermissionContext(authToken, registration.Grant, client.AccessRegistration, false).GetAwaiter().GetResult();
            return new ValueTask<PermissionContext>(permissionCtx);
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