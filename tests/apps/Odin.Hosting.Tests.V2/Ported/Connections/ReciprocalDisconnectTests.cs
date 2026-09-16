#nullable enable
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Controllers;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Membership.Connections;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// Port of <c>_Universal/Owner/Connections/ReciprocalDisconnectTests</c>. Disconnecting with
/// <c>notifyRemote</c> tells the peer, so both sides end up disconnected; opting out leaves the
/// intentional asymmetric state the connection machine relies on.
/// </summary>
/// <remarks>
/// <para>
/// The reciprocal notification travels through the sender's outbox, so
/// <c>WaitForEmptyOutbox(TransientTempDrive, 40s)</c> becomes <c>Sync.DrainOutboxAsync()</c> — the
/// fast host registers the outbox background service but never starts it, so the passive poll would
/// have waited out its full timeout and then thrown.
/// </para>
/// <para>
/// <c>notifyRemote</c> is the whole point of both tests and <c>owner.Connections.DisconnectFrom</c>
/// cannot express it, so the disconnect goes through
/// <see cref="IRefitUniversalCircleNetworkConnections"/> via <see cref="OwnerSession.RefitFor{T}"/>
/// — the same interface the original's <c>Network</c> client wrapped, including its
/// <c>notifyRemote: false</c> default.
/// </para>
/// <para>
/// The trailing one-sided disconnect in each test only restored state and asserted nothing; per-test
/// reset owns that. The original's <c>Content == null || Content.Status != Connected</c> becomes
/// <c>Content?.Status</c> against <c>Is.Not.EqualTo(Connected)</c> — the same claim, printing the
/// status instead of <c>Expected: True</c>. <c>SetupCallerWithOwner</c> is not in play — no caller
/// matrix, <c>LoginAsOwner</c> only.
/// </para>
/// </remarks>
[TestFixture]
public class ReciprocalDisconnectTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task DisconnectNotifiesRemoteByDefault()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await frodo.Connections.SendConnectionRequest(sam.Identity, []);
        await sam.Connections.AcceptConnectionRequest(frodo.Identity);

        // Sanity: both sides are connected
        var samViewBefore = await sam.Connections.GetConnectionInfo(frodo.Identity);
        Assert.That(samViewBefore.IsSuccessStatusCode, Is.True);
        Assert.That(samViewBefore.Content!.Status, Is.EqualTo(ConnectionStatus.Connected));

        // Frodo disconnects from Sam using the default (notifyRemote omitted), which now notifies
        // the remote so Sam disconnects too.
        var disconnectResponse = await Disconnect(frodo, sam.Identity, notifyRemote: true);
        Assert.That(disconnectResponse.IsSuccessStatusCode, Is.True);

        // Frodo's side is gone immediately
        var frodoView = await frodo.Connections.GetConnectionInfo(sam.Identity);
        Assert.That(frodoView.Content?.Status, Is.Not.EqualTo(ConnectionStatus.Connected));

        // The reciprocal-disconnect notification is delivered to Sam via Frodo's outbox.
        await frodo.Sync.DrainOutboxAsync();

        // Sam should now also be disconnected from Frodo
        var samViewAfter = await sam.Connections.GetConnectionInfo(frodo.Identity);
        Assert.That(samViewAfter.Content?.Status, Is.Not.EqualTo(ConnectionStatus.Connected),
            "Sam was still connected to Frodo after Frodo disconnected");
    }

    [Test]
    public async Task DisconnectCanOptOutOfNotifyingRemote()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await frodo.Connections.SendConnectionRequest(sam.Identity, []);
        await sam.Connections.AcceptConnectionRequest(frodo.Identity);

        // Passing notifyRemote:false must NOT notify the peer -- this preserves the intentional
        // asymmetric-state behavior the connection state machine relies on for callers that need it
        // (e.g. bad-CAT detection scenarios).
        var disconnectResponse = await Disconnect(frodo, sam.Identity, notifyRemote: false);
        Assert.That(disconnectResponse.IsSuccessStatusCode, Is.True);

        // Frodo's side is gone...
        var frodoView = await frodo.Connections.GetConnectionInfo(sam.Identity);
        Assert.That(frodoView.Content?.Status, Is.Not.EqualTo(ConnectionStatus.Connected));

        // ...but Sam still considers itself connected to Frodo (no reciprocal notification sent).
        var samView = await sam.Connections.GetConnectionInfo(frodo.Identity);
        Assert.That(samView.IsSuccessStatusCode, Is.True);
        Assert.That(samView.Content!.Status, Is.EqualTo(ConnectionStatus.Connected),
            "Sam should still be connected to Frodo after a one-sided disconnect");
    }

    private static Task<ApiResponse<HttpContent>> Disconnect(
        OwnerSession owner, OdinId recipient, bool notifyRemote) =>
        owner.RefitFor<IRefitUniversalCircleNetworkConnections>()
            .Disconnect(new OdinIdRequest { OdinId = recipient }, notifyRemote);
}
