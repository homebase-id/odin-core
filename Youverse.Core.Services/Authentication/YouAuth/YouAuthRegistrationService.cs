using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
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
        private readonly TenantContext _tenantContext;

        public YouAuthRegistrationService(ILogger<YouAuthRegistrationService> logger, IYouAuthRegistrationStorage youAuthRegistrationStorage, ExchangeGrantService exchangeGrantService,
            ICircleNetworkService circleNetworkService, CircleDefinitionService circleDefinitionService, TenantContext tenantContext)
        {
            _logger = logger;
            _youAuthRegistrationStorage = youAuthRegistrationStorage;
            _exchangeGrantService = exchangeGrantService;
            _circleNetworkService = circleNetworkService;
            _circleDefinitionService = circleDefinitionService;
            _tenantContext = tenantContext;
        }

        //

        public ValueTask<ClientAccessToken> RegisterYouAuthAccess(string dotYouId, ClientAuthenticationToken remoteIcrClientAuthToken)
        {
            if (string.IsNullOrWhiteSpace(dotYouId))
            {
                throw new YouAuthClientException("Invalid subject");
            }

            if (_circleNetworkService.TryCreateIdentityConnectionClient(dotYouId, remoteIcrClientAuthToken, out var icrClientAccessToken).GetAwaiter().GetResult())
            {
                return new ValueTask<ClientAccessToken>(icrClientAccessToken);
            }

            if (TryCreateAuthenticatedYouAuthClient(dotYouId, out ClientAccessToken youAuthClientAccessToken))
            {
                return new ValueTask<ClientAccessToken>(youAuthClientAccessToken);
            }

            throw new YouverseSecurityException("Unhandled case when registering YouAuth access");
        }


        /// <summary>
        /// Creates a YouAuth Client for an Identity that is not connected. (will show as authenticated)
        /// </summary>
        private bool TryCreateAuthenticatedYouAuthClient(string dotYouId, out ClientAccessToken clientAccessToken)
        {
            YouAuthRegistration registration = _youAuthRegistrationStorage.LoadFromSubject(dotYouId);

            var emptyKey = Guid.Empty.ToByteArray().ToSensitiveByteArray();
            if (null == registration)
            {
                registration = new YouAuthRegistration(dotYouId, new Dictionary<string, CircleGrant>());
                _youAuthRegistrationStorage.Save(registration);
            }

            var (accessRegistration, cat) = _exchangeGrantService.CreateClientAccessToken(emptyKey, ClientTokenType.YouAuth).GetAwaiter().GetResult();
            var client = new YouAuthClient(accessRegistration.Id, (DotYouIdentity)dotYouId, accessRegistration);
            _youAuthRegistrationStorage.SaveClient(client);

            clientAccessToken = cat;
            return true;
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

        public ValueTask DeleteFromSubject(string subject)
        {
            var session = _youAuthRegistrationStorage.LoadFromSubject(subject);
            if (session != null)
            {
                _youAuthRegistrationStorage.Delete(session);
            }

            return new ValueTask();
        }

        public ValueTask<(CallerContext callerContext, PermissionContext permissionContext)> GetPermissionContext(ClientAuthenticationToken authToken)
        {
            /*
             * trying to determine if the icr token given was valid but was blocked
             * if it is invalid, knock you down to anonymous
             * if it is valid but blocked, we knock you down to authenticated
             */
            if (authToken.ClientTokenType == ClientTokenType.IdentityConnectionRegistration)
            {
                var (cc, permissionContext) = _circleNetworkService.CreateConnectedClientContext(authToken).GetAwaiter().GetResult();
                return new ValueTask<(CallerContext callerContext, PermissionContext permissionContext)>((cc, permissionContext));
            }

            if (authToken.ClientTokenType == ClientTokenType.YouAuth)
            {
                if (!this.HasValidClientAuthToken(authToken, out var client, out _))
                {
                    return new ValueTask<(CallerContext callerContext, PermissionContext permissionContext)>((null, null));
                }

                var cc = new CallerContext(
                    dotYouId: client.DotYouId,
                    securityLevel: SecurityGroupType.Authenticated,
                    masterKey: null,
                    circleIds: null
                );


                PermissionContext permissionCtx = CreateAuthenticatedYouAuthPermissionContext(authToken, client);
                return new ValueTask<(CallerContext callerContext, PermissionContext permissionContext)>((cc, permissionCtx));
            }

            throw new YouverseSecurityException("Unhandled access registration type");
        }

        private PermissionContext CreateAuthenticatedYouAuthPermissionContext(ClientAuthenticationToken authToken, YouAuthClient client)
        {
            List<int> permissionKeys = new List<int>() { };
            if (_tenantContext.TenantSystemConfig.AuthenticatedIdentitiesCanViewConnections)
            {
                permissionKeys.Add(PermissionKeys.ReadConnections);
            }

            var token = authToken.AccessTokenHalfKey;
            var accessKey = client.AccessRegistration.ClientAccessKeyEncryptedKeyStoreKey.DecryptKeyClone(ref token);
            var ss = client.AccessRegistration.AccessKeyStoreKeyEncryptedSharedSecret.DecryptKeyClone(ref accessKey);
            accessKey.Wipe();

            var permissionCtx = new PermissionContext(
                new Dictionary<string, PermissionGroup>
                {
                    { "anonymous_drives", _exchangeGrantService.CreateAnonymousDrivePermissionGroup().GetAwaiter().GetResult() },
                    { "read_connections", new PermissionGroup(new PermissionSet(permissionKeys), null, null) }
                },
                sharedSecretKey: ss,
                isOwner: false);

            return permissionCtx;
        }

        private bool HasValidClientAuthToken(ClientAuthenticationToken authToken, out YouAuthClient client, out YouAuthRegistration registration)
        {
            client = _youAuthRegistrationStorage.GetYouAuthClient(authToken.Id);
            registration = null;

            var accessReg = client?.AccessRegistration;
            if (accessReg?.IsRevoked ?? true)
            {
                return false;
            }

            accessReg.AssertValidRemoteKey(authToken.AccessTokenHalfKey);

            registration = _youAuthRegistrationStorage.LoadFromSubject(client.DotYouId);
            if (null == registration)
            {
                return false;
            }

            return true;
        }
    }
}