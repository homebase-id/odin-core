using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
using Youverse.Core.Services.Authorization.Exchange;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
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

        public async Task Invoke(HttpContext httpContext, DotYouContext dotYouContext, IYouAuthSessionManager youAuthSessionManager)
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
                await LoadYouAuthContext(httpContext, dotYouContext, youAuthSessionManager);
                await _next(httpContext);
                return;
            }

            if (authType == TransitPerimeterAuthConstants.TransitAuthScheme)
            {
                await LoadTransitContext(httpContext, dotYouContext);
            }

            await _next(httpContext);
        }

        private async Task LoadOwnerContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            var user = httpContext.User;

            var authService = httpContext.RequestServices.GetRequiredService<IOwnerAuthenticationService>();
            var authResult = DotYouAuthenticationResult.Parse(user.FindFirstValue(DotYouClaimTypes.AuthResult));
            var (masterKey, clientSharedSecret) = await authService.GetMasterKey(authResult.SessionToken, authResult.ClientHalfKek);

            dotYouContext.Caller = new CallerContext(
                authContext: OwnerAuthConstants.SchemeName,
                dotYouId: (DotYouIdentity) user.Identity!.Name,
                isOwner: true,
                masterKey: masterKey
            );

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

        private async Task<AppContextBase> EnsureSystemAppsOrFail(Guid appId, HttpContext httpContext)
        {
            //HACK: this method should be removed when correct provisioning is in place
            var appRegSvc = httpContext.RequestServices.GetRequiredService<IAppRegistrationService>();
            var ctxBase = await appRegSvc.GetAppContextBase(appId, true);

            if (null == ctxBase)
            {
                if (appId == SystemAppConstants.ChatAppId || appId == SystemAppConstants.ProfileAppId || appId == SystemAppConstants.WebHomeAppId)
                {
                    var provService = httpContext.RequestServices.GetRequiredService<IIdentityProvisioner>();
                    await provService.EnsureSystemApps();
                    ctxBase = await appRegSvc.GetAppContextBase(appId, true);
                }
                else
                {
                    throw new YouverseSecurityException("App is invalid");
                }
            }

            return ctxBase;
        }

        private async Task LoadAppContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            var appRegSvc = httpContext.RequestServices.GetRequiredService<IAppRegistrationService>();

            var value = httpContext.Request.Cookies[AppAuthConstants.CookieName];
            var authResult = DotYouAuthenticationResult.Parse(value);
            var user = httpContext.User;

            dotYouContext.Caller = new CallerContext(
                authContext: AppAuthConstants.SchemeName,
                dotYouId: (DotYouIdentity) user.Identity!.Name,
                isOwner: user.HasClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower()),
                masterKey: null // Note: we're logged in using an app token so we do not have the master key
            );

            //**** HERE I DO NOT HAVE THE MASTER KEY - because we are logged in using an app token ****
            var appCtx = await appRegSvc.GetAppContext(authResult.SessionToken, authResult.ClientHalfKek);
            dotYouContext.AppContext = appCtx;

            var permissionGrants = new Dictionary<SystemApiPermissionType, int>();

            if (appCtx.CanManageConnections)
            {
                permissionGrants.Add(SystemApiPermissionType.CircleNetwork, (int) CircleNetworkPermissions.Manage);
                permissionGrants.Add(SystemApiPermissionType.CircleNetworkRequests, (int) CircleNetworkRequestPermissions.Manage);
            }

            dotYouContext.SetPermissionContext(new PermissionContext(MapAppDriveGrants(appCtx.OwnedDrives), permissionGrants, dotYouContext.AppContext.GetAppKey()));
        }

        private async Task LoadYouAuthContext(HttpContext httpContext, DotYouContext dotYouContext, IYouAuthSessionManager youAuthSessionManager)
        {
            var user = httpContext.User;

            dotYouContext.Caller = new CallerContext(
                authContext: YouAuthConstants.Scheme,
                dotYouId: (DotYouIdentity) user.Identity!.Name,
                isOwner: user.HasClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower()),
                masterKey: null // Note: we're logged in using an app token so we do not have the master key
            );


            //TODO: build app context from xtoken; how do i know which appid?
            var appRegSvc = httpContext.RequestServices.GetRequiredService<IAppRegistrationService>();

            //HACK: so this needs to be appid that is aligned with the drives; should it be the profile?
            var appId =  SystemAppConstants.ProfileAppId;
            var appCtx = await appRegSvc.GetAppContextBase(appId, false);
            dotYouContext.AppContext = appCtx;

            var (xtoken, remoteGrantKey) = await GetXTokenFromSession(httpContext, youAuthSessionManager);
            if (xtoken is {IsRevoked: false})
            {
                var dk = xtoken.HalfKeyEncryptedDriveGrantKey.DecryptKeyClone(ref remoteGrantKey);

                var driveGrants = xtoken.DriveGrants.Select(dg => new PermissionDriveGrant()
                {
                    DriveId = dg.DriveIdentifier,
                    EncryptedStorageKey = dg.XTokenEncryptedStorageKey,
                    Permissions = DrivePermissions.Read
                }).ToList();

                dotYouContext.SetPermissionContext(new PermissionContext(driveGrants, null, dk));
            }
        }

        private async Task LoadTransitContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            var user = httpContext.User;

            dotYouContext.Caller = new CallerContext(
                authContext: TransitPerimeterAuthConstants.TransitAuthScheme,
                dotYouId: (DotYouIdentity) user.Identity!.Name,
                isOwner: user.HasClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower()),
                masterKey: null // Note: we're logged in using a transit certificate so we do not have the master key
            );

            var permissionGrants = new Dictionary<SystemApiPermissionType, int>();

            IEnumerable<PermissionDriveGrant> driveGrants = null;
            SensitiveByteArray driveDecryptionKey = null;

            //Note: transit context may or may not have an app.  The need for an app is
            //enforced by auth policy on the endpoint as well as the calling code
            if (Guid.TryParse(user.FindFirstValue(DotYouClaimTypes.AppId), out var appId))
            {
                var appRegSvc = httpContext.RequestServices.GetRequiredService<IAppRegistrationService>();
                var appCtx = await appRegSvc.GetAppContextBase(appId);
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

        private async Task<(ExchangeRegistration, SensitiveByteArray)> GetXTokenFromSession(HttpContext httpContext, IYouAuthSessionManager youAuthSessionManager)
        {
            var remoteGrantKeyValue = httpContext.Request.Cookies[YouAuthDefaults.XTokenCookieName];
            if (!string.IsNullOrWhiteSpace(remoteGrantKeyValue))
            {
                var remoteGrantKey = Convert.FromBase64String(remoteGrantKeyValue);
                if (remoteGrantKey?.Length > 0)
                {
                    var sessionId = Guid.Parse(httpContext.Request.Cookies[YouAuthDefaults.SessionCookieName] ?? throw new YouverseSecurityException("Missing session"));
                    var session = await youAuthSessionManager.LoadFromId(sessionId);
                    return (session?.ExchangeRegistration, remoteGrantKey.ToSensitiveByteArray());
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