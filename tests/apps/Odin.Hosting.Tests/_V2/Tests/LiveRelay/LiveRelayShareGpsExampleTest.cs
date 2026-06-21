using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient.Factory;
using Odin.Services.Drives;
using Odin.Services.LiveRelay;

namespace Odin.Hosting.Tests._V2.Tests.LiveRelay;

/// <summary>
/// EXAMPLE / REFERENCE TEST — the end-to-end contract a client implements to share live GPS.
///
/// Two friends each run the same app (one shared appId). One opens the "live map" (a websocket to
/// its own server) and the other shares GPS coordinates. The server treats the coordinate as an
/// opaque <c>Blob</c> — it is the app that encodes/encrypts and decodes. Nothing is stored durably.
///
/// Client recipe demonstrated here:
///   1) both identities run the same app (shared appId) and are connected
///   2) recipient opens the notification websocket (authenticated as that app) and handshakes
///   3) sender POSTs /api/v2/live-relay with { channelKey, recipients, blob }
///   4) recipient receives a LiveRelay notification carrying { senderOdinId, channelKey, blob,
///      receivedAt }, filters by the channelKey of its open session, decodes the blob, and draws it
/// </summary>
[TestFixture]
public class LiveRelayShareGpsExampleTest
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
            TestIdentities.Samwise
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

    // The opaque payload, from the app's point of view. In production this is end-to-end encrypted
    // by the app before it leaves the device; the server never sees inside it.
    private class GpsPoint
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
    }

    [Test]
    public async Task TwoFriendsShareLiveGps()
    {
        var sharer = TestIdentities.Frodo;     // shares his location
        var watcher = TestIdentities.Samwise;  // watches the live map

        var ownerSharer = _scaffold.CreateOwnerApiClientRedux(sharer);
        var ownerWatcher = _scaffold.CreateOwnerApiClientRedux(watcher);

        // (1) Both run the same app and are connected friends.
        var appId = Guid.NewGuid();
        var sharerCircle = await LiveRelayTestHelpers.PrepareAppAccessAsync(ownerSharer, appId, TargetDrive.NewTargetDrive());
        var watcherCircle = await LiveRelayTestHelpers.PrepareAppAccessAsync(ownerWatcher, appId, TargetDrive.NewTargetDrive());
        await ownerSharer.Connections.SendConnectionRequest(watcher.OdinId, new List<GuidId> { sharerCircle });
        await ownerWatcher.Connections.AcceptConnectionRequest(sharer.OdinId, new List<GuidId> { watcherCircle });

        var (sharerAppToken, sharerAppSecret) = await ownerSharer.AppManager.RegisterAppClient(appId);
        var (watcherAppToken, watcherAppSecret) = await ownerWatcher.AppManager.RegisterAppClient(appId);

        // The session both apps agreed on (e.g. "our vacation trip"). The watcher renders only data
        // for sessions it has open; the sharer puts everyone it wants to share with on Recipients.
        var tripChannel = Guid.NewGuid();

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            // (2) Watcher opens the live map: connect the websocket as the app, then handshake.
            using var socket = await LiveRelayTestHelpers.ConnectAppSocketAsync(watcher.OdinId, watcherAppToken, cts.Token);
            await LiveRelayTestHelpers.DoHandshakeAsync(socket, watcherAppSecret, new List<TargetDrive>(), cts.Token);

            // (3) Sharer encodes a GPS point (opaque to the server) and shares it on the channel.
            var point = new GpsPoint { Lat = 51.5072, Lon = -0.1276 };
            var blob = Convert.ToBase64String(OdinSystemSerializer.Serialize(point).ToUtf8ByteArray());

            var factory = new ApiClientFactoryV2(YouAuthConstants.AppCookieName, sharerAppToken, sharerAppSecret);
            var client = factory.CreateHttpClient(sharer.OdinId, out var sharedSecret);
            var liveRelay = RefitCreator.RestServiceFor<ILiveRelayHttpClientApiV2>(client, sharedSecret);

            var response = await liveRelay.Relay(new LiveRelayRequest
            {
                ChannelKey = tripChannel,
                Recipients = new List<string> { watcher.OdinId.DomainName },
                Blob = blob
            });
            Assert.That(response.IsSuccessStatusCode, Is.True, $"share failed: {response.StatusCode}");

            // (4) Watcher receives it live and decodes the coordinate.
            var received = await LiveRelayTestHelpers.WaitForLiveRelayAsync(socket, watcherAppSecret, TimeSpan.FromSeconds(20));
            Assert.That(received, Is.Not.Null, "watcher did not receive the shared location");
            Assert.That(received.ChannelKey, Is.EqualTo(tripChannel), "client routes by channel to the open session");
            Assert.That(received.SenderOdinId, Is.EqualTo(sharer.OdinId.DomainName), "who is at this position");
            Assert.That(received.ReceivedAt, Is.GreaterThan(0), "freshness for the map");

            var decoded = OdinSystemSerializer.Deserialize<GpsPoint>(
                Convert.FromBase64String(received.Blob).ToStringFromUtf8Bytes());
            Assert.That(decoded.Lat, Is.EqualTo(point.Lat));
            Assert.That(decoded.Lon, Is.EqualTo(point.Lon));

            await LiveRelayTestHelpers.CloseQuietlyAsync(socket);
        }
        finally
        {
            await ownerSharer.Connections.DisconnectFrom(watcher.OdinId);
            await ownerWatcher.Connections.DisconnectFrom(sharer.OdinId);
        }
    }
}
