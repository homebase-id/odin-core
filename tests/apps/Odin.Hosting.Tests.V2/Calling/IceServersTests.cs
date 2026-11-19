#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Time;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Calling;
using Odin.Services.Drives;
using Refit;

namespace Odin.Hosting.Tests.V2.Calling;

public interface IV2CallingClient
{
    [Post(UnifiedApiRouteConstants.Calling + "/ice-servers")]
    Task<ApiResponse<IceServersResponse>> GetIceServers([Body] IceServersRequest request);
}

/// <summary>
/// POST /api/v2/calling/ice-servers (docs/turn.md). Its tests run on a host with a TURN server and on
/// one without.
/// </summary>
public abstract class IceServersFixture : V2Fixture
{
    protected const string StunUrl = "stun:stun1.dotyou.cloud:3478";

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    protected static async Task<ApiResponse<IceServersResponse>> AskAsync(IV2Caller caller, string peer) =>
        await caller.RefitFor<IV2CallingClient>().GetIceServers(new IceServersRequest { Peer = peer });

    protected async Task ConnectFrodoAndSamAsync()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.ConnectAsync(frodo, sam);
    }

    [Test]
    public async Task APeerThatIsNotConnectedIsRefused()
    {
        var app = await SetupCaller(CallerSpec.SampleApp());

        var response = await AskAsync(app, Identities.Sam);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), response.Error?.Content);
        Assert.That(response.Error?.Content, Does.Contain("\"errorCode\":\"notAConnectedIdentity\""),
            "refused for the connection, not for some other reason");
    }
}

/// <summary>A host with a TURN server: STUN plus a login coturn accepts.</summary>
public class IceServersTests : IceServersFixture
{
    private const string Secret = "ice-servers-test-secret-0123456789abcdef";

    protected override IReadOnlyDictionary<string, string?> ConfigOverrides =>
        new Dictionary<string, string?>
        {
            ["Stun:PublicUrls:0"] = StunUrl,
            ["Turn:Enabled"] = "true",
            ["Turn:SharedSecret"] = Secret,
        };

    [Test]
    public async Task AnAppGetsStunAndATurnLoginItsTurnServerAccepts()
    {
        var app = await SetupCaller(CallerSpec.SampleApp());
        await ConnectFrodoAndSamAsync();
        var before = UnixTimeUtc.Now();

        var response = await AskAsync(app, Identities.Sam);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True, $"Cache-Control: {response.Headers.CacheControl}");

        var servers = response.Content!.IceServers;
        Assert.That(servers.Select(s => string.Join(",", s.Urls)), Is.EqualTo(new[]
        {
            StunUrl,
            $"turn:{Identities.Frodo}:3479?transport=udp,turn:{Identities.Frodo}:3479?transport=tcp",
        }));
        Assert.That(servers[0].Username, Is.Null, "STUN has no login");

        var turn = servers[1];
        var parts = turn.Username.Split(':');
        Assert.That(parts, Has.Length.EqualTo(2), $"username '{turn.Username}' is <expiry>:<identity> and nothing else");
        Assert.That(parts[1], Is.EqualTo(Identities.Frodo));

        var expiry = long.Parse(parts[0]);
        Assert.That(expiry, Is.InRange(before.seconds + 299, UnixTimeUtc.Now().seconds + 300), "default lifetime is 300 s");
        Assert.That(response.Content.ExpiresAt, Is.EqualTo(new UnixTimeUtc(expiry * 1000)));

        // What coturn does with use-auth-secret: recompute the HMAC from the username
        var expected = Convert.ToBase64String(
            HMACSHA1.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(turn.Username)));
        Assert.That(turn.Credential, Is.EqualTo(expected));
    }

    public static IEnumerable<object[]> CallerCases()
    {
        // LiveRelay, which carries the call setup, needs an app; the owner console has none
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.BadRequest];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Forbidden];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Read, [PermissionKeys.UseTransitWrite]), HttpStatusCode.OK];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Forbidden];
    }

    [Test, TestCaseSource(nameof(CallerCases))]
    public async Task OnlyAnAppThatMaySendToPeersGetsServers(CallerSpec spec, HttpStatusCode expected)
    {
        var caller = await SetupCaller(spec);
        await ConnectFrodoAndSamAsync();

        var response = await AskAsync(caller, Identities.Sam);

        Assert.That(response.StatusCode, Is.EqualTo(expected), response.Error?.Content);
    }

    [Test]
    public async Task AnAppThatAsksTooOftenGetsStunWithoutATurnLogin()
    {
        // Sam, so the limiter is not shared with the other tests' Frodo apps
        var app = await SetupCaller(CallerSpec.SampleApp(), Identities.Sam);
        await ConnectFrodoAndSamAsync();

        for (var i = 0; i < TurnCredentialRateLimiter.PermitsPerMinute; i++)
        {
            var ok = await AskAsync(app, Identities.Frodo);
            Assert.That(ok.Content?.ExpiresAt, Is.Not.Null, $"login {i + 1}: {ok.StatusCode} {ok.Error?.Content}");
        }

        var over = await AskAsync(app, Identities.Frodo);

        Assert.That(over.StatusCode, Is.EqualTo(HttpStatusCode.OK), over.Error?.Content);
        Assert.That(over.Content!.IceServers.Select(s => string.Join(",", s.Urls)), Is.EqualTo(new[] { StunUrl }),
            "STUN still, so the call can try to connect directly");
        Assert.That(over.Content.ExpiresAt, Is.Null);
    }
}

/// <summary>A host without a TURN server: STUN only, so a client needs no other path.</summary>
public class IceServersWithoutTurnTests : IceServersFixture
{
    protected override IReadOnlyDictionary<string, string?> ConfigOverrides =>
        new Dictionary<string, string?> { ["Stun:PublicUrls:0"] = StunUrl };

    [Test]
    public async Task AnAppGetsStunOnly()
    {
        var app = await SetupCaller(CallerSpec.SampleApp());
        await ConnectFrodoAndSamAsync();

        var response = await AskAsync(app, Identities.Sam);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        Assert.That(response.Content!.IceServers.Select(s => string.Join(",", s.Urls)), Is.EqualTo(new[] { StunUrl }));
        Assert.That(response.Content.ExpiresAt, Is.Null);
    }
}
