using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive.Query.LiteDb;
using Youverse.Core.Services.Registry;
using Youverse.Core.Services.Tenant;
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

            if (tenant?.Name == null)
            {
                await _next(httpContext);
                return;
            }

            var user = httpContext.User;

            //TODO: is there a way to delete the claim's reference to they kek?
            var kek = user.FindFirstValue(DotYouClaimTypes.LoginDek);
            SecureKey chk = kek == null ? null : new SecureKey(Convert.FromBase64String(kek));
            var caller = new CallerContext(
                dotYouId: (DotYouIdentity)user.Identity.Name,
                isOwner: user.HasClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower()),
                loginDek: chk
            );

            //TODO: load with correct app shared key 
            //HACK: !!!
            var appEncryptionKey = new SecureKey(Guid.Empty.ToByteArray());
            var sharedSecretKey = new SecureKey(new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 });
            var appId = user.FindFirstValue(DotYouClaimTypes.AppId);
            var deviceUid = user.FindFirstValue(DotYouClaimTypes.DeviceUid);
            bool isAdminApp = bool.Parse(user.FindFirstValue(DotYouClaimTypes.IsAdminApp) ?? bool.FalseString);
            
            //todo: Lookup from app registration
            var driveId = ProfileIndexManager.DataAttributeDriveId;

            var app = new AppContext(appId, deviceUid, appEncryptionKey, sharedSecretKey, isAdminApp, driveId);
            
            //how to specify the destination drive?
            
            dotYouContext.HostDotYouId = (DotYouIdentity)tenant.Name;
            dotYouContext.AppContext = app;
            dotYouContext.Caller = caller;

            await _next(httpContext);
        }
    }
}