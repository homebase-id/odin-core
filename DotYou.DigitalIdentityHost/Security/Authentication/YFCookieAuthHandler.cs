using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotYou.TenantHost.Security.Authentication
{

    public class YFCookieAuthHandler : AuthenticationHandler<YFCookieAuthSchemeOptions>, IAuthenticationSignInHandler
    {
        public static string SchemeName = "yf.cookie.scheme";

        public YFCookieAuthHandler(IOptionsMonitor<YFCookieAuthSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder, ISystemClock clock)
            : base(options, logger, encoder, clock)
        {
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            string returnUri = HttpUtility.UrlDecode(Context.Request.Query["return_uri"]);

            var b = new UriBuilder();
            b.Host = Context.Request.Host.Host;
            b.Scheme = Context.Request.Scheme;

            b.Query = $"return_uri={HttpUtility.UrlEncode(returnUri)}";
            b.Path = this.Options.LoginUri;

            Context.Response.Redirect(b.ToString());

            return Task.CompletedTask;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var x = this.Context;
            return Task.FromResult(AuthenticateResult.Fail("Actor is not authenticated"));
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            return base.HandleForbiddenAsync(properties);
        }

        public Task SignOutAsync(AuthenticationProperties? properties)
        {
            return Task.CompletedTask;
        }

        public Task SignInAsync(ClaimsPrincipal user, AuthenticationProperties? properties)
        {
            return Task.CompletedTask;
        }
    }
}