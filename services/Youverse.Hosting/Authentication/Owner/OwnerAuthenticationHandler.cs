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
using Org.BouncyCastle.Asn1.Icao;
using Serilog;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication.Owner;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;

#nullable enable
namespace Youverse.Hosting.Authentication.Owner
{
    /// <summary>
    /// Handles authenticating owners to their owner-console
    /// </summary>
    public class OwnerAuthenticationHandler : AuthenticationHandler<OwnerAuthenticationSchemeOptions>, IAuthenticationSignInHandler
    {
        /// <summary/>
        public OwnerAuthenticationHandler(IOptionsMonitor<OwnerAuthenticationSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder, ISystemClock clock)
            : base(options, logger, encoder, clock)
        {
        }

        /// <summary/>
        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return Task.CompletedTask;
        }

        /// <summary/>
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (GetToken(out var authResult))
            {
                if (authResult == null)
                {
                    return AuthenticateResult.Fail("Empty authResult");
                }

                var dotYouContext = Context.RequestServices.GetRequiredService<DotYouContext>();

                if (!await UpdateDotYouContext(authResult, dotYouContext))
                {
                    return AuthenticateResult.Fail("Invalid Owner Token");
                }

                if (dotYouContext.Caller.OdinId == null)
                {
                    return AuthenticateResult.Fail("Missing OdinId");
                }

                var claims = new List<Claim>()
                {
                    new Claim(ClaimTypes.Name, dotYouContext.Caller.OdinId, ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer),
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

        private async Task<bool> UpdateDotYouContext(ClientAuthenticationToken token, DotYouContext dotYouContext)
        {
            var authService = Context.RequestServices.GetRequiredService<IOwnerAuthenticationService>();
            dotYouContext.SetAuthContext(OwnerAuthConstants.SchemeName);

            //HACK: fix this
            //a bit of a hack here: we have to set the context as owner
            //because it's required to build the permission context
            // this is justified because we're heading down the owner api path
            // just below this, we check to see if the token was good.  if not, the call fails.
            dotYouContext.Caller = new CallerContext(
                odinId: (OdinId)Request.Host.Host,
                masterKey: null,
                securityLevel: SecurityGroupType.Owner);

            DotYouContext ctx = await authService.GetDotYouContext(token);

            if (null == ctx)
            {
                return false;
            }

            dotYouContext.Caller = ctx.Caller;
            dotYouContext.SetPermissionContext(ctx.PermissionsContext);
            return true;
        }

        public Task SignOutAsync(AuthenticationProperties? properties)
        {
            if (GetToken(out var result) && result != null)
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

        private bool GetToken(out ClientAuthenticationToken? authenticationResult)
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