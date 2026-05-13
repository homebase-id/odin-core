#nullable enable
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Odin.Hosting.Authentication.Peer;
using Odin.Services.Authorization;

namespace Odin.Hosting.Tests.V2.Peer;

/// <summary>
/// Test-side replacement for <see cref="PeerCapiAuthenticationHandler"/>. The in-process peer
/// HTTP factory stamps every outbound request with <see cref="TestPeerHttpClientFactory.TestPeerIdentityHeader"/>;
/// this handler reads that header and short-circuits authentication without running the
/// production session-validate-callback dance (which would require mTLS and a real remote).
/// </summary>
/// <remarks>
/// Wired in at <see cref="Hosting.OdinHost"/> startup by post-configuring
/// <see cref="AuthenticationOptions"/> to swap the <c>HandlerType</c> on the three peer auth
/// schemes (Transit / PublicTransit / Feed) to this type. No production code knows about this
/// class or the header it consults.
/// </remarks>
internal sealed class TestPeerCapiAuthenticationHandler(
    IOptionsMonitor<PeerCapiAuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder)
    : AuthenticationHandler<PeerCapiAuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var peer = Context.Request.Headers[TestPeerHttpClientFactory.TestPeerIdentityHeader].ToString();
        if (string.IsNullOrWhiteSpace(peer))
        {
            return Task.FromResult(AuthenticateResult.Fail(
                $"Missing {TestPeerHttpClientFactory.TestPeerIdentityHeader} — in-process peer tests always set it."));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, peer, ClaimValueTypes.String, Scheme.Name),
            new(ClaimTypes.Name, peer, ClaimValueTypes.String, Scheme.Name),
            new(OdinClaimTypes.IsIdentityOwner, bool.FalseString, ClaimValueTypes.Boolean, OdinClaimTypes.YouFoundationIssuer),
            new(OdinClaimTypes.IsAuthenticated, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, OdinClaimTypes.YouFoundationIssuer),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
