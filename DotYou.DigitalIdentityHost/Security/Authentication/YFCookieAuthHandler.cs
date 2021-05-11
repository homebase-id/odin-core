using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using System.Web;
using DotYou.Kernel.Services.Admin.Authentication;
using DotYou.Types.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
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

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            Guid token;
            if (GetToken(out token))
            {
                var authService = Context.RequestServices.GetRequiredService<IAdminClientAuthenticationService>();
                if (await authService.IsValidToken(token))
                {
                    //TODO: add Identity
                    ClaimsPrincipal principal = new();
                    var ticket = new AuthenticationTicket(principal, SchemeName);
                    return AuthenticateResult.Success(ticket);
                }
            }
            
            return AuthenticateResult.Fail("Actor is not authenticated");
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
                var authService = Context.RequestServices.GetRequiredService<IAdminClientAuthenticationService>();
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
            return Guid.TryParse(Context.Request.Headers[DotYouHeaderNames.AuthToken], out token);
        }
    }
}