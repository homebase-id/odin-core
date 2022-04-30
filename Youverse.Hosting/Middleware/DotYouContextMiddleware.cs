﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography.Xml;
using System.Threading.Tasks;
using Dawn;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Cryptography;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authentication.Owner;
using Youverse.Core.Services.Authentication.YouAuth;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Registry;
using Youverse.Core.Services.Registry.Provisioning;
using Youverse.Core.Services.Tenant;
using Youverse.Hosting.Authentication.App;
using Youverse.Hosting.Authentication.Owner;
using Youverse.Hosting.Authentication.TransitPerimeter;
using Youverse.Hosting.Authentication.YouAuth;

namespace Youverse.Hosting.Middleware
{
    public class DotYouContextMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IIdentityContextRegistry _registry;
        private readonly ITenantProvider _tenantProvider;


        public DotYouContextMiddleware(RequestDelegate next, IIdentityContextRegistry registry, ITenantProvider tenantProvider)
        {
            _next = next;
            _registry = registry;
            _tenantProvider = tenantProvider;
        }

        public async Task Invoke(HttpContext httpContext, DotYouContext dotYouContext, IYouAuthSessionManager youAuthSessionManager, IDriveService driveService, ICircleNetworkService circleNetworkService, ExchangeGrantService exchangeGrantService)
        {
            var tenant = _tenantProvider.GetCurrentTenant();
            string authType = httpContext.User.Identity?.AuthenticationType;

            if (tenant?.Name == null || string.IsNullOrEmpty(authType))
            {
                await _next(httpContext);
                return;
            }

            if (authType == OwnerAuthConstants.SchemeName)
            {
                await LoadOwnerContext(httpContext, dotYouContext);
                await _next(httpContext);
                return;
            }

            if (authType == AppAuthConstants.SchemeName)
            {
                await LoadAppContext(httpContext, dotYouContext);
                await _next(httpContext);
                return;
            }

            if (authType == YouAuthConstants.Scheme)
            {
                await LoadYouAuthContext(httpContext, dotYouContext, youAuthSessionManager, driveService, circleNetworkService, exchangeGrantService);
                await _next(httpContext);
                return;
            }

            if (authType == TransitPerimeterAuthConstants.TransitAuthScheme)
            {
                await LoadTransitContext(httpContext, dotYouContext);
            }

            await _next(httpContext);
        }

        private static object _sysapps = new object();

        private async Task<AppContextBase> EnsureSystemAppsOrFail(Guid appId, HttpContext httpContext)
        {
            lock (_sysapps)
            {
                //HACK: this method should be removed when correct provisioning is in place
                var appRegSvc = httpContext.RequestServices.GetRequiredService<IAppRegistrationService>();
                var ctxBase = appRegSvc.GetAppContextBase(appId, true).GetAwaiter().GetResult();

                if (null == ctxBase)
                {
                    if (appId == SystemAppConstants.ChatAppId || appId == SystemAppConstants.ProfileAppId || appId == SystemAppConstants.WebHomeAppId)
                    {
                        var provService = httpContext.RequestServices.GetRequiredService<IIdentityProvisioner>();
                        provService.EnsureSystemApps().GetAwaiter().GetResult();
                        ctxBase = appRegSvc.GetAppContextBase(appId, true).GetAwaiter().GetResult();
                    }
                    else
                    {
                        throw new YouverseSecurityException("App is invalid");
                    }
                }

                return ctxBase;
            }
        }

        private async Task LoadOwnerContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            var user = httpContext.User;

            var authService = httpContext.RequestServices.GetRequiredService<IOwnerAuthenticationService>();
            var authResult = DotYouAuthenticationResult.Parse(user.FindFirstValue(DotYouClaimTypes.AuthResult));
            var (masterKey, clientSharedSecret) = await authService.GetMasterKey(authResult.SessionToken, authResult.ClientHalfKek);

            dotYouContext.Caller = new CallerContext(dotYouId: (DotYouIdentity) user.Identity!.Name,
                isOwner: true,
                masterKey: masterKey, authContext: OwnerAuthConstants.SchemeName,
                isAnonymous: false);

