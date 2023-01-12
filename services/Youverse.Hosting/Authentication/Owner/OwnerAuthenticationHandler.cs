#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication.Owner;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;

#nullable enable
namespace Youverse.Hosting.Authentication.Owner
{
    /// <summary>
    /// Handles authenticating owners to their owner-console
    /// </summary>
    public class OwnerAuthenticationHandler : AuthenticationHandler<OwnerAuthenticationSchemeOptions>, IAuthenticationSignInHandler
    {
        public OwnerAuthenticationHandler(IOptionsMonitor<OwnerAuthenticationSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder, ISystemClock clock)
            : base(options, logger, encoder, clock)
        {
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return Task.CompletedTask;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (GetToken(out var authResult))
            {
                var dotYouContext = Context.RequestServices.GetRequiredService<DotYouContext>();

                await UpdateDotYouContext(authResult, dotYouContext);

                var claims = new List<Claim>()
                {
                    new Claim(ClaimTypes.Name, dotYouContext.Caller.DotYouId, ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer),
                    new Claim(DotYouClaimTypes.IsAuthenticated, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
                    new Claim(DotYouClaimTypes.IsIdentityOwner, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
                };

                var identity = new ClaimsIdentity(claims, OwnerAuthConstants.SchemeName);
                ClaimsPrincipal principal = new ClaimsPrincipal(identity);

                AuthenticationProperties authProperties = new AuthenticationProperties();
                authProperties.IssuedUtc = DateTime.UtcNow;
                authProperties.ExpiresUtc = DateTime.UtcNow.AddDays(1);
                authProperties.AllowRefresh = true;
                authProperties.IsPersistent = true;

                var ticket = new AuthenticationTicket(principal, authProperties, OwnerAuthConstants.SchemeName);
                ticket.Properties.SetParameter(OwnerAuthConstants.CookieName, authResult.Id);
                return AuthenticateResult.Success(ticket);
            }

            return AuthenticateResult.Fail("Invalid or missing token");
        }

        private async Task UpdateDotYouContext(ClientAuthenticationToken token, DotYouContext dotYouContext)
        {
            var authService = Context.RequestServices.GetRequiredService<IOwnerAuthenticationService>();
            dotYouContext.AuthContext = OwnerAuthConstants.SchemeName;

            DotYouContext ctx = await authService.GetDotYouContext(token);
           
            if (null == ctx)
            {
                AuthenticateResult.Fail("Invalid Owner Token");
            }
            
            dotYouContext.Caller = ctx.Caller;
            dotYouContext.SetPermissionContext(ctx.PermissionsContext);
        }

        public Task SignOutAsync(AuthenticationProperties? properties)
        {
            if (GetToken(out var result))
            {
                var authService = Context.RequestServices.GetRequiredService<IOwnerAuthenticationService>();
                authService.ExpireToken(result.Id);
            }

            return Task.CompletedTask;
        }

        public Task SignInAsync(ClaimsPrincipal user, AuthenticationProperties? properties)
        {
            return Task.CompletedTask;
        }

        private bool GetToken(out ClientAuthenticationToken authenticationResult)
        {
            //TODO: can we remove some of the sensitive cookie values from memory
            var value = Context.Request.Cookies[OwnerAuthConstants.CookieName];
            if (ClientAuthenticationToken.TryParse(value, out var result))
            {
                authenticationResult = result;
                return true;
            }

            authenticationResult = null;
            return false;
        }
    }
}