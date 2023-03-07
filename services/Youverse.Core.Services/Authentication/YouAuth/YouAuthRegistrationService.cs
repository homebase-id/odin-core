using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication.ExtendedProtection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.AppNotifications.ClientNotifications;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;

#nullable enable

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

        private readonly DotYouContextCache _cache;

        public YouAuthRegistrationService(ILogger<YouAuthRegistrationService> logger, IYouAuthRegistrationStorage youAuthRegistrationStorage, ExchangeGrantService exchangeGrantService,
            ICircleNetworkService circleNetworkService, CircleDefinitionService circleDefinitionService, TenantContext tenantContext)
        {
            _logger = logger;
            _youAuthRegistrationStorage = youAuthRegistrationStorage;
            _exchangeGrantService = exchangeGrantService;
            _circleNetworkService = circleNetworkService;
            _circleDefinitionService = circleDefinitionService;
            _tenantContext = tenantContext;

            _cache = new DotYouContextCache();
        }

        //

        public ValueTask<ClientAccessToken> RegisterYouAuthAccess(string odinId, ClientAuthenticationToken remoteIcrClientAuthToken)
        {
            if (string.IsNullOrWhiteSpace(odinId))
            {
                throw new YouAuthClientException("Invalid subject");
            }

            if (_circleNetworkService.TryCreateIdentityConnectionClient(odinId, remoteIcrClientAuthToken, out var icrClientAccessToken).GetAwaiter().GetResult())
            {
                return new ValueTask<ClientAccessToken>(icrClientAccessToken);
            }

            if (TryCreateAuthenticatedYouAuthClient(odinId, out ClientAccessToken youAuthClientAccessToken))
            {
                return new ValueTask<ClientAccessToken>(youAuthClientAccessToken);
            }

            throw new YouverseSecurityException("Unhandled case when registering YouAuth access");
        }

        /// <summary>
        /// Gets the <see cref="GetDotYouContext"/> for the specified token from cache or disk.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<DotYouContext?> GetDotYouContext(ClientAuthenticationToken token)
        {
            var creator = new Func<Task<DotYouContext?>>(async delegate
            {
                var dotYouContext = new DotYouContext();
                var (callerContext, permissionContext) = await GetPermissionContext(token);

                if (null == permissionContext || callerContext == null)
                {
                    return await Task.FromResult<DotYouContext?>(null);
                }

                dotYouContext.Caller = callerContext;
                dotYouContext.SetPermissionContext(permissionContext);

                return dotYouContext;
            });

            return await _cache.GetOrAddContext(token, creator);
        }

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

        public ValueTask<(CallerContext? callerContext, PermissionContext? permissionContext)> GetPermissionContext(ClientAuthenticationToken authToken)
        {
            /*
             * trying to determine if the icr token given was valid but was blocked
             * if it is invalid, knock you down to anonymous
             * if it is valid but blocked, we knock you down to authenticated
             */
            if (authToken.ClientTokenType == ClientTokenType.IdentityConnectionRegistration)
            {
                try
                {
                    var (cc, permissionContext) = _circleNetworkService.CreateConnectedClientContext(authToken).GetAwaiter().GetResult();
                    return new ValueTask<(CallerContext? callerContext, PermissionContext? permissionContext)>((cc, permissionContext));
                }
                catch (YouverseSecurityException)
                {
                    //TODO: swallow the security exception and return null, otherwise the cache will keep trying to load data from the token 
                    return new ValueTask<(CallerContext? callerContext, PermissionContext? permissionContext)>((null, null));
                }
            }

            if (authToken.ClientTokenType == ClientTokenType.YouAuth)
            {
                if (!this.HasValidClientAuthToken(authToken, out var client, out _))
                {
                    return new ValueTask<(CallerContext? callerContext, PermissionContext? permissionContext)>((null, null));
                }

                if (client == null)
                {
                    throw new YouverseSecurityException("Client not assigned");        
                }

                var cc = new CallerContext(
                    odinId: client.OdinId,
                    securityLevel: SecurityGroupType.Authenticated,
                    masterKey: null,
                    circleIds: null
                );

                PermissionContext permissionCtx = CreateAuthenticatedYouAuthPermissionContext(authToken, client);
                return new ValueTask<(CallerContext? callerContext, PermissionContext? permissionContext)>((cc, permissionCtx));
            }

            throw new YouverseSecurityException("Unhandled access registration type");
        }

        //

        /// <summary>
        /// Creates a YouAuth Client for an Identity that is not connected. (will show as authenticated)
        /// </summary>
        private bool TryCreateAuthenticatedYouAuthClient(string odinId, out ClientAccessToken clientAccessToken)
        {
            var registration = _youAuthRegistrationStorage.LoadFromSubject(odinId);

            var emptyKey = Guid.Empty.ToByteArray().ToSensitiveByteArray();
            if (null == registration)
            {
                registration = new YouAuthRegistration(odinId, new Dictionary<string, CircleGrant>());
                _youAuthRegistrationStorage.Save(registration);
            }

            var (accessRegistration, cat) = _exchangeGrantService.CreateClientAccessToken(emptyKey, ClientTokenType.YouAuth).GetAwaiter().GetResult();
            var client = new YouAuthClient(accessRegistration.Id, (OdinId)odinId, accessRegistration);
            _youAuthRegistrationStorage.SaveClient(client);

            clientAccessToken = cat;
            return true;
        }

        private PermissionContext CreateAuthenticatedYouAuthPermissionContext(ClientAuthenticationToken authToken, YouAuthClient client)
        {
            List<int> permissionKeys = new List<int>() { };
            if (_tenantContext.Settings.AuthenticatedIdentitiesCanViewConnections)
            {
                permissionKeys.Add(PermissionKeys.ReadConnections);
            }

            if (_tenantContext.Settings.AuthenticatedIdentitiesCanViewWhoIFollow)
            {
                permissionKeys.Add(PermissionKeys.ReadWhoIFollow);
            }

            var token = authToken.AccessTokenHalfKey;
            var accessKey = client.AccessRegistration?.ClientAccessKeyEncryptedKeyStoreKey.DecryptKeyClone(ref token);
            var ss = client.AccessRegistration?.AccessKeyStoreKeyEncryptedSharedSecret.DecryptKeyClone(ref accessKey);
            accessKey?.Wipe();

            var permissionCtx = new PermissionContext(
                new Dictionary<string, PermissionGroup>
                {
                    { "read_anonymous_drives", _exchangeGrantService.CreateAnonymousDrivePermissionGroup().GetAwaiter().GetResult() },
                    { "read_connections", new PermissionGroup(new PermissionSet(permissionKeys), null, null) }
                },
                sharedSecretKey: ss);

            return permissionCtx;
        }

        private bool HasValidClientAuthToken(ClientAuthenticationToken authToken, out YouAuthClient? client, out YouAuthRegistration? registration)
        {
            client = _youAuthRegistrationStorage.GetYouAuthClient(authToken.Id);
            registration = null;

            var accessReg = client?.AccessRegistration;
            if (accessReg?.IsRevoked ?? true)
            {
                return false;
            }

            accessReg.AssertValidRemoteKey(authToken.AccessTokenHalfKey);

            if (client == null)
            {
                return false;
            }
            
            registration = _youAuthRegistrationStorage.LoadFromSubject(client.OdinId);
            if (null == registration)
            {
                return false;
            }

            return true;
        }

        public Task Handle(IdentityConnectionRegistrationChangedNotification notification, CancellationToken cancellationToken)
        {
            _cache.EnqueueIdentityForReset(notification.OdinId);
            return Task.CompletedTask;
        }
    }
}