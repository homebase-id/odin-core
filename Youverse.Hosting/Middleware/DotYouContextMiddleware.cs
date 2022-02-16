﻿using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Dawn;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authentication.Apps;
using Youverse.Core.Services.Authentication.Owner;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Registry;
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
                //TODO: is there anything special here for youauth?
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
            //var masterKey = await authService.GetMasterKey(authResult.SessionToken, authResult.ClientHalfKek);
            var (masterKey, clientSharedSecret) = await authService.GetMasterKey(authResult.SessionToken, authResult.ClientHalfKek);

            dotYouContext.Caller = new CallerContext(
                dotYouId: (DotYouIdentity) user.Identity.Name,
                isOwner: true,
                masterKey: masterKey
            );

            var appIdValue = httpContext.Request.Headers[DotYouHeaderNames.AppId];
            if (!string.IsNullOrEmpty(appIdValue))
            {
                Guard.Argument(appIdValue, DotYouHeaderNames.AppId).Require(Guid.TryParse(appIdValue, out var appId), v => "If appId specified, it must be a valid Guid");
                var appRegSvc = httpContext.RequestServices.GetRequiredService<IAppRegistrationService>();

                var ctxBase = await appRegSvc.GetAppContextBase(appId, true);

                dotYouContext.AppContext = new OwnerAppContext(
                    appId: appId,
                    appClientId: authResult.SessionToken,
                    clientSharedSecret: clientSharedSecret,
                    driveId: ctxBase.DriveId.GetValueOrDefault(),
                    masterKeyEncryptedAppKey: ctxBase.MasterKeyEncryptedAppKey,
                    driveGrants: ctxBase.DriveGrants,
                    canManageConnections: ctxBase.CanManageConnections,
                    masterKey: masterKey);
            }
        }

        private async Task LoadAppContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            var authService = httpContext.RequestServices.GetRequiredService<IAppAuthenticationService>();
            var appRegSvc = httpContext.RequestServices.GetRequiredService<IAppRegistrationService>();

            var value = httpContext.Request.Cookies[AppAuthConstants.CookieName];
            var authResult = DotYouAuthenticationResult.Parse(value);
            var user = httpContext.User;

            dotYouContext.Caller = new CallerContext(
                dotYouId: (DotYouIdentity) user.Identity.Name,
                isOwner: user.HasClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower()),
                masterKey: null // Note: we're logged in using an app token so we do not have the master key
            );

            //**** HERE I DO NOT HAVE THE MASTER KEY - because we are logged in using an app token ****

            dotYouContext.AppContext = await appRegSvc.GetAppContext(authResult.SessionToken, authResult.ClientHalfKek);
        }

        private async Task LoadTransitContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            var user = httpContext.User;
            
            dotYouContext.Caller = new CallerContext(
                dotYouId: (DotYouIdentity) user.Identity.Name,
                isOwner: user.HasClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower()),
                masterKey: null // Note: we're logged in using a transit certificate so we do not have the master key
            );
            
            //Note: transit context may or may not have an app.  The need for an app is enforced by auth policy on the endpoint
            //as well as the calling code
            if (Guid.TryParse(user.FindFirstValue(DotYouClaimTypes.AppId), out var appId))
            {
                var appRegSvc = httpContext.RequestServices.GetRequiredService<IAppRegistrationService>();
                dotYouContext.AppContext = await appRegSvc.GetAppContextBase(appId);
            }
        }
    }
}