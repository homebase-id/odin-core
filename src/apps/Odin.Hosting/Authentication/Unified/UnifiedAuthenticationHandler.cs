#nullable enable
using System;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Odin.Core.Exceptions;
using Odin.Hosting.Controllers.APIv2;
using Odin.Hosting.Controllers.APIv2.Base;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;

namespace Odin.Hosting.Authentication.Unified
{
    public class UnifiedAuthenticationHandler : AuthenticationHandler<UnifiedAuthenticationSchemeOptions>, IAuthenticationSignInHandler
    {
        private readonly TenantSystemStorage _tenantSystemStorage;

        /// <summary/>
        public UnifiedAuthenticationHandler(IOptionsMonitor<UnifiedAuthenticationSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder, TenantSystemStorage tenantSystemStorage)
            : base(options, logger, encoder)
        {
            _tenantSystemStorage = tenantSystemStorage;
        }

        /// <summary/>
        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            // SEB:TODO we should split up these two into different handlers
            if (Request.Path.StartsWithSegments(OwnerApiPathConstants.YouAuthV1Authorize))
            {
                var returnUrl = WebUtility.UrlEncode(Request.GetDisplayUrl());
                var loginUrl = $"{Request.Scheme}://{Request.Host}{OwnerFrontendPathConstants.Login}?returnUrl={returnUrl}";
                Response.Redirect(loginUrl);
            }
            else
            {
                Context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            }

            return Task.CompletedTask;
        }

        /// <summary/>
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var odinContext = Context.RequestServices.GetRequiredService<IOdinContext>();
            switch (GetRoute())
            {
                case RootApiRoutes.Owner:
                {
                    using var cn = _tenantSystemStorage.CreateConnection();
                    return await OwnerAuthPathHandler.Handle(Context, odinContext, cn);
                }

                case RootApiRoutes.Apps:
                {
                    using var cn = _tenantSystemStorage.CreateConnection();
                    return await AppAuthPathHandler.Handle(Context, odinContext, cn);
                }

                case RootApiRoutes.Guest:
                {
                    using var cn = _tenantSystemStorage.CreateConnection();
                    return await GuestAuthPathHandler.Handle(Context, odinContext, cn);
                }
            }

            return AuthenticateResult.Fail("Invalid Path");
        }

        public async Task SignOutAsync(AuthenticationProperties? properties)
        {
            var odinContext = Context.RequestServices.GetRequiredService<IOdinContext>();
            switch (GetRoute())
            {
                case RootApiRoutes.Owner:
                {
                    using var cn = _tenantSystemStorage.CreateConnection();
                    await OwnerAuthPathHandler.HandleSignOut(Context, odinContext, cn);
                    break;
                }

                case RootApiRoutes.Apps:
                {
                    using var cn = _tenantSystemStorage.CreateConnection();
                    await AppAuthPathHandler.HandleSignOut(Context, odinContext, cn);
                    break;
                }

                case RootApiRoutes.Guest:
                {
                    using var cn = _tenantSystemStorage.CreateConnection();
                    await GuestAuthPathHandler.HandleSignOut(Context, odinContext, cn);
                    break;
                }
            }
            
        }

        public Task SignInAsync(ClaimsPrincipal user, AuthenticationProperties? properties)
        {
            return Task.CompletedTask;
        }

        private bool IsPathRoot(string root)
        {
            return Context.Request.Path.StartsWithSegments(root, StringComparison.InvariantCultureIgnoreCase);
        }
        
        private RootApiRoutes GetRoute()
        {
            if (IsPathRoot(ApiV2PathConstants.OwnerRoot))
            {
                return RootApiRoutes.Owner;
            }

            if (IsPathRoot(ApiV2PathConstants.AppsRoot))
            {
                return RootApiRoutes.Apps;
            }

            if (IsPathRoot(ApiV2PathConstants.GuestRoot))
            {
                return RootApiRoutes.Guest;
            }

            throw new OdinSecurityException("Invalid route");
        }
    }
}