using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
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
            var masterKey = await authService.GetMasterKey(authResult.SessionToken, authResult.ClientHalfKek);
            
            dotYouContext.Caller = new CallerContext(
                dotYouId: (DotYouIdentity) user.Identity.Name,
                isOwner: true,
                masterKey: masterKey
            );

            dotYouContext.AppContext = null;
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

            //look up grant for this device and app
            var deviceHalfKek = authResult.ClientHalfKek;
            dotYouContext.AppContext = await appRegSvc.GetAppContext(authResult.SessionToken, deviceHalfKek);
        }

        private async Task LoadTransitContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            var user = httpContext.User;
            var appId = Guid.Parse(user.FindFirstValue(DotYouClaimTypes.AppId));

            var appRegSvc = httpContext.RequestServices.GetRequiredService<IAppRegistrationService>();
            var appReg = await appRegSvc.GetAppRegistration(appId);

            dotYouContext.Caller = new CallerContext(
                dotYouId: (DotYouIdentity) user.Identity.Name,
                isOwner: user.HasClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower()),
                masterKey: null // Note: we're logged in using an app token so we do not have the master key
            );
            
            //TODO: fix for transit
            var grants = new List<DriveGrant>();
            var driveId = appReg.DriveId;
            dotYouContext.AppContext = new AppContext(
                appId: appId.ToString(),
                appClientId: Guid.Empty, //TODO: this should be nullable or we need to have a TransitContext instead of AppContext (the latter is best)
                clientSharedSecret: null,
                driveId: driveId,
                null,
                null,
                driveGrants: grants);
        }
    }
}