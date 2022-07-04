﻿using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Dawn;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core;
using Youverse.Core.Cryptography;
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
using Youverse.Core.Services.Registry.Provisioning;
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

            if (authType == PerimeterAuthConstants.NotificationCertificateAuthScheme)
            {
                await LoadNotificationContext(httpContext, dotYouContext);
                dotYouContext.AuthContext = PerimeterAuthConstants.NotificationCertificateAuthScheme;
            }

            await _next(httpContext);
        }

        private static object _sysapps = new object();

        private Task<AppRegistrationResponse> EnsureSystemAppsOrFail(Guid appId, HttpContext httpContext)
        {
            lock (_sysapps)
            {
                //HACK: this method should be removed when correct provisioning is in place
                var appRegSvc = httpContext.RequestServices.GetRequiredService<IAppRegistrationService>();

                var appReg = appRegSvc.GetAppRegistration(appId).GetAwaiter().GetResult();

                if (null == appReg)
                {
                    if (appId == SystemAppConstants.ChatAppId || appId == SystemAppConstants.ProfileAppId || appId == SystemAppConstants.WebHomeAppId)
                    {
                        var provService = httpContext.RequestServices.GetRequiredService<IIdentityProvisioner>();
                        provService.EnsureSystemApps().GetAwaiter().GetResult();
                        appReg = appRegSvc.GetAppRegistration(appId).GetAwaiter().GetResult();
                    }
                    else
                    {
                        throw new YouverseSecurityException("App is invalid");
                    }
                }

                return Task.FromResult(appReg);
            }
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
                Permissions = DrivePermissions.All
            });

            //HACK: giving this the master key makes my hairs raise >:-[
            dotYouContext.SetPermissionContext(
                new PermissionContext(
                    driveGrants: allDriveGrants,
                    permissionSet: permissionSet,
                    driveDecryptionKey: masterKey,
                    sharedSecretKey: clientSharedSecret,
                    exchangeGrantId: Guid.Empty,
                    accessRegistrationId: Guid.Empty,
                    isOwner: true
                ));

            //Note: if you've logged in using the owner context, your appid is fixed for the owner console
            dotYouContext.AppContext = new OwnerAppContext(BuiltInAppIdentifiers.OwnerConsole, "Owner Console");
        }

        private async Task LoadAppContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            var appRegSvc = httpContext.RequestServices.GetRequiredService<IAppRegistrationService>();
            var exchangeGrantContextService = httpContext.RequestServices.GetRequiredService<ExchangeGrantContextService>();

            var value = httpContext.Request.Cookies[AppAuthConstants.ClientAuthTokenCookieName];
            var authToken = ClientAuthenticationToken.Parse(value);
            var user = httpContext.User;

            dotYouContext.Caller = new CallerContext(
                dotYouId: (DotYouIdentity)user.Identity!.Name,
                securityLevel: SecurityGroupType.Owner,
                masterKey: null
            );

            var permissionContext = await exchangeGrantContextService.GetContext(authToken);
            dotYouContext.SetPermissionContext(permissionContext);

            var appReg = await appRegSvc.GetAppRegistrationByGrant(permissionContext.ExchangeGrantId);
            dotYouContext.AppContext = new AppContext(appReg.ApplicationId, appReg.Name);
        }

        private async Task LoadYouAuthContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            //TODO: load the circles to which the caller belongs

            var user = httpContext.User;

            var callerDotYouId = (DotYouIdentity)user.Identity!.Name;
            bool isInNetwork = user.HasClaim(DotYouClaimTypes.IsInNetwork, bool.TrueString.ToLower());
            var securityLevel = isInNetwork ? SecurityGroupType.Authenticated : SecurityGroupType.Anonymous;
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
                    Permissions = DrivePermissions.All
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
                        exchangeGrantId: Guid.Empty,
                        accessRegistrationId: Guid.Empty,
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
                var exchangeGrantContextService = httpContext.RequestServices.GetRequiredService<ExchangeGrantContextService>();
                var permissionContext = await exchangeGrantContextService.GetYouAuthContext(clientAuthToken);
                dotYouContext.SetPermissionContext(permissionContext);

            }
        }

        private async Task LoadTransitContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            //TODO: load the circles to which the caller belongs

            var user = httpContext.User;
            var exchangeGrantContextService = httpContext.RequestServices.GetRequiredService<ExchangeGrantContextService>();
            var circleNetworkService = httpContext.RequestServices.GetRequiredService<ICircleNetworkService>();

            var callerDotYouId = (DotYouIdentity)user.Identity!.Name;

            dotYouContext.Caller = new CallerContext(
                dotYouId: callerDotYouId,
                securityLevel: SecurityGroupType.Authenticated, //note: this will need to come from a claim: re: best buy/3rd party scenario
                masterKey: null
            );

            if (ClientAuthenticationToken.TryParse(httpContext.Request.Headers[DotYouHeaderNames.ClientAuthToken], out var clientAuthToken))
            {
                //connection must be valid
                var icr = await circleNetworkService.GetIdentityConnectionRegistration(callerDotYouId, clientAuthToken);
                if (icr.IsConnected() == false)
                {
                    throw new YouverseSecurityException("Invalid connection");
                }

                dotYouContext.Caller.SetIsConnected();

                // if they are connected, we can load the permissions from there.
                var permissionContext = await exchangeGrantContextService.GetContext(clientAuthToken);

                dotYouContext.SetPermissionContext(permissionContext);
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

        private async Task LoadNotificationContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            var user = httpContext.User;
            var exchangeGrantContextService = httpContext.RequestServices.GetRequiredService<ExchangeGrantContextService>();
            var circleNetworkService = httpContext.RequestServices.GetRequiredService<ICircleNetworkService>();

            var callerDotYouId = (DotYouIdentity)user.Identity!.Name;

            dotYouContext.Caller = new CallerContext(
                dotYouId: callerDotYouId,
                securityLevel: SecurityGroupType.Authenticated, //note: this will need to come from a claim: re: best buy/3rd party scenario
                masterKey: null
            );

            //the client auth token is coming from one of the following:
            //  1. an Identity Connection Registration; in this case there is no app associated with the CAT.
            //  2. a 3rd party connection; in this case there is an app? (TODO: confirm)
            if (ClientAuthenticationToken.TryParse(httpContext.Request.Headers[DotYouHeaderNames.ClientAuthToken], out var clientAuthToken))
            {
                //connection must be valid
                var icr = await circleNetworkService.GetIdentityConnectionRegistration(callerDotYouId, clientAuthToken);
                if (icr.IsConnected() == false)
                {
                    throw new YouverseSecurityException("Invalid connection");
                }

                dotYouContext.Caller.SetIsConnected();

                // if they are connected, we can load the permissions from there.
                var permissionContext = await exchangeGrantContextService.GetContext(clientAuthToken);
                dotYouContext.SetPermissionContext(permissionContext);
            }
        }
    }
}