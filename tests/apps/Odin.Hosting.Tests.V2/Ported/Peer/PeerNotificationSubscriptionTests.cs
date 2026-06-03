using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Cap 4a of the chat-kmp V2 peer transport: REST management of subscriptions to live notifications
/// on drives hosted by other identities (<c>/api/v2/peer/notify/subscriptions/push-notification</c>).
/// Based on the V1 <c>_Universal/Peer/PeerAppNotificationsWebSocket</c> subscription client. The
/// websocket delivery itself is covered by a WebScaffold test (the fast framework does not host WS).
/// </summary>
[TestFixture]
public class PeerNotificationSubscriptionTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo];

    [Test]
    public async Task Subscribe_List_Unsubscribe_RoundTrips()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var client = new PeerNotificationV2Client(owner.Identity, owner.Factory);

        var peer = (OdinId)Identities.Sam;
        var subscriptionId = Guid.NewGuid();

        var subscribe = await client.SubscribeAsync(peer, subscriptionId);
        Assert.That(subscribe.IsSuccessStatusCode, Is.True, $"subscribe failed: {subscribe.StatusCode}");

        var afterSubscribe = await client.GetSubscriptionsAsync();
        Assert.That(afterSubscribe.IsSuccessStatusCode, Is.True, $"list failed: {afterSubscribe.StatusCode}");
        Assert.That(afterSubscribe.Content!.Any(s => s.SubscriptionId == subscriptionId && s.Identity == peer), Is.True,
            "subscription should be listed after subscribe");

        var unsubscribe = await client.UnsubscribeAsync(peer, subscriptionId);
        Assert.That(unsubscribe.IsSuccessStatusCode, Is.True, $"unsubscribe failed: {unsubscribe.StatusCode}");

        var afterUnsubscribe = await client.GetSubscriptionsAsync();
        Assert.That(afterUnsubscribe.IsSuccessStatusCode, Is.True);
        Assert.That(afterUnsubscribe.Content!.Any(s => s.SubscriptionId == subscriptionId), Is.False,
            "subscription should be gone after unsubscribe");
    }

    [Test]
    public async Task Subscribe_EmptySubscriptionId_IsRejected()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var client = new PeerNotificationV2Client(owner.Identity, owner.Factory);

        var response = await client.SubscribeAsync((OdinId)Identities.Sam, Guid.Empty);
        Assert.That(response.IsSuccessStatusCode, Is.False,
            $"empty subscription id should be rejected; got {response.StatusCode}");
    }
}
