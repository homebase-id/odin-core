#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Services.AppNotifications.ClientNotifications;
using Odin.Core.Services.Authentication.YouAuth;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Membership.Connections;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;

namespace Odin.Hosting.Controllers.Home
{
    public sealed class HomeAuthenticatorService : INotificationHandler<IdentityConnectionRegistrationChangedNotification>
    {
        private readonly ILogger<HomeAuthenticatorService> _logger;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory;
        private readonly CircleNetworkService _circleNetworkService;
        private readonly ExchangeGrantService _exchangeGrantService;
        private readonly TenantContext _tenantContext;
        private readonly HomeRegistrationStorage _homeRegistrationStorage;

        private readonly OdinContextCache _cache;

        //

        public HomeAuthenticatorService(
            ILogger<HomeAuthenticatorService> logger,
            IOdinHttpClientFactory odinHttpClientFactory,
            CircleNetworkService circleNetworkService,
            ExchangeGrantService exchangeGrantService, TenantContext tenantContext, HomeRegistrationStorage homeRegistrationStorage)
        {
            _logger = logger;
            _odinHttpClientFactory = odinHttpClientFactory;
            _circleNetworkService = circleNetworkService;
            _exchangeGrantService = exchangeGrantService;
            _tenantContext = tenantContext;
            _homeRegistrationStorage = homeRegistrationStorage;
            _cache = new OdinContextCache();
        }

        //

        public Task Handle(IdentityConnectionRegistrationChangedNotification notification, CancellationToken cancellationToken)
        {
            _cache.EnqueueIdentityForReset(notification.OdinId);
            return Task.CompletedTask;
        }

        public async ValueTask<YouAuthTokenResponse?> ExchangeCodeForToken(OdinId odinId, string authorizationCode, string digest)
        {
            var tokenRequest = new YouAuthTokenRequest
            {
                Code = authorizationCode,
                SecretDigest = digest
            };

            var response = await _odinHttpClientFactory
                .CreateClient<IHomePerimeterHttpClient>(odinId)
                .ExchangeCodeForToken(tokenRequest);

            if (response.IsSuccessStatusCode && response.Content != null)
            {
                return response.Content;
            }

            return null;

            //TODO: need to determine how to handle these scenarios

            // if (response.StatusCode == HttpStatusCode.BadRequest)
            // {
            // }
            //
            // if (response.StatusCode == HttpStatusCode.NotFound)
            // {
            //     throw new OdinClientException("");
            // }

            // throw new OdinSystemException("unhandled scenario");
        }

        /// <summary>
        /// Gets the <see cref="OdinContext"/> for the specified token from cache or disk.
        /// </summary>
        /// <param name="token"></param>
        public async Task<OdinContext?> GetDotYouContext(ClientAuthenticationToken token)
        {
            var creator = new Func<Task<OdinContext?>>(async delegate
            {
                var dotYouContext = new OdinContext();
                var (callerContext, permissionContext) = await GetPermissionContext(token);

                if (null == permissionContext || callerContext == null)
                {
                    return await Task.FromResult<OdinContext?>(null);
                }

                dotYouContext.Caller = callerContext;
                dotYouContext.SetPermissionContext(permissionContext);

                return dotYouContext;
            });

            return await _cache.GetOrAddContext(token, creator);
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
                    var (cc, permissionContext) = _circleNetworkService.CreateConnectedYouAuthClientContextClassic(authToken).GetAwaiter().GetResult();
                    return new ValueTask<(CallerContext? callerContext, PermissionContext? permissionContext)>((cc, permissionContext));
                }
                catch (OdinSecurityException)
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
                    throw new OdinSecurityException("Client not assigned");
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

            throw new OdinSecurityException("Unhandled access registration type");
        }


        public async ValueTask<ClientAccessToken> RegisterBrowserAccess(OdinId odinId, ClientAuthenticationToken remoteClientAuthToken)
        {
            if (remoteClientAuthToken.ClientTokenType == ClientTokenType.IdentityConnectionRegistration)
            {
                if (_circleNetworkService.TryCreateIdentityConnectionClient(odinId, remoteClientAuthToken, out var icrClientAccessToken).GetAwaiter()
                    .GetResult())
                {
                    return icrClientAccessToken;
                }
            }

            if (TryCreateAuthenticatedYouAuthClient(odinId, out ClientAccessToken youAuthClientAccessToken))
            {
                return youAuthClientAccessToken;
            }

            throw new OdinSecurityException("Unhandled case when registering YouAuth access");
        }

        //

        public ValueTask DeleteSession(string subject)
        {
            //TODO: need to delete an access registration?
            return ValueTask.CompletedTask;
        }

        //

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
                    { "read_connections", new PermissionGroup(new PermissionSet(permissionKeys), null, null, null) }
                },
                sharedSecretKey: ss);

            return permissionCtx;
        }

        private bool HasValidClientAuthToken(ClientAuthenticationToken authToken, out YouAuthClient? client, out YouAuthRegistration? registration)
        {
            client = _homeRegistrationStorage.GetYouAuthClient(authToken.Id);
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

            registration = _homeRegistrationStorage.LoadFromSubject(client.OdinId);
            if (null == registration)
            {
                return false;
            }

            return true;
        }


        /// <summary>
        /// Creates a YouAuth Client for an Identity that is not connected. (will show as authenticated)
        /// </summary>
        private bool TryCreateAuthenticatedYouAuthClient(string odinId, out ClientAccessToken clientAccessToken)
        {
            var registration = _homeRegistrationStorage.LoadFromSubject(odinId);

            var emptyKey = Guid.Empty.ToByteArray().ToSensitiveByteArray();
            if (null == registration)
            {
                registration = new YouAuthRegistration(odinId, new Dictionary<string, CircleGrant>());
                _homeRegistrationStorage.Save(registration);
            }

            var (accessRegistration, cat) = _exchangeGrantService.CreateClientAccessToken(emptyKey, ClientTokenType.YouAuth).GetAwaiter().GetResult();
            var client = new YouAuthClient(accessRegistration.Id, (OdinId)odinId, accessRegistration);
            _homeRegistrationStorage.SaveClient(client);

            clientAccessToken = cat;
            return true;
        }
    }
}