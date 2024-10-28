#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Mediator;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Controllers.Home.Service
{
    public sealed class HomeAuthenticatorService : INotificationHandler<IdentityConnectionRegistrationChangedNotification>
    {
        private readonly CircleNetworkService _circleNetworkService;
        private readonly ExchangeGrantService _exchangeGrantService;
        private readonly TenantContext _tenantContext;
        private readonly HomeRegistrationStorage _storage;
        private readonly CircleMembershipService _circleMembershipService;
        private readonly ILogger<HomeAuthenticatorService> _logger;
        private readonly OdinContextCache _cache;

        //

        public HomeAuthenticatorService(
            CircleNetworkService circleNetworkService,
            ExchangeGrantService exchangeGrantService,
            TenantContext tenantContext,
            HomeRegistrationStorage storage,
            CircleMembershipService circleMembershipService,
            ILogger<HomeAuthenticatorService> logger
        )
        {
            _circleNetworkService = circleNetworkService;
            _exchangeGrantService = exchangeGrantService;
            _tenantContext = tenantContext;
            _storage = storage;
            _circleMembershipService = circleMembershipService;
            _logger = logger;
            _cache = new OdinContextCache();
        }

        //

        /// <summary>
        /// Creates a <see cref="ClientAccessToken"/> for access the Home app via the browser
        /// </summary>
        public async Task<ClientAccessToken?> RegisterBrowserAccess(OdinId odinId, ClientAuthenticationToken remoteClientAuthToken, IdentityDatabase db)
        {
            //if the remote identity gave us an ICR token, the remote identity is saying we are connected
            if (remoteClientAuthToken.ClientTokenType == ClientTokenType.IdentityConnectionRegistration)
            {
                //so let's grant the browser token connected level access
                var result = await TryCreateIdentityConnectionClient(odinId, remoteClientAuthToken, db);
                if (result.success)
                {
                    return result.clientAccessToken;
                }

                //TODO: if not connected, do we fall back to anonymous or let authentication fail?
                throw new OdinSystemException("The remote identity return an ICR CAT that is not connected on the calling identity");
            }

            if (remoteClientAuthToken.ClientTokenType == ClientTokenType.YouAuth)
            {
                var result = await TryCreateAuthenticatedYouAuthClient(odinId, remoteClientAuthToken, db);
                if (result.success)
                {
                    return result.clientAccessToken;
                }

                //TODO: if failed to create a youauth client
            }

            throw new OdinSecurityException($"Unhandled ClientTokenType [{remoteClientAuthToken.ClientTokenType}] when registering YouAuth access");
        }


        public Task Handle(IdentityConnectionRegistrationChangedNotification notification, CancellationToken cancellationToken)
        {
            _cache.EnqueueIdentityForReset(notification.OdinId);
            return Task.CompletedTask;
        }


        /// <summary>
        /// Gets the <see cref="IOdinContext"/> for the specified token from cache or disk.
        /// </summary>
        public async Task<IOdinContext?> GetDotYouContext(ClientAuthenticationToken token, IOdinContext odinContext, IdentityDatabase db)
        {
            var creator = new Func<Task<IOdinContext?>>(async delegate
            {
                var dotYouContext = new OdinContext();
                var (callerContext, permissionContext) = await GetPermissionContext(token, odinContext, db);

                if (null == permissionContext || callerContext == null)
                {
                    return null;
                }

                dotYouContext.Caller = callerContext;
                dotYouContext.SetPermissionContext(permissionContext);

                return dotYouContext;
            });

            return await _cache.GetOrAddContextAsync(token, creator);
        }

        public async Task<bool> DeleteSessionAsync(IOdinContext odinContext, IdentityDatabase db)
        {
            try
            {
                var ctx = odinContext.Caller.OdinClientContext;

                if (null != ctx)
                {
                    await _storage.DeleteClientAsync(ctx.AccessRegistrationId);
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        //

        private async Task<(PermissionContext permissionContext, List<GuidId> circleIds)> CreatePermissionContextCore(
            IdentityConnectionRegistration icr,
            ClientAuthenticationToken authToken,
            AccessRegistration accessReg,
            IOdinContext odinContext,
            IdentityDatabase db)
        {
            var (grants, enabledCircles) = await
                _circleMembershipService.MapCircleGrantsToExchangeGrantsAsync(icr.OdinId.AsciiDomain, icr.AccessGrant.CircleGrants.Values.ToList(), odinContext);

            var permissionKeys = _tenantContext.Settings.GetAdditionalPermissionKeysForConnectedIdentities();
            var anonDrivePermissions = _tenantContext.Settings.GetAnonymousDrivePermissionsForConnectedIdentities();

            var permissionCtx = await _exchangeGrantService.CreatePermissionContext(
                authToken: authToken,
                grants: grants,
                accessReg: accessReg,
                odinContext: odinContext,
                db: db,
                additionalPermissionKeys: permissionKeys,
                includeAnonymousDrives: true,
                anonymousDrivePermission: anonDrivePermissions);

            var result = (permissionCtx, enabledCircles);
            return result;
        }

        private async Task<(CallerContext? callerContext, PermissionContext? permissionContext)> GetPermissionContext(ClientAuthenticationToken authToken,
            IOdinContext odinContext, IdentityDatabase db)
        {
            /*
             * trying to determine if the icr token given was valid but was blocked
             * if it is invalid, knock you down to anonymous
             * if it is valid but blocked, we knock you down to authenticated
             */
            var (validAuthToken, client) = await HasValidClientAuthTokenAsync(authToken, db);
            if (!validAuthToken)
            {
                return (null, null);
            }

            if (client!.ClientType == HomeAppClientType.ConnectedIdentity)
            {
                try
                {
                    var (cc, permissionContext) = await CreateConnectedPermissionContext(authToken, odinContext, db);
                    return (cc, permissionContext);
                }
                catch (OdinSecurityException)
                {
                    //if you're no longer connected, we can mark you as authenticated because you still have a client.
                    var (cc, permissionCtx) = await CreateAuthenticatedPermissionContext(authToken, client, odinContext, db);
                    return (cc, permissionCtx);
                }
            }

            if (client.ClientType == HomeAppClientType.UnconnectedIdentity)
            {
                var (cc, permissionCtx) = await CreateAuthenticatedPermissionContext(authToken, client, odinContext, db);
                return (cc, permissionCtx);
            }

            throw new OdinSecurityException("Unhandled Built-in app client type type");
        }

        private async Task<(CallerContext callerContext, PermissionContext permissionContext)> CreateAuthenticatedPermissionContext(
            ClientAuthenticationToken authToken, HomeAppClient client, IOdinContext odinContext, IdentityDatabase db)
        {
            if (null == client)
            {
                throw new OdinSecurityException("Invalid Client");
            }

            if (null == client.AccessRegistration)
            {
                //TODO: should we fail or fallback?  if this is missing we have a data
                //issue on the server BUT we dont want to block the user from seeing the site.
            }

            var permissionKeys = _tenantContext.Settings.GetAdditionalPermissionKeysForAuthenticatedIdentities();
            var anonDrivePermissions = _tenantContext.Settings.GetAnonymousDrivePermissionsForAuthenticatedIdentities();

            var grants = new Dictionary<Guid, ExchangeGrant>()
            {
                //no additional grants for authenticated
            };

            var permissionCtx = await _exchangeGrantService.CreatePermissionContext(authToken,
                grants,
                client.AccessRegistration!,
                odinContext: odinContext,
                db,
                additionalPermissionKeys: permissionKeys, //read_connections
                includeAnonymousDrives: true,
                anonymousDrivePermission: anonDrivePermissions);

            // var token = authToken.AccessTokenHalfKey;
            // var accessKey = client.AccessRegistration?.ClientAccessKeyEncryptedKeyStoreKey.DecryptKeyClone(ref token);
            // var ss = client.AccessRegistration?.AccessKeyStoreKeyEncryptedSharedSecret.DecryptKeyClone(ref accessKey);
            // accessKey?.Wipe();

            // var permissionCtx = new PermissionContext(
            //     new Dictionary<string, PermissionGroup>
            //     {
            //         { "read_anonymous_drives", await _exchangeGrantService.CreateAnonymousDrivePermissionGroup() },
            //         { "read_connections", new PermissionGroup(new PermissionSet(permissionKeys), null, null, null) }
            //     },
            //     sharedSecretKey: ss);

            var cc = new CallerContext(
                odinId: client.OdinId,
                securityLevel: SecurityGroupType.Authenticated,
                masterKey: null,
                circleIds: null,
                odinClientContext: new OdinClientContext()
                {
                    ClientIdOrDomain = client.OdinId,
                    CorsHostName = "",
                    AccessRegistrationId = client.AccessRegistration!.Id,
                    DevicePushNotificationKey = null
                }
            );

            return (cc, permissionCtx);
        }

        private async Task<(bool, HomeAppClient? client)> HasValidClientAuthTokenAsync(ClientAuthenticationToken authToken, IdentityDatabase db)
        {
            HomeAppClient? client = null;

            if (authToken.ClientTokenType != ClientTokenType.BuiltInBrowserApp)
            {
                return (false, client);
            }

            client = await _storage.GetClientAsync(authToken.Id);

            if (client == null)
            {
                return (false, client);
            }

            var accessReg = client.AccessRegistration;
            if (accessReg?.IsRevoked ?? true)
            {
                return (false, client);
            }

            accessReg.AssertValidRemoteKey(authToken.AccessTokenHalfKey);
            return (true, client);
        }


        /// <summary>
        /// Creates a YouAuth Client for an Identity that is not connected. (will show as authenticated)
        /// </summary>
        private async Task<(bool success, ClientAccessToken clientAccessToken)> TryCreateAuthenticatedYouAuthClient(string odinId,
            ClientAuthenticationToken remoteClientAuthToken, IdentityDatabase db)
        {
            var emptyKey = Guid.Empty.ToByteArray().ToSensitiveByteArray();
            var browserClientAccessToken = await StoreClientAsync((OdinId)odinId, emptyKey, HomeAppClientType.UnconnectedIdentity, db);
            return (true, browserClientAccessToken);
        }

        private async Task<(bool success, ClientAccessToken? clientAccessToken)> TryCreateIdentityConnectionClient(string odinId,
            ClientAuthenticationToken remoteClientAuthToken, IdentityDatabase db)
        {
            var icr = await _circleNetworkService.GetIcrAsync(new OdinId(odinId), remoteClientAuthToken);

            if (!icr.IsConnected())
            {
                return (false, null);
            }

            var (grantKeyStoreKey, sharedSecret) = icr.AccessGrant.AccessRegistration.DecryptUsingClientAuthenticationToken(remoteClientAuthToken);
            sharedSecret.Wipe();

            var browserClientAccessToken = await StoreClientAsync(icr.OdinId, grantKeyStoreKey, HomeAppClientType.ConnectedIdentity, db);

            return (true, browserClientAccessToken);
        }

        private async Task<ClientAccessToken> StoreClientAsync(OdinId odinId, SensitiveByteArray grantKeyStoreKey, HomeAppClientType clientType, IdentityDatabase db)
        {
            var (accessRegistration, cat) = await _exchangeGrantService.CreateClientAccessToken(grantKeyStoreKey, ClientTokenType.BuiltInBrowserApp);

            grantKeyStoreKey.Wipe();

            var homeAppClient = new HomeAppClient(odinId, accessRegistration, clientType);
            await _storage.SaveClientAsync(homeAppClient);

            return cat;
        }


        /// <summary>
        /// Creates a caller and permission context for the caller based on the <see cref="IdentityConnectionRegistrationClient"/> resolved by the authToken
        /// </summary>
        private async Task<(CallerContext callerContext, PermissionContext permissionContext)> CreateConnectedPermissionContext(
            ClientAuthenticationToken authToken, IOdinContext odinContext, IdentityDatabase db)
        {
            _logger.LogDebug("Create Connected Permission Context");

            var client = await _storage.GetClientAsync(authToken.Id);
            if (client?.AccessRegistration == null)
            {
                throw new OdinSecurityException("Invalid auth token or invalid client access registration");
            }

            client.AccessRegistration.AssertValidRemoteKey(authToken.AccessTokenHalfKey);

            //TODO: need to remove the override hack method below and support passing in the auth token from an icr client
            var icr = await _circleNetworkService.GetIcrAsync(client.OdinId, odinContext, true);
            bool isAuthenticated = icr.AccessGrant?.IsValid() ?? false;
            bool isConnected = icr.IsConnected();

            // Only return the permissions if the identity is connected.
            if (isAuthenticated && isConnected)
            {
                _logger.LogDebug("Create Connected Permission Context -> {icr} is connected", icr.OdinId);
                var (permissionContext, enabledCircles) = await CreatePermissionContextCore(
                    icr: icr,
                    accessReg: client.AccessRegistration,
                    odinContext: odinContext,
                    authToken: authToken,
                    db: db);

                var cc = new CallerContext(
                    odinId: client.OdinId,
                    masterKey: null,
                    securityLevel: SecurityGroupType.Connected,
                    circleIds: enabledCircles,
                    odinClientContext: new OdinClientContext()
                    {
                        ClientIdOrDomain = client.OdinId,
                        CorsHostName = "",
                        AccessRegistrationId = client.AccessRegistration.Id,
                        DevicePushNotificationKey = null
                    });

                _logger.LogDebug("Create Connected Permission Context -> {icr} has circles: [{circles}]", icr.OdinId, string.Join(",", enabledCircles));

                return (cc, permissionContext);
            }

            throw new OdinSecurityException("Invalid auth token");
        }
    }
}