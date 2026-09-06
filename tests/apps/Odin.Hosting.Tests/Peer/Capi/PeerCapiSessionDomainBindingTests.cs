using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Odin.Core.Http;
using Odin.Core.Identity;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Storage.Cache;
using Odin.Hosting.Authentication.Peer;
using Odin.Services.Authorization;
using Odin.Services.Authorization.Capi;
using Odin.Services.Configuration;
using ZiggyCreatures.Caching.Fusion;

namespace Odin.Hosting.Tests.Peer.Capi;

/// <summary>
/// Does the receiver's peer CAPI session cache bind a validated session to the domain it was
/// validated for? <see cref="PeerCapiAuthenticationHandler"/> caches the "already validated" flag
/// under the bare <c>sessionId</c> and then issues caller claims from the caller-supplied
/// <c>remoteDomain</c>. The sender/validator side (<see cref="CapiCallbackSession"/>) keys by
/// domain. This asymmetry is the concern: once any domain's session id is cached, a caller can
/// replay that same id under a different domain label and skip the callback.
///
/// The scenario under test:
///   1. evil.com authenticates legitimately -> callback validates -> sessionId cached.
///   2. evil.com re-sends the SAME sessionId but labelled victim.com.
///   3. cache hit -> callback skipped -> are claims issued as victim.com?
///
/// The security assertion is that step 3 must NOT yield a victim.com principal without a callback
/// to victim.com. Before the fix (receiver cache keyed on sessionId alone) this test failed;
/// PeerCapiAuthenticationHandler now keys the cache on remoteDomain + sessionId, so it passes.
///
/// Downstream, verified by reading the code (not by this test): the connected-peer drive path is
/// not directly exploitable from this alone. OdinContextMiddleware.LoadTransitContextAsync takes
/// the caller identity from this handler's ClaimTypes.Name but also requires the peer's ICR
/// client-auth token (OdinHeaderNames.ClientAuthToken); CreateTransitPermissionContextAsync then
/// calls GetIcrAsync(claimedDomain, token) -> AssertValidRemoteKey, an AES decrypt of the claimed
/// identity's stored server half-key with the caller's token half-key, which throws unless the
/// caller holds that identity's real token. So evil cannot assemble victim's permission context
/// without victim's token. The residual risk is any peer path that trusts the CAPI-authenticated
/// caller identity WITHOUT that second factor (e.g. token-less peer endpoints reading
/// User.Identity.Name); keying the receiver cache on remoteDomain + sessionId (matching the
/// sender side, CapiCallbackSession) removes the asymmetry regardless.
/// </summary>
public class PeerCapiSessionDomainBindingTests
{
    private const string Receiver = "receiver.dotyou.cloud";
    private const string Evil = "evil.dotyou.cloud";
    private const string Victim = "victim.dotyou.cloud";

    [Test]
    public async Task ValidatedSession_CannotBeReplayedUnderADifferentDomain()
    {
        var cache = NewSessionCache();
        var httpFactory = new RecordingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));

        // --- Step 1: evil.com authenticates legitimately. One session id, its own. ---
        var sessionId = Guid.NewGuid().ToString("N");
        var evilResult = await Authenticate(cache, httpFactory, $"{Evil}~{sessionId}");

        Assert.That(evilResult.Succeeded, Is.True,
            "Pre-req: a legitimately validated caller should authenticate.");
        Assert.That(CallerDomain(evilResult), Is.EqualTo(Evil));
        Assert.That(httpFactory.CallbackHosts.Count, Is.EqualTo(1),
            "Pre-req: the first, uncached session must trigger exactly one validate callback.");
        Assert.That(httpFactory.CallbackHosts.Single(), Is.EqualTo(Evil),
            "Pre-req: the callback must go to the domain the caller claimed.");

        // --- Step 2/3: same session id, relabelled as victim.com. ---
        var replayResult = await Authenticate(cache, httpFactory, $"{Victim}~{sessionId}");

        var calledBackVictim = httpFactory.CallbackHosts.Contains(Victim);
        var impersonatedVictim = replayResult.Succeeded && CallerDomain(replayResult) == Victim;

        Assert.That(impersonatedVictim && !calledBackVictim, Is.False,
            $"CAPI session impersonation: a session validated only for {Evil} authenticated as " +
            $"{Victim} with no callback to {Victim}. The receiver cache keys on sessionId alone " +
            $"while claims are built from the caller-supplied domain. Callbacks made: " +
            $"[{string.Join(", ", httpFactory.CallbackHosts)}].");
    }

    private static async Task<AuthenticateResult> Authenticate(
        ITenantLevel2Cache<PeerCapiAuthenticationHandler> cache,
        IDynamicHttpClientFactory httpFactory,
        string sessionHeader)
    {
        var handler = new PeerCapiAuthenticationHandler(
            new StaticOptionsMonitor(),
            NullLoggerFactory.Instance,
            NewConfiguration(),
            UrlEncoder.Default,
            new StubCorrelationContext(),
            cache,
            httpFactory,
            new OdinIdentity(Guid.NewGuid(), Receiver),
            new StubApplicationLifetime());

        var scheme = new AuthenticationScheme(
            PeerAuthConstants.TransitCapiAuthScheme, null, typeof(PeerCapiAuthenticationHandler));

        var context = new DefaultHttpContext();
        context.Request.Headers[ICapiCallbackSession.SessionHttpHeaderName] = sessionHeader;

        await handler.InitializeAsync(scheme, context);
        return await handler.AuthenticateAsync();
    }

    private static string CallerDomain(AuthenticateResult result)
        => result.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    private static ITenantLevel2Cache<PeerCapiAuthenticationHandler> NewSessionCache()
    {
        var fusion = new FusionCache(new FusionCacheOptions());
        return new TenantLevel2Cache<PeerCapiAuthenticationHandler>(new CacheKeyPrefix("test"), fusion);
    }

    private static OdinConfiguration NewConfiguration() => new()
    {
        Host = new OdinConfiguration.HostSection { CapiSessionLifetime = TimeSpan.FromMinutes(10) },
        CertificateRenewal = new OdinConfiguration.CertificateRenewalSection
        {
            UseCertificateAuthorityProductionServers = false,
        },
    };

    private sealed class RecordingHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : IDynamicHttpClientFactory
    {
        public List<string> CallbackHosts { get; } = [];

        public HttpClient CreateClient(string remoteHostKey, Action<ClientHandlerConfig> configure = null)
            => new(new RecordingHandler(this, responder));

        public void Dispose() { }

        private sealed class RecordingHandler(
            RecordingHttpClientFactory owner, Func<HttpRequestMessage, HttpResponseMessage> responder)
            : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                if (request.RequestUri != null)
                {
                    owner.CallbackHosts.Add(request.RequestUri.Host);
                }
                return Task.FromResult(responder(request));
            }
        }
    }

    private sealed class StaticOptionsMonitor : IOptionsMonitor<PeerCapiAuthenticationSchemeOptions>
    {
        public PeerCapiAuthenticationSchemeOptions CurrentValue { get; } = new();
        public PeerCapiAuthenticationSchemeOptions Get(string name) => CurrentValue;
        public IDisposable OnChange(Action<PeerCapiAuthenticationSchemeOptions, string> listener) => null;
    }

    private sealed class StubCorrelationContext : ICorrelationContext
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
    }

    private sealed class StubApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }
}
