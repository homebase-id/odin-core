﻿using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using System.Web;
using DotYou.Kernel.Services.Admin.Authentication;
using DotYou.Kernel.Services.Identity;
using DotYou.Types;
using DotYou.Types.Admin;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotYou.TenantHost.Security.Authentication
{
    public class DotIdentityOwnerAuthenticationHandler : AuthenticationHandler<DotIdentityOwnerAuthenticationSchemeOptions>, IAuthenticationSignInHandler
    {
        public DotIdentityOwnerAuthenticationHandler(IOptionsMonitor<DotIdentityOwnerAuthenticationSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder, ISystemClock clock)
            : base(options, logger, encoder, clock)
        {
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            //HACK: need to review if this makes sense.  maybe instead we just host all API calls on api.frodobaggins.me.
            if (Context.Request.Path.StartsWithSegments("/api", StringComparison.InvariantCultureIgnoreCase) == false)
            {
                return Task.CompletedTask;
            }

            string returnUri = HttpUtility.UrlDecode(Context.Request.Query["return_uri"]);

            var b = new UriBuilder();
            b.Host = Context.Request.Host.Host;
            b.Scheme = Context.Request.Scheme;

            b.Query = $"return_uri={HttpUtility.UrlEncode(returnUri)}";
            b.Path = this.Options.LoginUri;

            Context.Response.Redirect(b.ToString());
            return Task.CompletedTask;
        }


        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            const string YouFoundationIssuer = "YouFoundation";

            Guid token;
            if (GetToken(out token))
            {
                var authService = Context.RequestServices.GetRequiredService<IOwnerAuthenticationService>();

                if (await authService.IsValidToken(token))
                {
                    //TODO: this needs to be pulled from context rather than the domain

                    string domain = this.Context.Request.Host.Host;
                    var claims = new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, domain, ClaimValueTypes.String, YouFoundationIssuer),
                        new Claim(ClaimTypes.Name, domain, ClaimValueTypes.String, YouFoundationIssuer),
                        new Claim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower(), ClaimValueTypes.Boolean, YouFoundationIssuer),
                        new Claim(DotYouClaimTypes.IsIdentified, true.ToString().ToLower(), ClaimValueTypes.Boolean, YouFoundationIssuer),
                    };

                    var identity = new ClaimsIdentity(claims, DotYouAuthConstants.DotIdentityOwnerScheme);
                    ClaimsPrincipal principal = new ClaimsPrincipal(identity);

                    AuthenticationProperties authProperties = new AuthenticationProperties();
                    authProperties.IssuedUtc = DateTime.UtcNow;
                    authProperties.ExpiresUtc = DateTime.UtcNow.AddDays(1);
                    authProperties.AllowRefresh = true;
                    authProperties.IsPersistent = true;

                    var ticket = new AuthenticationTicket(principal, authProperties, DotYouAuthConstants.DotIdentityOwnerScheme);
                    ticket.Properties.SetParameter(DotYouAuthConstants.TokenKey, token);
                    return AuthenticateResult.Success(ticket);
                }
            }

            return AuthenticateResult.Fail("Invalid or missing token");
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            return base.HandleForbiddenAsync(properties);
        }

        public Task SignOutAsync(AuthenticationProperties? properties)
        {
            Guid token;
            if (GetToken(out token))
            {
                var authService = Context.RequestServices.GetRequiredService<IOwnerAuthenticationService>();
                authService.ExpireToken(token);
            }

            return Task.CompletedTask;
        }

        public Task SignInAsync(ClaimsPrincipal user, AuthenticationProperties? properties)
        {
            return Task.CompletedTask;
        }

        private bool GetToken(out Guid token)
        {
            //the header is used by the mobile app
            if (Guid.TryParse(Context.Request.Headers[DotYouHeaderNames.AuthToken], out token))
            {
                return true;
            }

            //the react client app uses the cookie
            AuthenticationResult result;
            var value = Context.Request.Cookies[DotYouAuthConstants.TokenKey];
            if (AuthenticationResult.TryParse(value, out result))
            {
                token = result.Token;
                return true;
            }

            //TODO: need to avoid the access token on the querystring after #prototrial
            //look for token on querying string as it will come from SignalR
            if (Context.Request.Path.StartsWithSegments("/api/live", StringComparison.OrdinalIgnoreCase) &&
                Context.Request.Query.TryGetValue("access_token", out var accessToken))
            {
                return Guid.TryParse(accessToken, out token);
            }

            return false;
        }
    }
}