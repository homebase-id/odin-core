using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authentication.AppAuth;
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
            var validationResult = await authService.ValidateSessionToken(authResult.SessionToken);
            var appDevice = validationResult.AppDevice;
            var user = httpContext.User;

            var appReg = await appRegSvc.GetAppRegistration(appDevice.ApplicationId);
            var deviceReg = await appRegSvc.GetAppDeviceRegistration(appDevice.ApplicationId, appDevice.DeviceUid);

            dotYouContext.Caller = new CallerContext(
                dotYouId: (DotYouIdentity) user.Identity.Name,
                isOwner: user.HasClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower()),
                masterKey: null // Note: we're logged in using an app token so we do not have the master key
            );

            //**** HERE I DO NOT HAVE THE MASTER KEY - because we are logged in using an app token ****

            var appDeviceReg = await appRegSvc.GetAppDeviceRegistration(appDevice.ApplicationId, appDevice.DeviceUid);
            var serverHalf = appDeviceReg.AppHalfKek;
            var appEncryptionKey = serverHalf.DecryptKey(authResult.ClientHalfKek.GetKey());
            
            //TODO: Use the fullKey to get the storageDek
            //at this point - I don't know which drive will be used, it will vary per request; i DO know the grants
            // so maybe i store the grants in context?
            
            var driveId = appReg.DriveId;
            dotYouContext.AppContext = new AppContext(
                appId: appDevice.ApplicationId.ToString(),
                deviceUid: appDevice.DeviceUid,
                deviceSharedSecret: new SensitiveByteArray(deviceReg.SharedSecret),
                driveId: driveId);
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

            //appReg.EncryptedAppDeK
            //how to specify the destination drive?
            var driveId = appReg.DriveId;
            dotYouContext.AppContext = new AppContext(
                appId: appId.ToString(),
                deviceUid: null,
                appEncryptionKey: new SensitiveByteArray(Guid.Empty.ToByteArray()),
                deviceSharedSecret: null,
                driveId: driveId);
        }
    }
}