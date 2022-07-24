using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication.Owner;
using Youverse.Core.Services.Authentication.YouAuth;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Tenant;
using Youverse.Hosting.Authentication.ClientToken;
using Youverse.Hosting.Authentication.Owner;
using Youverse.Hosting.Authentication.Perimeter;
using AppContext = Youverse.Core.Services.Base.AppContext;

namespace Youverse.Hosting.Middleware
{
    public class DotYouContextMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ITenantProvider _tenantProvider;

        public DotYouContextMiddleware(RequestDelegate next, ITenantProvider tenantProvider)
        {
            _next = next;
            _tenantProvider = tenantProvider;
        }

        public async Task Invoke(HttpContext httpContext, DotYouContext dotYouContext)
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
                dotYouContext.AuthContext = OwnerAuthConstants.SchemeName;

                await _next(httpContext);
                return;
            }

            if (authType == AppAuthConstants.SchemeName)
            {
                await LoadAppContext(httpContext, dotYouContext);
                dotYouContext.AuthContext = AppAuthConstants.SchemeName;

                await _next(httpContext);
                return;
            }

            if (authType == ClientTokenConstants.Scheme)
            {
                await LoadYouAuthContext(httpContext, dotYouContext);
                dotYouContext.AuthContext = ClientTokenConstants.Scheme;

                await _next(httpContext);
                return;
            }

            if (authType == PerimeterAuthConstants.TransitCertificateAuthScheme)
            {
                await LoadTransitContext(httpContext, dotYouContext);
                dotYouContext.AuthContext = PerimeterAuthConstants.TransitCertificateAuthScheme;

                await _next(httpContext);
                return;
            }

            if (authType == PerimeterAuthConstants.PublicTransitAuthScheme)
            {
                await LoadPublicTransitContext(httpContext, dotYouContext);
                dotYouContext.AuthContext = PerimeterAuthConstants.PublicTransitAuthScheme;

                await _next(httpContext);
                return;
            }


            await _next(httpContext);
        }

        private async Task LoadOwnerContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            var user = httpContext.User;

            var driveService = httpContext.RequestServices.GetRequiredService<IDriveService>();
            var authService = httpContext.RequestServices.GetRequiredService<IOwnerAuthenticationService>();
            var authResult = ClientAuthenticationToken.Parse(user.FindFirstValue(DotYouClaimTypes.AuthResult));
            var (masterKey, clientSharedSecret) = await authService.GetMasterKey(authResult.Id, authResult.AccessTokenHalfKey);

            dotYouContext.Caller = new CallerContext(
                dotYouId: (DotYouIdentity)user.Identity!.Name,
                securityLevel: SecurityGroupType.Owner,
                masterKey: masterKey
            );

            var permissionSet = new PermissionSet();
            permissionSet.Permissions.Add(SystemApi.CircleNetwork, (int)CircleNetworkPermissions.Manage);
            permissionSet.Permissions.Add(SystemApi.CircleNetworkRequests, (int)CircleNetworkRequestPermissions.Manage);

            var allDrives = await driveService.GetDrives(PageOptions.All);
            var allDriveGrants = allDrives.Results.Select(d => new DriveGrant()
            {
                DriveId = d.Id,
                DriveAlias = d.Alias,
                DriveType = d.Type,
                KeyStoreKeyEncryptedStorageKey = d.MasterKeyEncryptedStorageKey,
                Permission = DrivePermission.All
            });

            //HACK: giving this the master key makes my hairs raise >:-[
            dotYouContext.SetPermissionContext(
                new PermissionContext(
                    driveGrants: allDriveGrants,
                    permissionSet: permissionSet,
                    driveDecryptionKey: masterKey,
                    sharedSecretKey: clientSharedSecret,
                    isOwner: true
                ));

            //Note: if you've logged in using the owner context, your appid is fixed for the owner console
            dotYouContext.AppContext = new OwnerAppContext(BuiltInAppIdentifiers.OwnerConsole, "Owner Console");
        }

        private async Task LoadAppContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            var appRegSvc = httpContext.RequestServices.GetRequiredService<IAppRegistrationService>();
            var value = httpContext.Request.Cookies[AppAuthConstants.ClientAuthTokenCookieName];
            var authToken = ClientAuthenticationToken.Parse(value);
            var user = httpContext.User;

            dotYouContext.Caller = new CallerContext(
                dotYouId: (DotYouIdentity)user.Identity!.Name,
                securityLevel: SecurityGroupType.Owner,
                masterKey: null
            );

            var (appId, permissionContext) = await appRegSvc.GetPermissionContext(authToken);

            dotYouContext.SetPermissionContext(permissionContext);
            dotYouContext.AppContext = new AppContext(appId, "");
        }

        private async Task LoadYouAuthContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            //TODO: load the circles to which the caller belongs

            var user = httpContext.User;

            var callerDotYouId = (DotYouIdentity)user.Identity!.Name;
            var securityLevel = user.HasClaim(DotYouClaimTypes.IsAuthenticated, bool.TrueString.ToLower())
                ? SecurityGroupType.Authenticated
                : SecurityGroupType.Anonymous;
            
            dotYouContext.Caller = new CallerContext(
                dotYouId: callerDotYouId,
                securityLevel: securityLevel,
                masterKey: null
            );

            if (securityLevel == SecurityGroupType.Anonymous)
            {
                var driveService = httpContext.RequestServices.GetRequiredService<IDriveService>();
                var anonymousDrives = await driveService.GetAnonymousDrives(PageOptions.All);
                var grants = anonymousDrives.Results.Select(d => new DriveGrant()
                {
                    DriveId = d.Id,
                    DriveAlias = d.Alias,
                    DriveType = d.Type,
                    KeyStoreKeyEncryptedStorageKey = d.MasterKeyEncryptedStorageKey,
                    Permission = DrivePermission.Read
                });

                //HACK: granting ability to see friends list to anon users.
                var permissionSet = new PermissionSet();
                permissionSet.Permissions.Add(SystemApi.CircleNetwork, (int)CircleNetworkPermissions.Read);

                dotYouContext.SetPermissionContext(
                    new PermissionContext(
                        driveGrants: grants,
                        permissionSet: permissionSet,
                        driveDecryptionKey: null,
                        sharedSecretKey: null,
                        isOwner: false
                    ));

                return;
            }

            //
            var circleNetworkService = httpContext.RequestServices.GetRequiredService<ICircleNetworkService>();

            //TODO: if we switch session to being and exchange grant then I can use this overload
            var icr = await circleNetworkService.GetIdentityConnectionRegistration(callerDotYouId, true);
            if (icr.IsConnected())
            {
                dotYouContext.Caller.SetIsConnected();
            }

            //if there's a client auth token, let's add the permissions it grants
            if (ClientAuthenticationToken.TryParse(httpContext.Request.Cookies[YouAuthDefaults.XTokenCookieName], out var clientAuthToken))
            {
                var youAuthRegistrationService = httpContext.RequestServices.GetRequiredService<IYouAuthRegistrationService>();
                var permissionContext = await youAuthRegistrationService.GetPermissionContext(clientAuthToken);

                dotYouContext.SetPermissionContext(permissionContext);
            }
        }

        private async Task LoadTransitContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            //TODO: load the circles to which the caller belongs

            var user = httpContext.User;
            var circleNetworkService = httpContext.RequestServices.GetRequiredService<ICircleNetworkService>();

            var callerDotYouId = (DotYouIdentity)user.Identity!.Name;

            dotYouContext.Caller = new CallerContext(
                dotYouId: callerDotYouId,
                securityLevel: SecurityGroupType.Authenticated, //note: this will need to come from a claim: re: best buy/3rd party scenario
                masterKey: null
            );

            if (ClientAuthenticationToken.TryParse(httpContext.Request.Headers[DotYouHeaderNames.ClientAuthToken], out var clientAuthToken))
            {
                var (isConnected, permissionContext) = await circleNetworkService.CreatePermissionContext(callerDotYouId, clientAuthToken);
                if (!isConnected)
                {
                    throw new YouverseSecurityException("Invalid connection");
                }

                dotYouContext.Caller.SetIsConnected();
                dotYouContext.SetPermissionContext(permissionContext);
            }
            else
            {
                dotYouContext.SetPermissionContext(null);
            }
        }

        private Task LoadPublicTransitContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            /*
             * handle these requests only -
             * Connection Requests Management
                give one permission to add a request
                give no drive permission
             * transit public key request (offline)
             */

            var user = httpContext.User;
            var callerDotYouId = (DotYouIdentity)user.Identity!.Name;

            dotYouContext.Caller = new CallerContext(
                dotYouId: callerDotYouId,
                securityLevel: SecurityGroupType.Authenticated, //note: this will need to come from a claim: re: best buy/3rd party scenario
                masterKey: null
            );

            //No permissions allowed
            dotYouContext.SetPermissionContext(null);

            return Task.CompletedTask;
        }
    }
}