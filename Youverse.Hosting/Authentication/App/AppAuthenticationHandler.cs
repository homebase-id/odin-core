#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authentication.Apps;
using Youverse.Core.Services.Authentication.Owner;
using Youverse.Core.Services.Authorization;
using Youverse.Hosting.Authentication.Owner;

#nullable enable
namespace Youverse.Hosting.Authentication.App
{
    /// <summary>
    /// Handles authenticating apps
    /// </summary>
    public class AppAuthenticationHandler : AuthenticationHandler<AppAuthenticationSchemeOptions>, IAuthenticationSignInHandler
    {
        public AppAuthenticationHandler(IOptionsMonitor<AppAuthenticationSchemeOptions> options, ILoggerFactory logger,
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
            var authService = Context.RequestServices.GetRequiredService<IAppAuthenticationService>();
            if (GetAuthResult(out var authResult))
            {
                var validationResult = await authService.ValidateClientToken(authResult.SessionToken);
                if (validationResult.IsValid)
                {
                    //TODO: this needs to be pulled from context rather than the domain
                    string domain = this.Context.Request.Host.Host;

                    var claims = new List<Claim>()
                    {
                        new Claim(ClaimTypes.NameIdentifier, domain, ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer),
                        new Claim(ClaimTypes.Name, domain, ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer),
                        new Claim(DotYouClaimTypes.IsIdentified, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
                        
                        new Claim(DotYouClaimTypes.IsAuthorizedApp, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),

                        new Claim(DotYouClaimTypes.IsIdentityOwner, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer)
                    };

                    var identity = new ClaimsIdentity(claims, AppAuthConstants.SchemeName);
                    ClaimsPrincipal principal = new ClaimsPrincipal(identity);

                    AuthenticationProperties authProperties = new AuthenticationProperties();
                    authProperties.IssuedUtc = DateTime.UtcNow;
                    authProperties.ExpiresUtc = DateTime.UtcNow.AddDays(1);
                    authProperties.AllowRefresh = true;
                    authProperties.IsPersistent = true;

                    var ticket = new AuthenticationTicket(principal, authProperties, AppAuthConstants.SchemeName);
                    ticket.Properties.SetParameter(OwnerAuthConstants.CookieName, authResult.SessionToken);
                    return AuthenticateResult.Success(ticket);
                }
            }

            return AuthenticateResult.Fail("Invalid or missing token");
        }

        public Task SignOutAsync(AuthenticationProperties? properties)
        {
            if (GetAuthResult(out var result))
            {
                var authService = Context.RequestServices.GetRequiredService<IOwnerAuthenticationService>();
                authService.ExpireToken(result.SessionToken);
            }

            return Task.CompletedTask;
        }

        public Task SignInAsync(ClaimsPrincipal user, AuthenticationProperties? properties)
        {
            return Task.CompletedTask;
        }

        private bool GetAuthResult(out DotYouAuthenticationResult result)
        {
            var value = Context.Request.Cookies[AppAuthConstants.CookieName];
            if (DotYouAuthenticationResult.TryParse(value, out result))
            {
                return true;
            }

            result = null;
            return false;
        }
    }
}