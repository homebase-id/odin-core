using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.AppNotifications.Data;
using Odin.Services.Drives;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._V2.Tests.LiveRelay;

/// <summary>
/// A LiveRelay message can carry a push that wakes the recipient's app: the ring for a P2P call, or
/// a silent "send me your location". The push is observed through the recipient's notification list,
/// which the enqueue writes before the outbox item, so no push relay and no socket are needed.
///
/// Lives in the old WebScaffold framework next to V2LiveRelayTests: the fast host has no peer hop
/// for LiveRelay yet.
/// </summary>
[TestFixture]
public class V2LiveRelayPushTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _scaffold = new WebScaffold(GetType().Name);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>
        {
            TestIdentities.Frodo,
            TestIdentities.Samwise,
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
    public async Task Relay_WithPush_EnqueuesNotificationOnRecipient_WithCallerAsSender()
    {
        var frodo = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);
        var ownerSam = _scaffold.CreateOwnerApiClientRedux(sam);

        var appId = Guid.NewGuid();
        var (frodoAppToken, frodoAppSecret, samAppToken, samAppSecret) =
            await LiveRelayTestHelpers.ConnectAndSetupAppAsync(ownerFrodo, ownerSam, frodo, sam, appId);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var samSocket = await LiveRelayTestHelpers.ConnectAppSocketAsync(sam.OdinId, samAppToken, cts.Token);
            await LiveRelayTestHelpers.DoHandshakeAsync(samSocket, samAppSecret, new List<TargetDrive>(), cts.Token);

            var channelKey = Guid.NewGuid();
            var typeId = Guid.NewGuid();
            var tagId = Guid.NewGuid();
            var push = new AppNotificationOptions
            {
                AppId = Guid.NewGuid(), // a lie; the server must replace it with the caller's app
                TypeId = typeId,
                TagId = tagId,
                UnEncryptedMessage = "Frodo is calling",
                TimeToLiveSeconds = 60,
                CollapseId = $"call-{channelKey:N}",
                TimeSensitive = true,
                PeerSubscriptionId = Guid.NewGuid(), // ignored
                Recipients = new List<Odin.Core.Identity.OdinId> { frodo.OdinId }, // ignored: no fan-out from the recipient
            };

            var relayResponse = await LiveRelayTestHelpers.SendRelayAsync(frodo, frodoAppToken, frodoAppSecret,
                channelKey, new List<string> { sam.OdinId.DomainName }, "b2ZmZXI=", push);
            Assert.That(relayResponse.IsSuccessStatusCode, Is.True, $"relay failed: {relayResponse.StatusCode}");

            // The relay itself still reaches the socket.
            var received = await LiveRelayTestHelpers.WaitForLiveRelayAsync(samSocket, samAppSecret, TimeSpan.FromSeconds(20));
            Assert.That(received, Is.Not.Null, "the push must not cost the socket delivery");
            Assert.That(received.ChannelKey, Is.EqualTo(channelKey));

            var notification = await WaitForNotificationAsync(ownerSam, n => n.Options?.TagId == tagId);
            Assert.That(notification, Is.Not.Null, "no notification with the push's tag id landed on the recipient");
            Assert.That(notification.SenderId, Is.EqualTo(frodo.OdinId.DomainName), "sender must be the authenticated caller");
            Assert.That(notification.Options.AppId, Is.EqualTo(appId), "AppId must be the caller's app, not what the request claimed");
            Assert.That(notification.Options.TypeId, Is.EqualTo(typeId));
            Assert.That(notification.Options.UnEncryptedMessage, Is.EqualTo("Frodo is calling"));
            Assert.That(notification.Options.TimeToLiveSeconds, Is.EqualTo(60));
            Assert.That(notification.Options.CollapseId, Is.EqualTo($"call-{channelKey:N}"));
            Assert.That(notification.Options.TimeSensitive, Is.True);
            Assert.That(notification.Options.PeerSubscriptionId, Is.EqualTo(Guid.Empty), "the peer-subscription route is not this route");
            Assert.That(notification.Options.Recipients, Is.Null.Or.Empty, "the recipient must not fan the push out further");

            await LiveRelayTestHelpers.CloseQuietlyAsync(samSocket);
        }
        finally
        {
            await LiveRelayTestHelpers.DisconnectAsync(ownerFrodo, ownerSam, frodo, sam);
        }
    }

    [Test]
    public async Task Relay_WithPush_ToRecipientWithoutTheApp_IsNotEnqueued()
    {
        var frodo = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);
        var ownerSam = _scaffold.CreateOwnerApiClientRedux(sam);

        // Connected, but Sam registered a different app: not installed means not woken.
        var frodoAppId = Guid.NewGuid();
        var samOtherAppId = Guid.NewGuid();
        var frodoCircleId = await LiveRelayTestHelpers.PrepareAppAccessAsync(ownerFrodo, frodoAppId, TargetDrive.NewTargetDrive());
        var samCircleId = await LiveRelayTestHelpers.PrepareAppAccessAsync(ownerSam, samOtherAppId, TargetDrive.NewTargetDrive());
        await ownerFrodo.Connections.SendConnectionRequest(sam.OdinId, new List<Odin.Core.GuidId> { frodoCircleId });
        await ownerSam.Connections.AcceptConnectionRequest(frodo.OdinId, new List<Odin.Core.GuidId> { samCircleId });
        var (frodoAppToken, frodoAppSecret) = await ownerFrodo.AppManager.RegisterAppClient(frodoAppId);

        try
        {
            var tagId = Guid.NewGuid();
            var relayResponse = await LiveRelayTestHelpers.SendRelayAsync(frodo, frodoAppToken, frodoAppSecret,
                Guid.NewGuid(), new List<string> { sam.OdinId.DomainName }, "b2ZmZXI=",
                new AppNotificationOptions { TypeId = Guid.NewGuid(), TagId = tagId, UnEncryptedMessage = "Frodo is calling" });
            Assert.That(relayResponse.IsSuccessStatusCode, Is.True, $"relay failed: {relayResponse.StatusCode}");

            await Task.Delay(1000);
            var list = await ListNotificationsAsync(ownerSam);
            Assert.That(list.Any(n => n.Options?.TagId == tagId), Is.False,
                "a push for an app the recipient does not have must leave no trace; list was: " + Describe(list));
        }
        finally
        {
            await LiveRelayTestHelpers.DisconnectAsync(ownerFrodo, ownerSam, frodo, sam);
        }
    }

    [Test]
    public async Task Relay_WithoutPush_EnqueuesNothing()
    {
        var frodo = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);
        var ownerSam = _scaffold.CreateOwnerApiClientRedux(sam);

        var appId = Guid.NewGuid();
        var (frodoAppToken, frodoAppSecret, _, _) =
            await LiveRelayTestHelpers.ConnectAndSetupAppAsync(ownerFrodo, ownerSam, frodo, sam, appId);

        try
        {
            var before = (await ListNotificationsAsync(ownerSam)).Count;

            var relayResponse = await LiveRelayTestHelpers.SendRelayAsync(frodo, frodoAppToken, frodoAppSecret,
                Guid.NewGuid(), new List<string> { sam.OdinId.DomainName }, "cG9zaXRpb24=");
            Assert.That(relayResponse.IsSuccessStatusCode, Is.True);

            await Task.Delay(1000);
            var after = await ListNotificationsAsync(ownerSam);
            Assert.That(after.Count, Is.EqualTo(before), "a relay without a push must not touch the notification list; list was: " + Describe(after));
        }
        finally
        {
            await LiveRelayTestHelpers.DisconnectAsync(ownerFrodo, ownerSam, frodo, sam);
        }
    }

    [Test]
    public async Task Relay_WithInvalidPush_IsRejectedAtHop1()
    {
        var frodo = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);
        var ownerSam = _scaffold.CreateOwnerApiClientRedux(sam);

        var appId = Guid.NewGuid();
        var (frodoAppToken, frodoAppSecret, _, _) =
            await LiveRelayTestHelpers.ConnectAndSetupAppAsync(ownerFrodo, ownerSam, frodo, sam, appId);

        try
        {
            var cases = new Dictionary<string, AppNotificationOptions>
            {
                ["empty TypeId"] = new() { TypeId = Guid.Empty },
                ["TTL 0"] = new() { TypeId = Guid.NewGuid(), TimeToLiveSeconds = 0 },
                ["TTL over a day"] = new() { TypeId = Guid.NewGuid(), TimeToLiveSeconds = 86401 },
                ["CollapseId 65 chars"] = new() { TypeId = Guid.NewGuid(), CollapseId = new string('c', 65) },
            };

            foreach (var (name, push) in cases)
            {
                var response = await LiveRelayTestHelpers.SendRelayAsync(frodo, frodoAppToken, frodoAppSecret,
                    Guid.NewGuid(), new List<string> { sam.OdinId.DomainName }, "b2ZmZXI=", push);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), $"{name}: got {response.StatusCode}");
            }
        }
        finally
        {
            await LiveRelayTestHelpers.DisconnectAsync(ownerFrodo, ownerSam, frodo, sam);
        }
    }

    //

    private static async Task<List<AppNotification>> ListNotificationsAsync(OwnerApiClientRedux owner)
    {
        var response = await owner.AppNotifications.GetList(1000);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"notification list failed: {response.StatusCode}");
        return response.Content?.Results ?? new List<AppNotification>();
    }

    private static async Task<AppNotification> WaitForNotificationAsync(OwnerApiClientRedux owner, Func<AppNotification, bool> match)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        List<AppNotification> list = new();
        while (DateTime.UtcNow < deadline)
        {
            list = await ListNotificationsAsync(owner);
            var hit = list.FirstOrDefault(match);
            if (hit != null)
            {
                return hit;
            }
            await Task.Delay(250);
        }

        TestContext.Out.WriteLine("notification list at timeout: " + Describe(list));
        return null;
    }

    private static string Describe(IEnumerable<AppNotification> list) =>
        string.Join(" | ", list.Select(n => $"{n.SenderId} type={n.Options?.TypeId} tag={n.Options?.TagId} app={n.Options?.AppId}"));
}
