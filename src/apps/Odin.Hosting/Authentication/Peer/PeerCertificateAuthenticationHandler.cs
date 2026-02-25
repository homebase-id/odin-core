using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Odin.Services.Authorization;
using Odin.Services.Authorization.Capi;
using Odin.Services.Configuration;

namespace Odin.Hosting.Authentication.Peer
{
    /// <summary>
    /// This is a copy of Microsoft.AspNetCore.Authentication.Certificate.CertificateAuthenticationHandler except
    /// it raises an event after certificate validation even if it has been cached.
    /// </summary>
    public class PeerCertificateAuthenticationHandler(
        IOptionsMonitor<CertificateAuthenticationOptions> options,
        ILoggerFactory logger,
        //OdinConfiguration config,
        UrlEncoder encoder)
        : AuthenticationHandler<CertificateAuthenticationOptions>(options, logger, encoder)
    {
        /// <summary>
        /// The handler calls methods on the events which give the application control at certain points where processing is occurring.
        /// If it is not provided a default instance is supplied which does nothing when the methods are called.
        /// </summary>
        protected new CertificateAuthenticationEvents Events
        {
            get { return (CertificateAuthenticationEvents)base.Events; }
            set { base.Events = value; }
        }

        /// <summary>
        /// Creates a new instance of the events instance.
        /// </summary>
        /// <returns>A new instance of the events instance.</returns>
        protected override Task<object> CreateEventsAsync() => Task.FromResult<object>(new CertificateAuthenticationEvents());

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var capiSession = Context.Request.Headers[ICapiCallbackSession.SessionHttpHeaderName].ToString();
            var capiDomainAndSessionId = capiSession.Split(':');
            if (capiDomainAndSessionId.Length != 2)
            {
                return AuthenticateResult.Fail($"Invalid or missing {ICapiCallbackSession.SessionHttpHeaderName}");
            }

            var domain = capiDomainAndSessionId[0];
            if (string.IsNullOrWhiteSpace(domain))
            {
                return AuthenticateResult.Fail($"Invalid sender domain in {ICapiCallbackSession.SessionHttpHeaderName}");
            }

            var sessionId = capiDomainAndSessionId[1];
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return AuthenticateResult.Fail($"Invalid session id in {ICapiCallbackSession.SessionHttpHeaderName}");
            }

            await Task.CompletedTask;

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, domain, ClaimValueTypes.String, Options.ClaimsIssuer),
                new Claim(ClaimTypes.Name, domain, ClaimValueTypes.String, Options.ClaimsIssuer),
                new Claim(OdinClaimTypes.IsIdentityOwner, bool.FalseString, ClaimValueTypes.Boolean, OdinClaimTypes.YouFoundationIssuer),
                new Claim(OdinClaimTypes.IsAuthenticated, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, OdinClaimTypes.YouFoundationIssuer),
            };

            var principal = new ClaimsPrincipal(
                new ClaimsIdentity(claims, Scheme.Name)
            );

            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return AuthenticateResult.Success(ticket);
        }

        protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            var authenticationChallengedContext = new CertificateChallengeContext(Context, Scheme, Options, properties);
            await Events.Challenge(authenticationChallengedContext);

            if (authenticationChallengedContext.Handled)
            {
                return;
            }

            // Certificate authentication takes place at the connection level. We can't prompt once we're in
            // user code, so the best thing to do is Forbid, not Challenge.
            await HandleForbiddenAsync(properties);
        }
    }
}