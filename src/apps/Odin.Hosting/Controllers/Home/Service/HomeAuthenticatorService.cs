﻿#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Services.AppNotifications.ClientNotifications;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Mediator;
using Odin.Core.Services.Membership;
using Odin.Core.Services.Membership.CircleMembership;
using Odin.Core.Services.Membership.Connections;

namespace Odin.Hosting.Controllers.Home.Service
{
    public sealed class HomeAuthenticatorService : INotificationHandler<IdentityConnectionRegistrationChangedNotification>
    {
        private readonly OdinContextAccessor _contextAccessor;
        private readonly CircleNetworkService _circleNetworkService;
        private readonly ExchangeGrantService _exchangeGrantService;
        private readonly TenantContext _tenantContext;
        private readonly HomeRegistrationStorage _storage;
        private readonly CircleMembershipService _circleMembershipService;
        private readonly OdinContextCache _cache;

        //

        public HomeAuthenticatorService(
            CircleNetworkService circleNetworkService,
            ExchangeGrantService exchangeGrantService,
            TenantContext tenantContext,
            HomeRegistrationStorage storage,
            CircleMembershipService circleMembershipService,
            OdinContextAccessor contextAccessor)
        {
            _circleNetworkService = circleNetworkService;
            _exchangeGrantService = exchangeGrantService;
            _tenantContext = tenantContext;
            _storage = storage;
            _circleMembershipService = circleMembershipService;
            _contextAccessor = contextAccessor;
            _cache = new OdinContextCache();
        }

        //

