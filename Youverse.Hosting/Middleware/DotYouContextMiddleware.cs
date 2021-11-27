﻿using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Registry;
using Youverse.Hosting.Multitenant;
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

            var cert = _registry.ResolveCertificate(tenant.Name);
            var storage = _registry.ResolveStorageConfig(tenant.Name);
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
            var sharedSecretKey = new SecureKey(Guid.Parse("4fc5b0fd-e21e-427d-961b-a2c7a18f18c5").ToByteArray());
            var appId = user.FindFirstValue(DotYouClaimTypes.AppId);
            var deviceUid = user.FindFirstValue(DotYouClaimTypes.DeviceUid);
            bool isAdminApp = bool.Parse(user.FindFirstValue(DotYouClaimTypes.IsAdminApp) ?? bool.FalseString);
            var app = new AppContext(appId, deviceUid, appEncryptionKey, sharedSecretKey, isAdminApp);
            
            dotYouContext.HostDotYouId = (DotYouIdentity)tenant.Name;
            dotYouContext.AppContext = app;
            dotYouContext.Caller = caller;
            dotYouContext.TenantCertificate = cert;
            dotYouContext.StorageConfig = storage;

            await _next(httpContext);
        }
    }
}