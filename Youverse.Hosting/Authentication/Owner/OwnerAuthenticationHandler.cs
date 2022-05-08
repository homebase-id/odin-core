#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authentication.Owner;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;

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
            Context.Response.StatusCode = (int) HttpStatusCode.Unauthorized;
            return Task.CompletedTask;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (GetToken(out var authResult))
            {
                var authService = Context.RequestServices.GetRequiredService<IOwnerAuthenticationService>();

                if (await authService.IsValidToken(authResult.Id))
                {
                    var deviceUid = Context.Request.Cookies[DotYouHeaderNames.DeviceUid] ?? "";

                    //TODO: this needs to be pulled from context rather than the domain
                    //TODO: need to centralize where these claims are set.  there is duplicate code in the certificate handler in Startup.cs
                    string domain = this.Context.Request.Host.Host;

                    var claims = new List<Claim>()
                    {
                        new Claim(ClaimTypes.NameIdentifier, domain, ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer),
                        new Claim(ClaimTypes.Name, domain, ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer),

                        //TODO: re-evaluate what it means to be 'identified' at this point in the app
                        new Claim(DotYouClaimTypes.IsIdentified, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),

                        //Note: the app policy of IsAuthorizedApp checks these both.  it's a bit illogical to do so since i set them both here and in the app-auth-handler
                        // will not resolve now, however
                        new Claim(DotYouClaimTypes.IsIdentityOwner, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),

                        new Claim(DotYouClaimTypes.AuthResult, authResult.ToString(), ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer),
                        new Claim(DotYouClaimTypes.DeviceUid64, deviceUid, ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer),
                    };

                    if (!string.IsNullOrEmpty(Context.Request.Headers[DotYouHeaderNames.AppId]))
                    {
                        //TODO: the app is checked in the dotyoucontextmiddleware.  we should add checking here maybe or rename to IsApp instead of IsAuthorizedApp
                        var c = new Claim(DotYouClaimTypes.IsAuthorizedApp, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer);
                        claims.Add(c);
                    }

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
            }

            return AuthenticateResult.Fail("Invalid or missing token");
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