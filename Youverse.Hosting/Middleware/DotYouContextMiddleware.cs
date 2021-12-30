using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;
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
            var loginDek = await authService.GetOwnerDek(authResult.SessionToken, authResult.ClientHalfKek);

            dotYouContext.Caller = new CallerContext(
                dotYouId: (DotYouIdentity) user.Identity.Name,
                isOwner: true,
                loginDek: loginDek
            );

            dotYouContext.AppContext = null;
        }

        private async Task LoadAppContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            var user = httpContext.User;
            var appId = Guid.Parse(user.FindFirstValue(DotYouClaimTypes.AppId));
            var deviceUid = Convert.FromBase64String(user.FindFirstValue(DotYouClaimTypes.DeviceUid64));

            var appRegSvc = httpContext.RequestServices.GetRequiredService<IAppRegistrationService>();
            // var appReg = await appRegSvc.GetAppRegistration(appId);
            var deviceReg = await appRegSvc.GetAppDeviceRegistration(appId, deviceUid);

            dotYouContext.Caller = new CallerContext(
                dotYouId: (DotYouIdentity) user.Identity.Name,
                isOwner: user.HasClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower()),
                loginDek: null
            );

            //TODO: what do we need for the appEncryptionKey?
            //appReg.EncryptedAppDeK
            //how to specify the destination drive?
            var driveId = Guid.Empty;
            dotYouContext.AppContext = new AppContext(
                appId: appId.ToString(),
                deviceUid: deviceUid,
                appEncryptionKey: new SecureKey(Guid.Empty.ToByteArray()),
                appSharedSecret: new SecureKey(deviceReg.SharedSecret),
                isAdminApp: false,
                driveId: driveId);
        }

        private async Task LoadTransitContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            var user = httpContext.User;
            var appId = Guid.Parse(user.FindFirstValue(DotYouClaimTypes.AppId));

            // var appRegSvc = httpContext.RequestServices.GetRequiredService<IAppRegistrationService>();
            // var appReg = await appRegSvc.GetAppRegistration(appId);

            dotYouContext.Caller = new CallerContext(
                dotYouId: (DotYouIdentity) user.Identity.Name,
                isOwner: user.HasClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower()),
                loginDek: null
            );

            //appReg.EncryptedAppDeK
            //how to specify the destination drive?
            var driveId = Guid.Empty;
            dotYouContext.AppContext = new AppContext(
                appId: appId.ToString(),
                deviceUid: null,
                appEncryptionKey: new SecureKey(Guid.Empty.ToByteArray()),
                appSharedSecret: null,
                isAdminApp: false,
                driveId: driveId);
        }
    }
}