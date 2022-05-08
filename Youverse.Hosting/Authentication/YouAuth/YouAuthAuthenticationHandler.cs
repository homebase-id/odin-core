using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Youverse.Core.Services.Authentication.YouAuth;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Hosting.Authentication.YouAuth
{
    public class YouAuthAuthenticationHandler : AuthenticationHandler<YouAuthAuthenticationSchemeOptions>
    {
        private readonly ExchangeGrantContextService _exchangeGrantContextService;

        public YouAuthAuthenticationHandler(
            IOptionsMonitor<YouAuthAuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock,
            ExchangeGrantContextService exchangeGrantContextService)
            : base(options, logger, encoder, clock)
        {
            _exchangeGrantContextService = exchangeGrantContextService;
        }

        //

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!TryGetClientAuthToken(out var clientAuthToken))
            {
                return AuthenticateResult.Success(CreateAnonTicket());
            }

            var (isValid, _, grant) = await _exchangeGrantContextService.ValidateClientAuthToken(clientAuthToken);
            var identityGrant = (IdentityExchangeGrant) grant;
            
            if (!isValid)
            {
                //TODD: changing to set user as anonymous instead of failing.  This allows us to support reading files whose ACL = anonymous
                // return AuthenticateResult.Fail("No session matching session id");
                return AuthenticateResult.Success(CreateAnonTicket());
            }

            var claims = new[]
            {
                new Claim(YouAuthDefaults.IdentityClaim, identityGrant.DotYouId),
                new Claim(DotYouClaimTypes.IsIdentityOwner, bool.FalseString, ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
                new Claim(DotYouClaimTypes.IsIdentified, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
                //Note: if you have a session, you're in the network because we've verified via login
                new Claim(DotYouClaimTypes.IsInNetwork, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer)
            };

            var claimsIdentity = new ClaimsIdentity(claims, YouAuthConstants.Scheme);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(claimsIdentity), this.Scheme.Name);

            return AuthenticateResult.Success(ticket);
        }

        private AuthenticationTicket CreateAnonTicket()
        {
            var claims = new[]
            {
                new Claim(YouAuthDefaults.IdentityClaim, YouAuthDefaults.AnonymousIdentifier), //TODO: figure out a better way to communicate this visitor is anonymous
                new Claim(DotYouClaimTypes.IsIdentityOwner, bool.FalseString, ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
                new Claim(DotYouClaimTypes.IsIdentified, bool.FalseString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer)
            };

            var claimsIdentity = new ClaimsIdentity(claims, YouAuthConstants.Scheme);
            return new AuthenticationTicket(new ClaimsPrincipal(claimsIdentity), this.Scheme.Name);
        }

        //

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Not authenticated",
                Instance = Context.Request.Path
            };
            var json = JsonSerializer.Serialize(problemDetails);

            Context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            Context.Response.ContentType = "application/problem+json";
            return Context.Response.WriteAsync(json);
        }

        //

        private bool TryGetClientAuthToken(out ClientAuthenticationToken clientAuthToken)
        {
            var clientAccessTokenValue64 = Context.Request.Cookies[YouAuthDefaults.XTokenCookieName];
            return ClientAuthenticationToken.TryParse(clientAccessTokenValue64, out clientAuthToken);
        }
    }
}