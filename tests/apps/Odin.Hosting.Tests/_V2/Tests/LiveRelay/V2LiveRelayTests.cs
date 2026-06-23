using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient.Factory;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;
using Odin.Services.LiveRelay;

namespace Odin.Hosting.Tests._V2.Tests.LiveRelay;

/// <summary>
/// Coverage for the Live Relay primitive: app-initiated, ephemeral, last-value-wins data sharing
/// between connected identities. Exercises delivery, server-enforced app isolation, automatic
/// flush-on-(re)connect, and the not-connected guard.
/// </summary>
[TestFixture]
public class V2LiveRelayTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>
        {
            TestIdentities.Frodo,
            TestIdentities.Samwise,
            TestIdentities.Merry
        });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _scaffold.RunAfterAnyTests();

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown() => _scaffold.AssertLogEvents();

    [Test]
    public async Task Relay_FromConnectedApp_IsDeliveredToRecipientAppSocket()
    {
        var frodo = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);
        var ownerSam = _scaffold.CreateOwnerApiClientRedux(sam);

        var appId = Guid.NewGuid();
        var (frodoAppToken, frodoAppSecret, samAppToken, samAppSecret) =
            await ConnectAndSetupAppAsync(ownerFrodo, ownerSam, frodo, sam, appId);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var samSocket = await LiveRelayTestHelpers.ConnectAppSocketAsync(sam.OdinId, samAppToken, cts.Token);
            await LiveRelayTestHelpers.DoHandshakeAsync(samSocket, samAppSecret, new List<TargetDrive>(), cts.Token);

            var channelKey = Guid.NewGuid();
            const string blob = "eyJsYXQiOjUxLjUsImxvbiI6LTAuMX0="; // opaque to the server

            var relayResponse = await SendRelayAsync(frodo, frodoAppToken, frodoAppSecret,
                channelKey, new List<string> { sam.OdinId.DomainName }, blob);
            Assert.That(relayResponse.IsSuccessStatusCode, Is.True, $"relay failed: {relayResponse.StatusCode}");

            var received = await LiveRelayTestHelpers.WaitForLiveRelayAsync(samSocket, samAppSecret, TimeSpan.FromSeconds(20));

            Assert.That(received, Is.Not.Null, "recipient socket did not receive the live relay notification");
            Assert.That(received.Blob, Is.EqualTo(blob), "blob must be byte-identical end to end");
            Assert.That(received.ChannelKey, Is.EqualTo(channelKey));
            Assert.That(received.SenderOdinId, Is.EqualTo(frodo.OdinId.DomainName), "sender identity is authoritative");
            Assert.That(received.ReceivedAt, Is.GreaterThan(0), "server-received timestamp must be stamped");

            await LiveRelayTestHelpers.CloseQuietlyAsync(samSocket);
        }
        finally
        {
            await DisconnectAsync(ownerFrodo, ownerSam, frodo, sam);
        }
    }

    [Test]
    public async Task Relay_IsNotDeliveredToSocketOfADifferentApp()
    {
        var frodo = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);
        var ownerSam = _scaffold.CreateOwnerApiClientRedux(sam);

        var appId = Guid.NewGuid();
        var (frodoAppToken, frodoAppSecret, _, _) =
            await ConnectAndSetupAppAsync(ownerFrodo, ownerSam, frodo, sam, appId);

        // A second, unrelated app on Sam — its socket must never see the first app's data.
        var otherAppId = Guid.NewGuid();
        await LiveRelayTestHelpers.PrepareAppAccessAsync(ownerSam, otherAppId, TargetDrive.NewTargetDrive());
        var (otherAppToken, otherAppSecret) = await ownerSam.AppManager.RegisterAppClient(otherAppId);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var otherSocket = await LiveRelayTestHelpers.ConnectAppSocketAsync(sam.OdinId, otherAppToken, cts.Token);
            await LiveRelayTestHelpers.DoHandshakeAsync(otherSocket, otherAppSecret, new List<TargetDrive>(), cts.Token);

            var relayResponse = await SendRelayAsync(frodo, frodoAppToken, frodoAppSecret,
                Guid.NewGuid(), new List<string> { sam.OdinId.DomainName }, "c29tZS1ncHM=");
            Assert.That(relayResponse.IsSuccessStatusCode, Is.True);

            var received = await LiveRelayTestHelpers.WaitForLiveRelayAsync(otherSocket, otherAppSecret, TimeSpan.FromSeconds(5));
            Assert.That(received, Is.Null, "a different app's socket must not receive the relay (server-enforced app isolation)");

            await LiveRelayTestHelpers.CloseQuietlyAsync(otherSocket);
        }
        finally
        {
            await DisconnectAsync(ownerFrodo, ownerSam, frodo, sam);
        }
    }

    [Test]
    public async Task Relay_WhileRecipientHasNoSocket_IsFlushedOnConnect()
    {
        var frodo = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);
        var ownerSam = _scaffold.CreateOwnerApiClientRedux(sam);

        var appId = Guid.NewGuid();
        var (frodoAppToken, frodoAppSecret, samAppToken, samAppSecret) =
            await ConnectAndSetupAppAsync(ownerFrodo, ownerSam, frodo, sam, appId);

        try
        {
            var channelKey = Guid.NewGuid();
            const string blob = "bGFzdC1rbm93bi1wb3NpdGlvbg==";

            // Relay BEFORE Sam connects any socket — the point is retained, not lost.
            var relayResponse = await SendRelayAsync(frodo, frodoAppToken, frodoAppSecret,
                channelKey, new List<string> { sam.OdinId.DomainName }, blob);
            Assert.That(relayResponse.IsSuccessStatusCode, Is.True);

            // Give the recipient server a moment to land + retain the point.
            await Task.Delay(500);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var samSocket = await LiveRelayTestHelpers.ConnectAppSocketAsync(sam.OdinId, samAppToken, cts.Token);
            await LiveRelayTestHelpers.DoHandshakeAsync(samSocket, samAppSecret, new List<TargetDrive>(), cts.Token);

            // No new relay is sent — the data must arrive purely via flush-on-connect.
            var received = await LiveRelayTestHelpers.WaitForLiveRelayAsync(samSocket, samAppSecret, TimeSpan.FromSeconds(20));

            Assert.That(received, Is.Not.Null, "retained point was not flushed to the newly-connected socket");
            Assert.That(received.Blob, Is.EqualTo(blob));
            Assert.That(received.ChannelKey, Is.EqualTo(channelKey));
            Assert.That(received.SenderOdinId, Is.EqualTo(frodo.OdinId.DomainName));

            await LiveRelayTestHelpers.CloseQuietlyAsync(samSocket);
        }
        finally
        {
            await DisconnectAsync(ownerFrodo, ownerSam, frodo, sam);
        }
    }

    [Test]
    public async Task Relay_ToNonConnectedIdentity_IsDroppedAndNotDelivered()
    {
        // Frodo and Merry are never connected. The relay must be silently dropped (fire-and-forget)
        // and nothing must reach Merry's socket.
        var frodo = TestIdentities.Frodo;
        var merry = TestIdentities.Merry;
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);
        var ownerMerry = _scaffold.CreateOwnerApiClientRedux(merry);

        // Frodo needs the app (with UseTransitWrite) to call the endpoint; Merry needs the same app
        // to authenticate a socket.
        var appId = Guid.NewGuid();
        await LiveRelayTestHelpers.PrepareAppAccessAsync(ownerFrodo, appId, TargetDrive.NewTargetDrive());
        await LiveRelayTestHelpers.PrepareAppAccessAsync(ownerMerry, appId, TargetDrive.NewTargetDrive());
        var (frodoAppToken, frodoAppSecret) = await ownerFrodo.AppManager.RegisterAppClient(appId);
        var (merryAppToken, merryAppSecret) = await ownerMerry.AppManager.RegisterAppClient(appId);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var merrySocket = await LiveRelayTestHelpers.ConnectAppSocketAsync(merry.OdinId, merryAppToken, cts.Token);
        await LiveRelayTestHelpers.DoHandshakeAsync(merrySocket, merryAppSecret, new List<TargetDrive>(), cts.Token);

        // Fire-and-forget: even with an unreachable/non-connected recipient the call succeeds.
        var relayResponse = await SendRelayAsync(frodo, frodoAppToken, frodoAppSecret,
            Guid.NewGuid(), new List<string> { merry.OdinId.DomainName }, "c2hvdWxkLW5vdC1hcnJpdmU=");
        Assert.That(relayResponse.IsSuccessStatusCode, Is.True, "relay must not fail the caller on a dropped recipient");

        var received = await LiveRelayTestHelpers.WaitForLiveRelayAsync(merrySocket, merryAppSecret, TimeSpan.FromSeconds(5));
        Assert.That(received, Is.Null, "data must not be delivered to a non-connected identity");

        await LiveRelayTestHelpers.CloseQuietlyAsync(merrySocket);
    }

    //

    private async Task<(ClientAuthenticationToken frodoAppToken, byte[] frodoAppSecret,
            ClientAuthenticationToken samAppToken, byte[] samAppSecret)>
        ConnectAndSetupAppAsync(
            OwnerApiClientRedux ownerFrodo, OwnerApiClientRedux ownerSam,
            TestIdentity frodo, TestIdentity sam, Guid appId)
    {
        // Same app on both identities (a single shared appId — e.g. the chat app).
        var frodoCircleId = await LiveRelayTestHelpers.PrepareAppAccessAsync(ownerFrodo, appId, TargetDrive.NewTargetDrive());
        var samCircleId = await LiveRelayTestHelpers.PrepareAppAccessAsync(ownerSam, appId, TargetDrive.NewTargetDrive());

        await ownerFrodo.Connections.SendConnectionRequest(sam.OdinId, new List<GuidId> { frodoCircleId });
        await ownerSam.Connections.AcceptConnectionRequest(frodo.OdinId, new List<GuidId> { samCircleId });

        var (frodoAppToken, frodoAppSecret) = await ownerFrodo.AppManager.RegisterAppClient(appId);
        var (samAppToken, samAppSecret) = await ownerSam.AppManager.RegisterAppClient(appId);

        return (frodoAppToken, frodoAppSecret, samAppToken, samAppSecret);
    }

    private static async Task DisconnectAsync(
        OwnerApiClientRedux ownerFrodo, OwnerApiClientRedux ownerSam, TestIdentity frodo, TestIdentity sam)
    {
        await ownerFrodo.Connections.DisconnectFrom(sam.OdinId);
        await ownerSam.Connections.DisconnectFrom(frodo.OdinId);
    }

    private static async Task<Refit.IApiResponse> SendRelayAsync(
        TestIdentity sender, ClientAuthenticationToken appToken, byte[] appSecret,
        Guid channelKey, List<string> recipients, string blob)
    {
        var factory = new ApiClientFactoryV2(YouAuthConstants.AppCookieName, appToken, appSecret);
        var client = factory.CreateHttpClient(sender.OdinId, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<ILiveRelayHttpClientApiV2>(client, sharedSecret);
        return await svc.Relay(new LiveRelayRequest
        {
            ChannelKey = channelKey,
            Recipients = recipients,
            Blob = blob
        });
    }
}