        /// <summary>
        /// Creates a <see cref="ClientAccessToken"/> for access the Home app via the browser
        /// </summary>
        public async ValueTask<ClientAccessToken> RegisterBrowserAccess(OdinId odinId, ClientAuthenticationToken remoteClientAuthToken)
        {
            //if the remote identity gave us an ICR token, the remote identity is saying we are connected
            if (remoteClientAuthToken.ClientTokenType == ClientTokenType.IdentityConnectionRegistration)
            {
                //so let's grant the browser token connected level access
                if (await this.TryCreateIdentityConnectionClient(odinId, remoteClientAuthToken, out var icrClientAccessToken))
                {
                    return icrClientAccessToken!;
                }

                //TODO: if not connected, do we fall back to anonymous or let authentication fail?
                throw new OdinSystemException("The remote identity return an ICR CAT that is not connected on the calling identity");
            }

            if (remoteClientAuthToken.ClientTokenType == ClientTokenType.YouAuth)
            {
                if (TryCreateAuthenticatedYouAuthClient(odinId, remoteClientAuthToken, out ClientAccessToken youAuthClientAccessToken))
                {
                    return youAuthClientAccessToken;
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

        public ValueTask<bool> DeleteSession()
        {
            try
            {
                var ctx = _contextAccessor.GetCurrent().Caller.OdinClientContext;

                if (null != ctx)
                {
                    _storage.DeleteClient(ctx.AccessRegistrationId);
                }
            }
            catch
            {
                return new ValueTask<bool>(false);
            }

            return new ValueTask<bool>(true);
        }

        //

        private async Task<(PermissionContext permissionContext, List<GuidId> circleIds)> CreatePermissionContextCore(
            IdentityConnectionRegistration icr,
            ClientAuthenticationToken authToken,
            AccessRegistration accessReg)
        {
            var (grants, enabledCircles) = _circleMembershipService.MapCircleGrantsToExchangeGrants(icr.AccessGrant.CircleGrants.Values.ToList());

            var permissionKeys = _tenantContext.Settings.GetAdditionalPermissionKeysForConnectedIdentities();
            var anonDrivePermissions = _tenantContext.Settings.GetAnonymousDrivePermissionsForConnectedIdentities();

            var permissionCtx = await _exchangeGrantService.CreatePermissionContext(
                authToken: authToken,
                grants: grants,
                accessReg: accessReg,
                additionalPermissionKeys: permissionKeys,
                includeAnonymousDrives: true,
                anonymousDrivePermission: anonDrivePermissions);

            var result = (permissionCtx, enabledCircles);
            return await Task.FromResult(result);
        }

        private ValueTask<(CallerContext? callerContext, PermissionContext? permissionContext)> GetPermissionContext(ClientAuthenticationToken authToken)
        {
            /*
             * trying to determine if the icr token given was valid but was blocked
             * if it is invalid, knock you down to anonymous
             * if it is valid but blocked, we knock you down to authenticated
             */

            if (!this.HasValidClientAuthToken(authToken, out var client))
            {
                return new ValueTask<(CallerContext? callerContext, PermissionContext? permissionContext)>((null, null));
            }

            if (client!.ClientType == HomeAppClientType.ConnectedIdentity)
            {
                try
                {
                    var (cc, permissionContext) = CreateConnectedPermissionContext(authToken).GetAwaiter().GetResult();
                    return new ValueTask<(CallerContext? callerContext, PermissionContext? permissionContext)>((cc, permissionContext));
                }
                catch (OdinSecurityException)
                {
                    //if you're no longer connected, we can mark you as authenticated because you still have a client.
                    var (cc, permissionCtx) = CreateAuthenticatedPermissionContext(authToken, client).GetAwaiter().GetResult();
                    return new ValueTask<(CallerContext? callerContext, PermissionContext? permissionContext)>((cc, permissionCtx));
                }
            }

            if (client.ClientType == HomeAppClientType.UnconnectedIdentity)
            {
                var (cc, permissionCtx) = CreateAuthenticatedPermissionContext(authToken, client).GetAwaiter().GetResult();
                return new ValueTask<(CallerContext? callerContext, PermissionContext? permissionContext)>((cc, permissionCtx));
            }

            throw new OdinSecurityException("Unhandled Built-in app client type type");
        }

        private Task<(CallerContext callerContext, PermissionContext permissionContext)> CreateAuthenticatedPermissionContext(
            ClientAuthenticationToken authToken, HomeAppClient client)
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

            var permissionCtx = _exchangeGrantService.CreatePermissionContext(authToken,
                    grants,
                    client.AccessRegistration!,
                    additionalPermissionKeys: permissionKeys, //read_connections
                    includeAnonymousDrives: true,
                    anonymousDrivePermission: anonDrivePermissions)
                .GetAwaiter().GetResult();

            // var token = authToken.AccessTokenHalfKey;
            // var accessKey = client.AccessRegistration?.ClientAccessKeyEncryptedKeyStoreKey.DecryptKeyClone(ref token);
            // var ss = client.AccessRegistration?.AccessKeyStoreKeyEncryptedSharedSecret.DecryptKeyClone(ref accessKey);
            // accessKey?.Wipe();

            // var permissionCtx = new PermissionContext(
            //     new Dictionary<string, PermissionGroup>
            //     {
            //         { "read_anonymous_drives", _exchangeGrantService.CreateAnonymousDrivePermissionGroup().GetAwaiter().GetResult() },
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

            return Task.FromResult((cc, permissionCtx));
        }

        private bool HasValidClientAuthToken(ClientAuthenticationToken authToken, out HomeAppClient? client)
        {
            client = null;

            if (authToken.ClientTokenType != ClientTokenType.BuiltInBrowserApp)
            {
                return false;
            }

            client = _storage.GetClient(authToken.Id);

            if (client == null)
            {
                return false;
            }

            var accessReg = client.AccessRegistration;
            if (accessReg?.IsRevoked ?? true)
            {
                return false;
            }

            accessReg.AssertValidRemoteKey(authToken.AccessTokenHalfKey);
            return true;
        }


        /// <summary>
        /// Creates a YouAuth Client for an Identity that is not connected. (will show as authenticated)
        /// </summary>
        private bool TryCreateAuthenticatedYouAuthClient(string odinId, ClientAuthenticationToken remoteClientAuthToken,
            out ClientAccessToken browserClientAccessToken)
        {
            var emptyKey = Guid.Empty.ToByteArray().ToSensitiveByteArray();
            browserClientAccessToken = StoreClient((OdinId)odinId, emptyKey, HomeAppClientType.UnconnectedIdentity);
            return true;
        }

        private Task<bool> TryCreateIdentityConnectionClient(string odinId, ClientAuthenticationToken remoteClientAuthToken,
            out ClientAccessToken? browserClientAccessToken)
        {
            var icr = _circleNetworkService.GetIdentityConnectionRegistration(new OdinId(odinId), remoteClientAuthToken).GetAwaiter().GetResult();

            if (!icr.IsConnected())
            {
                browserClientAccessToken = null;
                return Task.FromResult(false);
            }

            var (grantKeyStoreKey, sharedSecret) = icr.AccessGrant.AccessRegistration.DecryptUsingClientAuthenticationToken(remoteClientAuthToken);
            sharedSecret.Wipe();

            browserClientAccessToken = StoreClient(icr.OdinId, grantKeyStoreKey, HomeAppClientType.ConnectedIdentity);

            return Task.FromResult(true);
        }

        private ClientAccessToken StoreClient(OdinId odinId, SensitiveByteArray grantKeyStoreKey, HomeAppClientType clientType)
        {
            var (accessRegistration, cat) = _exchangeGrantService.CreateClientAccessToken(
                grantKeyStoreKey, ClientTokenType.BuiltInBrowserApp).GetAwaiter().GetResult();

            grantKeyStoreKey.Wipe();

            var homeAppClient = new HomeAppClient(odinId, accessRegistration, clientType);
            _storage.SaveClient(homeAppClient);

            return cat;
        }


        /// <summary>
        /// Creates a caller and permission context for the caller based on the <see cref="IdentityConnectionRegistrationClient"/> resolved by the authToken
        /// </summary>
        private async Task<(CallerContext callerContext, PermissionContext permissionContext)> CreateConnectedPermissionContext(
            ClientAuthenticationToken authToken)
        {
            var client = _storage.GetClient(authToken.Id);
            if (client?.AccessRegistration == null)
            {
                throw new OdinSecurityException("Invalid auth token or invalid client access registration");
            }

            client.AccessRegistration.AssertValidRemoteKey(authToken.AccessTokenHalfKey);

            //TODO: need to remove the override hack method below and support passing in the auth token from an icr client
            var icr = await _circleNetworkService.GetIdentityConnectionRegistration(client.OdinId, true);
            bool isAuthenticated = icr.AccessGrant?.IsValid() ?? false;
            bool isConnected = icr.IsConnected();

            // Only return the permissions if the identity is connected.
            if (isAuthenticated && isConnected)
            {
                var (permissionContext, enabledCircles) = await CreatePermissionContextCore(
                    icr: icr,
                    accessReg: client.AccessRegistration,
                    authToken: authToken);

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

                return (cc, permissionContext);
            }

            throw new OdinSecurityException("Invalid auth token");
        }
    }
}