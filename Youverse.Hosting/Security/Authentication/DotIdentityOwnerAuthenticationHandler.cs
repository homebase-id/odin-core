using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authorization;

namespace Youverse.Hosting.Security.Authentication
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

            Guid sessionToken;
            if (GetToken(out sessionToken))
            {
                var authService = Context.RequestServices.GetRequiredService<IOwnerAuthenticationService>();

                if (await authService.IsValidToken(sessionToken))
                {
                    //TODO: this needs to be pulled from context rather than the domain

                    //TODO: need to centralize where these claims are set.  there is duplicate code in the certificate handler in Startup.cs
                    string domain = this.Context.Request.Host.Host;

                    //TODO: we need to avoid using a claim to hold the login kek.  it should just be set duringf the Startup.ResolveContext method
                    var r = GetAuthenticationResult();
                    //var loginKek = await authService.GetLoginKek(sessionToken, r.ClientHalfKek);
                    var loginDek = await authService.GetLoginDek(sessionToken, r.ClientHalfKek);
                    var b64 = Convert.ToBase64String(loginDek.GetKey());

                    var claims = new List<Claim>()
                    {
                        new Claim(ClaimTypes.NameIdentifier, domain, ClaimValueTypes.String, YouFoundationIssuer),
                        new Claim(ClaimTypes.Name, domain, ClaimValueTypes.String, YouFoundationIssuer),
                        new Claim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower(), ClaimValueTypes.Boolean, YouFoundationIssuer),
                        new Claim(DotYouClaimTypes.IsIdentified, true.ToString().ToLower(), ClaimValueTypes.Boolean, YouFoundationIssuer),
                        new Claim(DotYouClaimTypes.LoginDek, b64, ClaimValueTypes.String, YouFoundationIssuer)
                    };

                    var identity = new ClaimsIdentity(claims, DotYouAuthConstants.DotIdentityOwnerScheme);
                    ClaimsPrincipal principal = new ClaimsPrincipal(identity);

                    AuthenticationProperties authProperties = new AuthenticationProperties();
                    authProperties.IssuedUtc = DateTime.UtcNow;
                    authProperties.ExpiresUtc = DateTime.UtcNow.AddDays(1);
                    authProperties.AllowRefresh = true;
                    authProperties.IsPersistent = true;

                    var ticket = new AuthenticationTicket(principal, authProperties, DotYouAuthConstants.DotIdentityOwnerScheme);
                    ticket.Properties.SetParameter(DotYouAuthConstants.TokenKey, sessionToken);
                    return AuthenticateResult.Success(ticket);
                }
            }

            return AuthenticateResult.Fail("Invalid or missing token");
        }

        private DotYouAuthenticationResult GetAuthenticationResult()
        {
            DotYouAuthenticationResult result;
            var value = Context.Request.Cookies[DotYouAuthConstants.TokenKey];
            if (DotYouAuthenticationResult.TryParse(value, out result))
            {
                return result;
            }

            return null;
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
            string headerToken = Context.Request.Headers[DotYouAuthConstants.TokenKey];
            var value = string.IsNullOrEmpty(headerToken?.Trim()) ? Context.Request.Cookies[DotYouAuthConstants.TokenKey] : headerToken;
            if (DotYouAuthenticationResult.TryParse(value, out var result))
            {
                token = result.SessionToken;
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