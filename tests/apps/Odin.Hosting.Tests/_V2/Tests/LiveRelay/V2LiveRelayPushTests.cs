using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Controllers.OwnerToken.Notifications;
using Odin.Hosting.Tests._Universal.ApiClient.Notifications;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.AppNotifications.Data;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Drives;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._V2.Tests.LiveRelay;

/// <summary>
/// A LiveRelay message can carry a push that wakes the recipient's app: the ring for a P2P call, or
/// a silent "send me your location". The push is observed through the recipient's notification list,
/// which the enqueue writes before the outbox item, so no push relay is needed.
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
                Recipients = new List<OdinId> { frodo.OdinId }, // ignored: no fan-out from the recipient
            };

            var relayResponse = await LiveRelayTestHelpers.SendRelayAsync(frodo, frodoAppToken, frodoAppSecret,
                channelKey, new List<string> { sam.OdinId.DomainName }, "b2ZmZXI=", push);
            Assert.That(relayResponse.IsSuccessStatusCode, Is.True, $"relay failed: {relayResponse.StatusCode}");

            // The relay itself still reaches the socket.
            var received = await LiveRelayTestHelpers.WaitForLiveRelayAsync(samSocket, samAppSecret, TimeSpan.FromSeconds(20));
            Assert.That(received, Is.Not.Null, "the push must not cost the socket delivery");
            Assert.That(received.ChannelKey, Is.EqualTo(channelKey));

            var notification = await ownerSam.AppNotifications.WaitForNotification(n => n.Options?.TagId == tagId, TimeSpan.FromSeconds(15));
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
    public async Task Relay_WithRing_DerivesTheCallDefaultsOnTheRecipient()
    {
        // The whole ring API for a calling app: kind, the call id as tag, a line of text.
        var frodo = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);
        var ownerSam = _scaffold.CreateOwnerApiClientRedux(sam);

        var appId = Guid.NewGuid();
        var (frodoAppToken, frodoAppSecret, _, _) =
            await LiveRelayTestHelpers.ConnectAndSetupAppAsync(ownerFrodo, ownerSam, frodo, sam, appId);

        try
        {
            var callId = Guid.NewGuid();
            var relayResponse = await LiveRelayTestHelpers.SendRelayAsync(frodo, frodoAppToken, frodoAppSecret,
                Guid.NewGuid(), new List<string> { sam.OdinId.DomainName }, "b2ZmZXI=",
                new AppNotificationOptions { Kind = PushKind.Ring, TypeId = Guid.NewGuid(), TagId = callId, UnEncryptedMessage = "Frodo is calling" });
            Assert.That(relayResponse.IsSuccessStatusCode, Is.True, $"relay failed: {relayResponse.StatusCode}");

            var notification = await ownerSam.AppNotifications.WaitForNotification(n => n.Options?.TagId == callId, TimeSpan.FromSeconds(15));
            Assert.That(notification, Is.Not.Null, "the ring never landed on the recipient");
            Assert.That(notification.Options.Kind, Is.EqualTo(PushKind.Ring));
            Assert.That(notification.Options.TimeToLiveSeconds, Is.EqualTo(PushKindDefaults.RingTimeToLiveSeconds), "a ring expires");
            Assert.That(notification.Options.CollapseId, Is.EqualTo($"call-{callId:N}"), "a hangup with the same id retracts it");
            Assert.That(notification.Options.TimeSensitive, Is.True);
            Assert.That(notification.Options.Silent, Is.False);
        }
        finally
        {
            await LiveRelayTestHelpers.DisconnectAsync(ownerFrodo, ownerSam, frodo, sam);
        }
    }

    [Test]
    public async Task DeviceRegistration_StoresAndEchoesTheVoipToken()
    {
        var sam = TestIdentities.Samwise;
        var ownerSam = _scaffold.CreateOwnerApiClientRedux(sam);

        try
        {
            var subscribe = await ownerSam.AppNotifications.SubscribeFirebase(new PushNotificationSubscribeFirebaseRequest
            {
                FriendlyName = "Sam's iPhone",
                DeviceToken = "fcm-token-123",
                DevicePlatform = "ios",
                VoipDeviceToken = "pushkit-token-456",
            });
            Assert.That(subscribe.IsSuccessStatusCode, Is.True, $"subscribe failed: {subscribe.StatusCode}");

            var subscription = await ownerSam.AppNotifications.GetSubscription();
            Assert.That(subscription.IsSuccessStatusCode, Is.True, $"get subscription failed: {subscription.StatusCode}");
            Assert.That(subscription.Content!.FirebaseDeviceToken, Is.EqualTo("fcm-token-123"));
            Assert.That(subscription.Content.VoipDeviceToken, Is.EqualTo("pushkit-token-456"), "the client must be able to verify its PushKit token is registered");

            // Without a VoIP token the field reads back empty, which is what every non-iOS device looks like.
            await ownerSam.AppNotifications.SubscribeFirebase(new PushNotificationSubscribeFirebaseRequest
            {
                FriendlyName = "Sam's Pixel", DeviceToken = "fcm-token-789", DevicePlatform = "android",
            });
            var android = await ownerSam.AppNotifications.GetSubscription();
            Assert.That(android.Content!.VoipDeviceToken, Is.Null.Or.Empty, $"got {android.Content.VoipDeviceToken}");
        }
        finally
        {
            // The test host has no push relay; a subscription left behind makes every later push
            // to Sam log an error and fail another fixture's log assertion.
            await ownerSam.AppNotifications.UnsubscribeAll();
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
        var (frodoAppToken, frodoAppSecret, _, _) = await LiveRelayTestHelpers.ConnectAndSetupAppAsync(
            ownerFrodo, ownerSam, frodo, sam, Guid.NewGuid(), samAppId: Guid.NewGuid());

        try
        {
            var tagId = Guid.NewGuid();
            var relayResponse = await LiveRelayTestHelpers.SendRelayAsync(frodo, frodoAppToken, frodoAppSecret,
                Guid.NewGuid(), new List<string> { sam.OdinId.DomainName }, "b2ZmZXI=",
                new AppNotificationOptions { TypeId = Guid.NewGuid(), TagId = tagId, UnEncryptedMessage = "Frodo is calling" });
            Assert.That(relayResponse.IsSuccessStatusCode, Is.True, $"relay failed: {relayResponse.StatusCode}");

            // No socket can prove hop 2 finished (Sam has no socket for Frodo's app), so give it a moment.
            await Task.Delay(1000);
            var list = await ListNotificationsAsync(ownerSam);
            Assert.That(list.Any(n => n.Options?.TagId == tagId), Is.False,
                "a push for an app the recipient does not have must leave no trace; list was: " + AppNotificationsApiClient.Describe(list));
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
        var (frodoAppToken, frodoAppSecret, samAppToken, samAppSecret) =
            await LiveRelayTestHelpers.ConnectAndSetupAppAsync(ownerFrodo, ownerSam, frodo, sam, appId);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var samSocket = await LiveRelayTestHelpers.ConnectAppSocketAsync(sam.OdinId, samAppToken, cts.Token);
            await LiveRelayTestHelpers.DoHandshakeAsync(samSocket, samAppSecret, new List<TargetDrive>(), cts.Token);

            var before = (await ListNotificationsAsync(ownerSam)).Count;

            var relayResponse = await LiveRelayTestHelpers.SendRelayAsync(frodo, frodoAppToken, frodoAppSecret,
                Guid.NewGuid(), new List<string> { sam.OdinId.DomainName }, "cG9zaXRpb24=");
            Assert.That(relayResponse.IsSuccessStatusCode, Is.True);

            // The socket frame proves hop 2 ran; without a push there is nothing after it.
            var received = await LiveRelayTestHelpers.WaitForLiveRelayAsync(samSocket, samAppSecret, TimeSpan.FromSeconds(20));
            Assert.That(received, Is.Not.Null, "relay was not delivered");

            var after = await ListNotificationsAsync(ownerSam);
            Assert.That(after.Count, Is.EqualTo(before),
                "a relay without a push must not touch the notification list; list was: " + AppNotificationsApiClient.Describe(after));

            await LiveRelayTestHelpers.CloseQuietlyAsync(samSocket);
        }
        finally
        {
            await LiveRelayTestHelpers.DisconnectAsync(ownerFrodo, ownerSam, frodo, sam);
        }
    }

    [Test]
    public async Task Relay_WithInvalidPush_IsRejectedAtHop1()
    {
        // The limits themselves are unit-tested (PushDeliveryOptionsValidationTests); this proves the
        // relay endpoint applies them before fanning out and answers 400.
        var frodo = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);
        var ownerSam = _scaffold.CreateOwnerApiClientRedux(sam);

        var appId = Guid.NewGuid();
        var (frodoAppToken, frodoAppSecret, _, _) =
            await LiveRelayTestHelpers.ConnectAndSetupAppAsync(ownerFrodo, ownerSam, frodo, sam, appId);

        try
        {
            var response = await LiveRelayTestHelpers.SendRelayAsync(frodo, frodoAppToken, frodoAppSecret,
                Guid.NewGuid(), new List<string> { sam.OdinId.DomainName }, "b2ZmZXI=",
                new AppNotificationOptions { TypeId = Guid.Empty });
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), $"empty TypeId: got {response.StatusCode}");
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
}