            var appIdValue = httpContext.Request.Headers[DotYouHeaderNames.AppId];
            if (string.IsNullOrEmpty(appIdValue))
            {
                //TODO: Need to sort out owner permissions?  is this just everything?
                var permissionGrants = new Dictionary<SystemApiPermissionType, int>();
                permissionGrants.Add(SystemApiPermissionType.CircleNetwork, (int) CircleNetworkPermissions.Manage);
                permissionGrants.Add(SystemApiPermissionType.CircleNetworkRequests, (int) CircleNetworkRequestPermissions.Manage);

                dotYouContext.SetPermissionContext(new PermissionContext(null, permissionGrants, null));
            }
            else
            {
                Guard.Argument(appIdValue, DotYouHeaderNames.AppId).Require(Guid.TryParse(appIdValue, out var appId), v => "If appId specified, it must be a valid Guid");
                var ctxBase = await this.EnsureSystemAppsOrFail(appId, httpContext);

                var appCtx = new OwnerAppContext(
                    appId: appId,
                    appClientId: authResult.SessionToken,
                    clientSharedSecret: clientSharedSecret,
                    defaultDriveId: ctxBase.DefaultDriveId.GetValueOrDefault(),
                    masterKeyEncryptedAppKey: ctxBase.MasterKeyEncryptedAppKey,
                    ownedDrives: ctxBase.OwnedDrives,
                    canManageConnections: true,
                    masterKey: masterKey);

                dotYouContext.AppContext = appCtx;

                var permissionGrants = new Dictionary<SystemApiPermissionType, int>();
                if (appCtx.CanManageConnections)
                {
                    permissionGrants.Add(SystemApiPermissionType.CircleNetwork, (int) CircleNetworkPermissions.Manage);
                    permissionGrants.Add(SystemApiPermissionType.CircleNetworkRequests, (int) CircleNetworkRequestPermissions.Manage);
                }

                var driveGrants = MapAppDriveGrants(appCtx.OwnedDrives);
                dotYouContext.SetPermissionContext(new PermissionContext(driveGrants, permissionGrants, dotYouContext.AppContext.GetAppKey()));
            }
        }

        private async Task LoadAppContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            var appRegSvc = httpContext.RequestServices.GetRequiredService<IAppRegistrationService>();

            var value = httpContext.Request.Cookies[AppAuthConstants.CookieName];
            var authResult = DotYouAuthenticationResult.Parse(value);
            var user = httpContext.User;

            dotYouContext.Caller = new CallerContext(dotYouId: (DotYouIdentity) user.Identity!.Name,
                isOwner: user.HasClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower()),
                masterKey: null,
                authContext: AppAuthConstants.SchemeName, // Note: we're logged in using an app token so we do not have the master key
                isAnonymous: false
            );

            //**** HERE I DO NOT HAVE THE MASTER KEY - because we are logged in using an app token ****
            var appCtx = await appRegSvc.GetAppContext(authResult.SessionToken, authResult.ClientHalfKek, true);
            dotYouContext.AppContext = appCtx;

            var permissionGrants = new Dictionary<SystemApiPermissionType, int>();

            if (appCtx.CanManageConnections)
            {
                permissionGrants.Add(SystemApiPermissionType.CircleNetwork, (int) CircleNetworkPermissions.Manage);
                permissionGrants.Add(SystemApiPermissionType.CircleNetworkRequests, (int) CircleNetworkRequestPermissions.Manage);
            }

            dotYouContext.SetPermissionContext(new PermissionContext(MapAppDriveGrants(appCtx.OwnedDrives), permissionGrants, dotYouContext.AppContext.GetAppKey()));
        }

        private async Task LoadYouAuthContext(HttpContext httpContext, DotYouContext dotYouContext, IYouAuthSessionManager youAuthSessionManager, IDriveService driveService, ICircleNetworkService circleNetworkService, ExchangeGrantService exchangeGrantService)
        {
            var user = httpContext.User;

            //HACK: need to determine how we can see if the subject of the session is in the youverse network
            Guid.TryParse(httpContext.Request.Cookies[YouAuthDefaults.SessionCookieName] ?? "", out var sessionId);
            var session = await youAuthSessionManager.LoadFromId(sessionId);

            bool isAnonymous = user.HasClaim(YouAuthDefaults.IdentityClaim, YouAuthDefaults.AnonymousIdentifier);
            bool isInNetwork = isAnonymous == false && session != null; //HACK: used for testing but invalid way to determine if someone is in network

            dotYouContext.Caller = new CallerContext(
                authContext: YouAuthConstants.Scheme,
                dotYouId: (DotYouIdentity) user.Identity!.Name,
                isOwner: false, //NOTE: owner can never login as the owner via YouAuth
                masterKey: null,
                isInYouverseNetwork: isInNetwork,
                isAnonymous: isAnonymous
            );

            // Note: removing appid since we have the exchange drive grant
            // var appIdValue = httpContext.Request.Headers[DotYouHeaderNames.AppId];
            // if (string.IsNullOrEmpty(appIdValue))
            // {
            //     //grant nothing extra if no app is specified
            //     return;
            // }
            //
            // var appRegSvc = httpContext.RequestServices.GetRequiredService<IAppRegistrationService>();
            // var appId = Guid.Parse(appIdValue);
            // var appCtx = await appRegSvc.GetAppContextBase(appId, false, true);
            //
            // if (appCtx == null)
            // {
            //     throw new YouverseSecurityException("Invalid App");
            // }

            dotYouContext.AppContext = null;
            // var allGrants = MapAppDriveGrants(appCtx.OwnedDrives);
            //
            // var anonDriveGrants = allGrants.Where(grant =>
            //     {
            //         var drive = driveService.GetDrive(grant.DriveId, true).GetAwaiter().GetResult();
            //         return drive.AllowAnonymousReads;
            //     })
            //     .Select(pdg => //reduce to read only
            //     {
            //         pdg.Permissions = DrivePermissions.Read;
            //         return pdg;
            //     }).ToList();

            // dotYouContext.SetPermissionContext(new PermissionContext(anonDriveGrants, null, null));

            //here, we need to determine if the caller has additional access via an ExchangeGrant.  This will be stored on the session
            // we need back the drives (and later permissions) to which the caller has access
            var (keyStoreKey, drives) = await GetExchangeGrantRegistration(httpContext, youAuthSessionManager, exchangeGrantService);
            
            //     //TODO: hit the circle membership service to validate the circle still has the app AND the user is a member of it?
            //     //here we can load the actual drive id for now but really should move these to the drive service, deep inside
            List<PermissionDriveGrant> driveGrants = new List<PermissionDriveGrant>();
            
            foreach (var dg in drives)
            {
                driveGrants.Add(new PermissionDriveGrant()
                {
                    DriveId = dg.DriveId,
                    EncryptedStorageKey = dg.KeyStoreKeyEncryptedStorageKey,
                    Permissions = dg.Permissions
                });
            }

            dotYouContext.SetPermissionContext(new PermissionContext(driveGrants, null, keyStoreKey));
        }

        private async Task LoadTransitContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            var user = httpContext.User;

            dotYouContext.Caller = new CallerContext(dotYouId: (DotYouIdentity) user.Identity!.Name,
                isOwner: user.HasClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower()),
                masterKey: null,
                authContext: TransitPerimeterAuthConstants.TransitAuthScheme, // Note: we're logged in using a transit certificate so we do not have the master key
                isAnonymous: false
            );

            var permissionGrants = new Dictionary<SystemApiPermissionType, int>();

            IEnumerable<PermissionDriveGrant> driveGrants = null;
            SensitiveByteArray driveDecryptionKey = null;

            //Note: transit context may or may not have an app.  The need for an app is
            //enforced by auth policy on the endpoint as well as the calling code
            if (Guid.TryParse(user.FindFirstValue(DotYouClaimTypes.AppId), out var appId))
            {
                var appRegSvc = httpContext.RequestServices.GetRequiredService<IAppRegistrationService>();
                var appCtx = await appRegSvc.GetAppContextBase(appId, false, true);


                dotYouContext.AppContext = appCtx;

                driveGrants = MapAppDriveGrants(appCtx.OwnedDrives);
                driveDecryptionKey = appCtx.GetAppKey();
                if (appCtx.CanManageConnections)
                {
                    permissionGrants.Add(SystemApiPermissionType.CircleNetwork, (int) CircleNetworkPermissions.Manage);
                    permissionGrants.Add(SystemApiPermissionType.CircleNetwork, (int) CircleNetworkRequestPermissions.Manage);
                }
            }

            dotYouContext.SetPermissionContext(new PermissionContext(driveGrants, permissionGrants, driveDecryptionKey));
        }

        private async Task<(SensitiveByteArray, List<DriveGrant>)> GetExchangeGrantRegistration(HttpContext httpContext, IYouAuthSessionManager youAuthSessionManager, ExchangeGrantService exchangeGrantService)
        {
            var clientAccessTokenValue64 = httpContext.Request.Cookies[YouAuthDefaults.XTokenCookieName];
            if (!string.IsNullOrWhiteSpace(clientAccessTokenValue64))
            {
                var combined = Convert.FromBase64String(clientAccessTokenValue64);
                if (combined?.Length > 0)
                {
                    var (accessRegistrationIdBytes, accessTokenHalfKey) = ByteArrayUtil.Split(combined, 16, 16);
                    var accessRegistrationId = new Guid(accessRegistrationIdBytes);
                    
                    var (keyStoreKey, drives) = await exchangeGrantService.GetDrivesFromValidatedAccessRegistration(accessRegistrationId, accessTokenHalfKey.ToSensitiveByteArray());
                    return (keyStoreKey, drives);
                }
            }

            return (null, null);
        }

        private IEnumerable<PermissionDriveGrant> MapAppDriveGrants(IEnumerable<AppDriveGrant> appDriveGrants)
        {
            var driveGrants = appDriveGrants.Select(dg => new PermissionDriveGrant()
            {
                DriveId = dg.DriveId,
                EncryptedStorageKey = dg.AppKeyEncryptedStorageKey,
                Permissions = dg.Permissions
            }).ToList();

            return driveGrants;
        }
    }
}