using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;

namespace Youverse.Core.Services.Authentication.YouAuth
{
    public sealed class YouAuthRegistrationService : IYouAuthRegistrationService
    {
        private readonly ILogger<YouAuthRegistrationService> _logger;
        private readonly IYouAuthRegistrationStorage _youAuthRegistrationStorage;
        private readonly ICircleNetworkService _circleNetworkService;
        private readonly ExchangeGrantService _exchangeGrantService;
        private readonly CircleDefinitionService _circleDefinitionService;

        public YouAuthRegistrationService(ILogger<YouAuthRegistrationService> logger, IYouAuthRegistrationStorage youAuthRegistrationStorage, ExchangeGrantService exchangeGrantService,
            ICircleNetworkService circleNetworkService, CircleDefinitionService circleDefinitionService)
        {
            _logger = logger;
            _youAuthRegistrationStorage = youAuthRegistrationStorage;
            _exchangeGrantService = exchangeGrantService;
            _circleNetworkService = circleNetworkService;
            _circleDefinitionService = circleDefinitionService;
        }

        //

        public ValueTask<ClientAccessToken> RegisterYouAuthAccess(string dotYouId, ClientAuthenticationToken remoteIcrClientAuthToken)
        {
            if (string.IsNullOrWhiteSpace(dotYouId))
            {
                throw new YouAuthException("Invalid subject");
            }

            if (CreateAuthenticatedYouAuthClient(dotYouId, remoteIcrClientAuthToken, out ClientAccessToken youAuthClientAccessToken))
            {
                return new ValueTask<ClientAccessToken>(youAuthClientAccessToken);
            }

            //
            // dotYouId is connected so let's create a YouAuthClient that uses the ICR's access
            //
            var icr = _circleNetworkService.GetIdentityConnectionRegistration(new DotYouIdentity(dotYouId), remoteIcrClientAuthToken).GetAwaiter().GetResult();

            var token = remoteIcrClientAuthToken.AccessTokenHalfKey;
            var accessKey = icr.AccessGrant.AccessRegistration.ClientAccessKeyEncryptedKeyStoreKey.DecryptKeyClone(ref token);
            // var sharedSecret = icr.AccessGrant.AccessRegistration.AccessKeyStoreKeyEncryptedSharedSecret.DecryptKeyClone(ref accessKey);
            var grantKeyStoreKey = icr.AccessGrant.AccessRegistration.GetGrantKeyStoreKey(accessKey);
            var (accessRegistration, clientAccessToken) = _exchangeGrantService.CreateClientAccessToken(grantKeyStoreKey).GetAwaiter().GetResult();
            grantKeyStoreKey.Wipe();
            var client = new YouAuthClient(accessRegistration.Id, (DotYouIdentity)dotYouId, accessRegistration, YouAuthClientAccessRegistrationType.IdentityConnectionRegistration);
            _youAuthRegistrationStorage.SaveClient(client);

            return new ValueTask<ClientAccessToken>(clientAccessToken);
        }

        /// <summary>
        /// Creates a YouAuth Client for an Identity that is not connected. (will show as authenticated)
        /// </summary>
        private bool CreateAuthenticatedYouAuthClient(string dotYouId, ClientAuthenticationToken remoteIcrClientAuthToken, out ClientAccessToken clientAccessToken)
        {
            YouAuthRegistration registration = _youAuthRegistrationStorage.LoadFromSubject(dotYouId);

            if (null == remoteIcrClientAuthToken)
            {
                //create a youauth registration and/or client
                clientAccessToken = CreateAuthenticatedClient(dotYouId, registration).Result;
                return true;
            }

            //if the ICR is valid but not connected; need to consider - do we fall back to a youauth client or do we check if they're blocked?
            var icr = _circleNetworkService.GetIdentityConnectionRegistration(new DotYouIdentity(dotYouId), remoteIcrClientAuthToken).GetAwaiter().GetResult();
            if (!icr?.IsConnected() ?? false)
            {
                clientAccessToken = CreateAuthenticatedClient(dotYouId, registration).Result;
                return true;
            }

            clientAccessToken = null;
            return false;
        }

