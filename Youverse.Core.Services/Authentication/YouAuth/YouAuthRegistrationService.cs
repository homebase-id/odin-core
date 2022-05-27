using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;

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

        public ValueTask<(YouAuthRegistration, ClientAccessToken)> RegisterYouAuthAccess(string dotYouId, ClientAuthenticationToken remoteIcrClientAuthToken)
        {
            if (string.IsNullOrWhiteSpace(dotYouId))
            {
                throw new YouAuthException("Invalid subject");
            }

            // NOTE: this lock only works because litedb isn't async
            lock (_mutex)
            {
                YouAuthRegistration registration = _youAuthRegistrationStorage.LoadFromSubject(dotYouId);

                //if this dotYouId has no registration
                if (registration == null)
                {
                    var (grant, clientAccessToken) = this.EnsureGrant(dotYouId, remoteIcrClientAuthToken);

                    registration = new YouAuthRegistration(Guid.NewGuid(), dotYouId);

                    _youAuthRegistrationStorage.Save(registration);
                    return ValueTask.FromResult<(YouAuthRegistration, ClientAccessToken)>((registration, clientAccessToken));
                }

                //
                // There is a registration so let's see if we need to add a new client
                // 
                if (remoteIcrClientAuthToken == null)
                {
                    //We need to add a new client
                    
                    //need to create a new EGR for this browser and identity

                    var (grant, clientAccessToken) = _exchangeGrantService.CreateYouAuthExchangeGrant((DotYouIdentity) dotYouId, null, null, AccessRegistrationClientType.Cookies).GetAwaiter().GetResult();
                    registration = new YouAuthRegistration(registration.Id, dotYouId);

                    _youAuthRegistrationStorage.Save(registration);
                    return new ValueTask<(YouAuthRegistration, ClientAccessToken)>((registration, clientAccessToken));
                }
                
                //
                // There is a remoteIcrClientAuthToken so we need to upgrade
                // 

                var (newGrant, newClientAccessToken) = _exchangeGrantService.SpawnYouAuthExchangeGrant(remoteIcrClientAuthToken, AccessRegistrationClientType.Cookies).GetAwaiter().GetResult();
                registration = new YouAuthRegistration(registration.Id, registration.Subject);
                _youAuthRegistrationStorage.Save(registration);
                return ValueTask.FromResult<(YouAuthRegistration, ClientAccessToken)>((registration, newClientAccessToken));

            }
        }

        private (YouAuthExchangeGrant, ClientAccessToken) EnsureGrant(string dotYouId, ClientAuthenticationToken remoteIcrClientAuthToken)
        {
            if (remoteIcrClientAuthToken == null)
            {
                var (grant, clientAccessToken) = _exchangeGrantService.CreateYouAuthExchangeGrant((DotYouIdentity) dotYouId, null, null, AccessRegistrationClientType.Cookies).GetAwaiter().GetResult();
                return (grant, clientAccessToken);
            }
            else
            {
                //create a grant based on the ICR
                var (grant, clientAccessToken) = _exchangeGrantService.SpawnYouAuthExchangeGrant(remoteIcrClientAuthToken, AccessRegistrationClientType.Cookies).GetAwaiter().GetResult();
                return (grant, clientAccessToken);
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

        //
    }
}