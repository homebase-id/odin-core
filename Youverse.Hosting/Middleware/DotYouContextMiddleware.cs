using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
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

        //

        public DotYouContextMiddleware(RequestDelegate next, IIdentityContextRegistry registry, ITenantProvider tenantProvider)
        {
            _next = next;
            _registry = registry;
            _tenantProvider = tenantProvider;
        }

        //

        public async Task Invoke(HttpContext httpContext, DotYouContext dotYouContext)
        {
            var tenant = _tenantProvider.GetCurrentTenant();

            if (tenant?.Name == null || string.IsNullOrEmpty(httpContext.User?.Identity?.AuthenticationType) || null == httpContext.User?.Identity)
            {
                await _next(httpContext);
                return;
            }

            var user = httpContext.User;

            var appId = Guid.Parse(user.FindFirstValue(DotYouClaimTypes.AppId));
            var deviceUid = Convert.FromBase64String(user.FindFirstValue(DotYouClaimTypes.DeviceUid64));

            dotYouContext.HostDotYouId = (DotYouIdentity)tenant.Name;

            string authType = user.Identity?.AuthenticationType ?? "";

            if (authType == OwnerAuthConstants.SchemeName)
            {
                var kek = user.FindFirstValue(DotYouClaimTypes.LoginDek);
                SecureKey chk = kek == null ? null : new SecureKey(Convert.FromBase64String(kek));
                var caller = new CallerContext(
                    dotYouId: (DotYouIdentity)user.Identity.Name,
                    isOwner: user.HasClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower()),
                    loginDek: chk
                );

                dotYouContext.Caller = caller;

                bool isAdminApp = bool.Parse(user.FindFirstValue(DotYouClaimTypes.IsAdminApp) ?? bool.FalseString);
            }

            if (authType == YouAuthConstants.Scheme)
            {
            }

            if (authType == AppAuthConstants.SchemeName)
            {
                var appRegSvc = httpContext.RequestServices.GetRequiredService<IAppRegistrationService>();
                var appReg = await appRegSvc.GetAppRegistration(appId);
                var deviceReg = await appRegSvc.GetAppDeviceRegistration(appId, deviceUid);

                //how to specify the destination drive?
                var driveId = Guid.Empty;
                dotYouContext.AppContext = new AppContext(appId.ToString(), deviceUid, new SecureKey(appReg.EncryptedAppDeK), new SecureKey(deviceReg.SharedSecret), false, driveId);

                dotYouContext.Caller = new CallerContext(
                    dotYouId: (DotYouIdentity)user.Identity.Name,
                    isOwner: user.HasClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower()),
                    loginDek: null
                );
            }

            if (authType == TransitPerimeterAuthConstants.TransitAuthScheme)
            {
            }


            // var appEncryptionKey = new SecureKey(Convert.FromBase64String(user.FindFirstValue(DotYouClaimTypes.AppEncryptionKey64)));
            // var sharedSecretKey = new SecureKey(Convert.FromBase64String(user.FindFirstValue(DotYouClaimTypes.AppDeviceSharedSecret64)));
            var appEncryptionKey = new SecureKey(Array.Empty<byte>());
            var sharedSecretKey = new SecureKey(Array.Empty<byte>());


            //todo: Lookup from app registration


            await _next(httpContext);
        }
    }
}