        private ValueTask<ClientAccessToken> CreateAuthenticatedClient(string dotYouId, YouAuthRegistration registration)
        {
            //TODO: this is fine until a user gets connected.  then that client needs to re-login.  I wonder if we can detect this
            var emptyKey = Guid.Empty.ToByteArray().ToSensitiveByteArray();

            if (null == registration)
            {
                registration = new YouAuthRegistration(dotYouId, new Dictionary<string, CircleGrant>(), null);
                _youAuthRegistrationStorage.Save(registration);
            }

            var (accessRegistration, cat) = _exchangeGrantService.CreateClientAccessToken(emptyKey).GetAwaiter().GetResult();

            var client = new YouAuthClient(accessRegistration.Id, (DotYouIdentity)dotYouId, accessRegistration, YouAuthClientAccessRegistrationType.YouAuth);
            _youAuthRegistrationStorage.SaveClient(client);

            return new ValueTask<ClientAccessToken>(cat);
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

        public ValueTask<(DotYouIdentity dotYouId, bool isValid, bool isConnected, PermissionContext permissionContext, List<ByteArrayId> enabledCircleIds)> GetPermissionContext(
            ClientAuthenticationToken authToken)
        {
            if (!this.ValidateClientAuthToken(authToken, out var client, out var registration, out var icr))
            {
                return new ValueTask<(DotYouIdentity, bool isValid, bool isConnected, PermissionContext permissionContext, List<ByteArrayId> enabledCircleIds)>(((DotYouIdentity)"", false, false, null,
                    null));
            }

            if (client.AccessRegistrationType == YouAuthClientAccessRegistrationType.YouAuth)
            {
                var token = authToken.AccessTokenHalfKey;
                var accessKey = client.AccessRegistration.ClientAccessKeyEncryptedKeyStoreKey.DecryptKeyClone(ref token);

                var permissionCtx = new PermissionContext(
                    new Dictionary<string, PermissionGroup>
                    {
                        {
                            "anonymous_drives", _exchangeGrantService.CreateAnonymousDrivePermissionGroup().GetAwaiter().GetResult()
                        }
                    },
                    sharedSecretKey: client.AccessRegistration.AccessKeyStoreKeyEncryptedSharedSecret.DecryptKeyClone(ref accessKey),
                    isOwner: false);

                return new ValueTask<(DotYouIdentity, bool, bool, PermissionContext, List<ByteArrayId>)>((client.DotYouId, true, false, permissionCtx, null));
            }

            if (client.AccessRegistrationType == YouAuthClientAccessRegistrationType.IdentityConnectionRegistration)
            {
                var (isConnected, permissionContext, circleIds) = _circleNetworkService.CreatePermissionContext(client.DotYouId, authToken).GetAwaiter().GetResult();
                return new ValueTask<(DotYouIdentity dotYouId,
                    bool isValid,
                    bool isConnected,
                    PermissionContext permissionContext,
                    List<ByteArrayId> enabledCircleIds)>((
                    client.DotYouId, true, isConnected, permissionContext, circleIds));
            }

            throw new YouverseSecurityException("Unhandled access registration type");
        }

        private bool ValidateClientAuthToken(ClientAuthenticationToken authToken, out YouAuthClient client, out YouAuthRegistration registration, out IdentityConnectionRegistration icr)
        {
            client = _youAuthRegistrationStorage.GetYouAuthClient(authToken.Id);
            registration = null;
            icr = null;

            var accessReg = client?.AccessRegistration;
            if (accessReg?.IsRevoked ?? true)
            {
                return false;
            }

            //this should work for both youAuth and icr
            accessReg.AssertValidRemoteKey(authToken.AccessTokenHalfKey);

            if (client.AccessRegistrationType == YouAuthClientAccessRegistrationType.YouAuth)
            {
                registration = _youAuthRegistrationStorage.LoadFromSubject(client.DotYouId);
                if (null == registration)
                {
                    return false;
                }

                return true;
            }
            
            if (client.AccessRegistrationType == YouAuthClientAccessRegistrationType.IdentityConnectionRegistration)
            {
                //here - the auth token is the Youauth Client's auth token and yet the method GetIdentityConnectionRegistration only works w/ the 
                // client.AccessRegistration.GetGrantKeyStoreKey()
                icr = _circleNetworkService.GetIdentityConnectionRegistration(client.DotYouId, true).GetAwaiter().GetResult();
                return icr != null;
            }

            throw new YouverseSecurityException("Unhandled access registration type");
        }
    }
}