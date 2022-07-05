using System;
using System.Collections.Generic;
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
using Youverse.Hosting.Controllers.Anonymous;
using Youverse.Hosting.Controllers.ClientToken;

namespace Youverse.Hosting.Authentication.ClientToken
{
    public class ClientTokenAuthenticationHandler : AuthenticationHandler<ClientTokenAuthenticationSchemeOptions>
    {
        private readonly ExchangeGrantContextService _exchangeGrantContextService;

        public ClientTokenAuthenticationHandler(
            IOptionsMonitor<ClientTokenAuthenticationSchemeOptions> options,
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
            bool isAppPath = this.Context.Request.Path.StartsWithSegments(AppApiPathConstants.BasePathV1, StringComparison.InvariantCultureIgnoreCase);
            if (isAppPath)
            {
                return await HandleAppAuth();
            }

            bool isYouAuthPath = this.Context.Request.Path.StartsWithSegments(YouAuthApiPathConstants.BasePathV1, StringComparison.InvariantCultureIgnoreCase);
            if (isYouAuthPath)
            {
                return await HandleYouAuth();
            }

            return AuthenticateResult.Fail("Invalid Path");
        }

        private async Task<AuthenticateResult> HandleAppAuth()
        {
            if (!TryGetClientAuthToken(AppAuthConstants.ClientAuthTokenCookieName, out var clientAuthToken))
            {
                return AuthenticateResult.Success(CreateAnonTicket());
            }

            var (isValid, _, _) = await _exchangeGrantContextService.ValidateClientAuthToken(clientAuthToken);

            if (!isValid)
            {
                return AuthenticateResult.Success(CreateAnonTicket());
            }
            
            var claims = new List<Claim>();

            
            claims.Add(new Claim(ClaimTypes.Name, Request.Host.Host)); //caller is this owner
            claims.Add(new Claim(DotYouClaimTypes.IsAuthorizedApp, true.ToString().ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer));
            claims.Add(new Claim(DotYouClaimTypes.IsIdentified, true.ToString().ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer));
            claims.Add(new Claim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer));

            return CreateAuthenticationResult(claims, AppAuthConstants.SchemeName);
        }

        private async Task<AuthenticateResult> HandleYouAuth()
        {
            if (!TryGetClientAuthToken(YouAuthDefaults.XTokenCookieName, out var clientAuthToken))
            {
                return AuthenticateResult.Success(CreateAnonTicket());
            }

            var (isValid, _, grant) = await _exchangeGrantContextService.ValidateClientAuthToken(clientAuthToken);

            if (!isValid)
            {
                return AuthenticateResult.Success(CreateAnonTicket());
            }

            var claims = new List<Claim>();
            var youAuthGrant = (YouAuthExchangeGrant)grant;
            claims.Add(new Claim(ClaimTypes.Name, youAuthGrant.DotYouId));
            claims.Add(new Claim(DotYouClaimTypes.IsIdentityOwner, bool.FalseString, ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer));
            claims.Add(new Claim(DotYouClaimTypes.IsInNetwork, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer));
            claims.Add(new Claim(DotYouClaimTypes.IsIdentified, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer));

            return CreateAuthenticationResult(claims, ClientTokenConstants.Scheme);
        }

        private AuthenticateResult CreateAuthenticationResult(IEnumerable<Claim> claims, string scheme)
        {
            var claimsIdentity = new ClaimsIdentity(claims, scheme);
            // AuthenticationProperties authProperties = new AuthenticationProperties();
            // authProperties.IssuedUtc = DateTime.UtcNow;
            // authProperties.ExpiresUtc = DateTime.UtcNow.AddDays(1);
            // authProperties.AllowRefresh = true;
            // authProperties.IsPersistent = true;

            var ticket = new AuthenticationTicket(new ClaimsPrincipal(claimsIdentity), scheme);
            return AuthenticateResult.Success(ticket);
        }

        private AuthenticationTicket CreateAnonTicket()
        {
            var claims = new[]
            {
                new Claim(YouAuthDefaults.IdentityClaim, YouAuthDefaults.AnonymousIdentifier), //TODO: figure out a better way to communicate this visitor is anonymous
                new Claim(DotYouClaimTypes.IsIdentityOwner, bool.FalseString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
                new Claim(DotYouClaimTypes.IsIdentified, bool.FalseString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer)
            };

            var claimsIdentity = new ClaimsIdentity(claims, ClientTokenConstants.Scheme);
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

        private bool TryGetClientAuthToken(string cookieName, out ClientAuthenticationToken clientAuthToken)
        {
            var clientAccessTokenValue64 = Context.Request.Cookies[cookieName];
            return ClientAuthenticationToken.TryParse(clientAccessTokenValue64, out clientAuthToken);
        }
    }
}