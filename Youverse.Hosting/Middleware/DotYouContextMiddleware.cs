using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core;
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
using AppContext = Youverse.Core.Services.Base.AppContext;

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

            if (authType == YouAuthConstants.Scheme)
            {
                await LoadYouAuthContext(httpContext, dotYouContext);
                dotYouContext.AuthContext = YouAuthConstants.Scheme;

                await _next(httpContext);
                return;
            }

            if (authType == TransitPerimeterAuthConstants.TransitAuthScheme)
            {
                await LoadTransitContext(httpContext, dotYouContext);
                dotYouContext.AuthContext = TransitPerimeterAuthConstants.TransitAuthScheme;
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
                dotYouId: (DotYouIdentity) user.Identity!.Name,
                isOwner: true,
                masterKey: masterKey,
                authContext: OwnerAuthConstants.SchemeName,
                isAnonymous: false);

            var permissionSet = new PermissionSet();
            permissionSet.Permissions.Add(SystemApiPermissionType.CircleNetwork, (int) CircleNetworkPermissions.Manage);
            permissionSet.Permissions.Add(SystemApiPermissionType.CircleNetworkRequests, (int) CircleNetworkRequestPermissions.Manage);

            var allDrives = await driveService.GetDrives(PageOptions.All);
            var allDriveGrants = allDrives.Results.Select(d => new DriveGrant()
            {
                DriveId = d.Id,
                DriveAlias = d.Alias,
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

            //TODO: we need to decide on how appid works for owner console
            string appIdValue = httpContext.Request.Headers[DotYouHeaderNames.AppId];
            Guid.TryParse(appIdValue, out var appId);

            dotYouContext.AppContext = new OwnerAppContext(appId, "", masterKey, clientSharedSecret);
        }

        private async Task LoadAppContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            var appRegSvc = httpContext.RequestServices.GetRequiredService<IAppRegistrationService>();
            var exchangeGrantContextService = httpContext.RequestServices.GetRequiredService<ExchangeGrantContextService>();

            var value = httpContext.Request.Cookies[AppAuthConstants.ClientAuthTokenCookieName];
            var authToken = ClientAuthenticationToken.Parse(value);
            var user = httpContext.User;

            dotYouContext.Caller = new CallerContext(
                dotYouId: (DotYouIdentity) user.Identity!.Name,
                isOwner: user.HasClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower()),
                masterKey: null,
                authContext: AppAuthConstants.SchemeName, // Note: we're logged in using an app token so we do not have the master key
                isAnonymous: false
            );

            //TODO: need to check the app reg negated permission set

            var permissionContext = await exchangeGrantContextService.GetContext(authToken);
            dotYouContext.SetPermissionContext(permissionContext);

            var appReg = await appRegSvc.GetAppRegistrationByGrant(permissionContext.ExchangeGrantId);
            dotYouContext.AppContext = new AppContext(appReg.ApplicationId, appReg.Name, permissionContext.SharedSecretKey);
        }

        private async Task LoadYouAuthContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            var user = httpContext.User;
            var appRegSvc = httpContext.RequestServices.GetRequiredService<IAppRegistrationService>();
            var exchangeGrantContextService = httpContext.RequestServices.GetRequiredService<ExchangeGrantContextService>();

            var callerDotYouId = (DotYouIdentity) user.Identity!.Name;

            bool isAnonymous = user.HasClaim(YouAuthDefaults.IdentityClaim, YouAuthDefaults.AnonymousIdentifier);
            bool isInNetwork = user.HasClaim(DotYouClaimTypes.IsInNetwork, bool.TrueString.ToLower());

            dotYouContext.Caller = new CallerContext(
                dotYouId: callerDotYouId,
                isOwner: false, //NOTE: owner can NEVER login as the owner via YouAuth
                masterKey: null,
                authContext: YouAuthConstants.Scheme,
                isInYouverseNetwork: isInNetwork,
                isAnonymous: isAnonymous,
                isConnected: false
            );

            if (isAnonymous)
            {
                var driveService = httpContext.RequestServices.GetRequiredService<IDriveService>();
                var anonymousDrives = await driveService.GetAnonymousDrives(PageOptions.All);
                var grants = anonymousDrives.Results.Select(d => new DriveGrant()
                {
                    DriveId = d.Id,
                    DriveAlias = d.Alias,
                    KeyStoreKeyEncryptedStorageKey = d.MasterKeyEncryptedStorageKey,
                    Permissions = DrivePermissions.All
                });

                //HACK: granting ability to see friends list to anon users.
                var permissionSet = new PermissionSet();
                permissionSet.Permissions.Add(SystemApiPermissionType.CircleNetwork, (int) CircleNetworkPermissions.Read);

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
            var clientAuthToken = GetClientAuthToken(httpContext);
            if (null != clientAuthToken)
            {
                var permissionContext = await exchangeGrantContextService.GetContext(clientAuthToken);
                dotYouContext.SetPermissionContext(permissionContext);

                //this part gets weird.  what if they're coming via a specific app. do i load that app or do i
                //load the one associated with the exchange grant?
                // and what if the exchange grant has no appid?   which takes priority

                //var appReg = await appRegSvc.GetAppRegistrationByGrant(permissionContext.ExchangeGrantId);
                if(Guid.TryParse(httpContext.Request.Headers[DotYouHeaderNames.AppId], out Guid appId))
                {
                    var appReg = await appRegSvc.GetAppRegistration(appId);
                    dotYouContext.AppContext = new AppContext(appReg.ApplicationId, appReg.Name, permissionContext.SharedSecretKey);
                }
            }
        }

        private async Task LoadTransitContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            var user = httpContext.User;
            var appRegSvc = httpContext.RequestServices.GetRequiredService<IAppRegistrationService>();
            var exchangeGrantContextService = httpContext.RequestServices.GetRequiredService<ExchangeGrantContextService>();
            var circleNetworkService = httpContext.RequestServices.GetRequiredService<ICircleNetworkService>();

            var callerDotYouId = (DotYouIdentity) user.Identity!.Name;

            //todo: the appid is coming in from a header which is set on a claim.  you might have this app id but not the client auth token

            //actually, you need both the client auth token and app 

            //so in the case of requesting an transit public key, there is not client auth token but there is an app id.  this means 
            // I can request a transit public key w/o being connected?  i guess that makes sense 

            var appIdClaim = user.FindFirst(DotYouClaimTypes.AppId)?.Value;
            if (null == appIdClaim)
            {
                dotYouContext.Caller = new CallerContext(
                    dotYouId: callerDotYouId,
                    isOwner: false,
                    masterKey: null,
                    authContext: TransitPerimeterAuthConstants.TransitAuthScheme, // Note: we're logged in using a transit certificate so we do not have the master key
                    isAnonymous: false,
                    isConnected: false,
                    isInYouverseNetwork: true //note: this will need to come from a claim: re: best buy/3rd party scenario
                );

                /*
                 * if no app is specified then we're looking at one of the special requests
                 *  Specials -
                    *      Connection Requests Management
                    *          give one permission to add a request
                    *          give no drive permission
                    *      Indirect introduction
                 * * transit public key request (offline)
                */
            }
            else
            {
                var failMessage = "Missing or revoked appId specified";
                if (!Guid.TryParse(appIdClaim, out var appId) || appId == Guid.Empty)
                {
                    throw new YouverseSecurityException(failMessage);
                }

                var appReg = await appRegSvc.GetAppRegistration(appId);
                if (appReg is {IsRevoked: true})
                {
                    throw new YouverseSecurityException(failMessage);
                }

                //TODO: reduce the permissions based on the app registration's negated permission set

                SensitiveByteArray sharedSecret = null;

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

                    dotYouContext.Caller = new CallerContext(
                        dotYouId: callerDotYouId,
                        isOwner: false,
                        masterKey: null,
                        authContext: TransitPerimeterAuthConstants.TransitAuthScheme, // Note: we're logged in using a transit certificate so we do not have the master key
                        isAnonymous: false,
                        isConnected: true,
                        isInYouverseNetwork: true //note: this will need to come from a claim: re: best buy/3rd party scenario
                    );

                    // if they are connected, we can load the permissions from there.
                    var permissionContext = await exchangeGrantContextService.GetContext(clientAuthToken);
                    dotYouContext.SetPermissionContext(permissionContext);
                    sharedSecret = permissionContext.SharedSecretKey;
                }

                dotYouContext.AppContext = new AppContext(appReg.ApplicationId, appReg.Name, sharedSecret);
            }

            //From here: if they are not connected then we must examine if this is a special case
            // - incoming connection request
            // - incoming message request
            // - getting app transit key
            // - or??
        }

        private ClientAuthenticationToken GetClientAuthToken(HttpContext httpContext)
        {
            var clientAccessTokenValue64 = httpContext.Request.Cookies[YouAuthDefaults.XTokenCookieName];
            if (ClientAuthenticationToken.TryParse(clientAccessTokenValue64, out var token))
            {
                return token;
            }

            return null;
        }
    }
}