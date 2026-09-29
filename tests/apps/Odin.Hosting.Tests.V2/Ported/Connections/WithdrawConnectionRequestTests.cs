#nullable enable
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Controllers;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests.V2.Api;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// Port of <c>_Universal/Owner/Connections/WithdrawConnectionRequestTests</c>. Cancelling a sent
/// connection request with <c>notifyRemote</c> withdraws the recipient's pending copy too; the
/// default cancel is one-sided.
/// </summary>
/// <remarks>
/// <para>
/// The withdrawal notification travels through the sender's outbox, so the original's
/// <c>AwaitIntroductionsProcessing(40s)</c> — a passive poll of the transient-temp-drive outbox —
/// becomes <c>Sync.DrainOutboxAsync()</c>. The fast host registers the outbox background service
/// but never starts it, so the poll would have waited out its timeout and then thrown.
/// </para>
/// <para>
/// <c>notifyRemote</c> is the thing under test and <c>owner.Connections.DeleteSentRequestTo</c>
/// cannot express it, so the cancel goes through <see cref="IRefitUniversalCircleNetworkRequests"/>
/// via <see cref="OwnerSession.RefitFor{T}"/> — the same interface and the same
/// <c>notifyRemote: false</c> default the original's requests client used.
/// </para>
/// <para>
/// The trailing <c>DeleteConnectionRequestFrom</c> in the second test only stopped the stranded
/// pending request leaking into the next test and asserted nothing; per-test reset owns that.
/// <c>SetupCallerWithOwner</c> is not in play — no caller matrix, <c>LoginAsOwner</c> only.
/// </para>
/// </remarks>
[TestFixture]
public class WithdrawConnectionRequestTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task CancelingASentRequestWithNotifyRemoteWithdrawsThePendingRequestOnRecipient()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        var sendResponse = await frodo.Connections.SendConnectionRequest(sam.Identity, []);
        Assert.That(sendResponse.IsSuccessStatusCode, Is.True);

        // Sanity: Sam has the incoming/pending request
        var samPendingBefore = await sam.Connections.GetIncomingRequestFrom(frodo.Identity);
        Assert.That(samPendingBefore.IsSuccessStatusCode, Is.True);
        Assert.That(samPendingBefore.Content, Is.Not.Null);

        // Frodo cancels the sent request, opting in to notify the remote so Sam withdraws the pending request too
        var deleteResponse = await DeleteSentRequestTo(frodo, sam.Identity, notifyRemote: true);
        Assert.That(deleteResponse.IsSuccessStatusCode, Is.True);

        // Frodo's sent request is gone immediately
        var frodoSent = await frodo.Connections.GetOutgoingSentRequestTo(sam.Identity);
        Assert.That(frodoSent.Content is null || frodoSent.StatusCode == HttpStatusCode.NotFound, Is.True);

        // The withdrawal notification is delivered to Sam via Frodo's outbox.
        await frodo.Sync.DrainOutboxAsync();

        // Sam's pending request should now be withdrawn
        var samPendingAfter = await sam.Connections.GetIncomingRequestFrom(frodo.Identity);
        Assert.That(samPendingAfter.Content is null || samPendingAfter.StatusCode == HttpStatusCode.NotFound, Is.True,
            $"Sam still had a pending request from Frodo after Frodo cancelled it (status: {samPendingAfter.StatusCode})");
    }

    [Test]
    public async Task CancelingASentRequestIsOneSidedByDefault()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        var sendResponse = await frodo.Connections.SendConnectionRequest(sam.Identity, []);
        Assert.That(sendResponse.IsSuccessStatusCode, Is.True);

        // Default cancel (notifyRemote omitted) must NOT notify the recipient -- this preserves the
        // intentional asymmetric behavior the connection flow relies on.
        var deleteResponse = await frodo.Connections.DeleteSentRequestTo(sam.Identity);
        Assert.That(deleteResponse.IsSuccessStatusCode, Is.True);

        // Frodo's sent request is gone...
        var frodoSent = await frodo.Connections.GetOutgoingSentRequestTo(sam.Identity);
        Assert.That(frodoSent.Content is null || frodoSent.StatusCode == HttpStatusCode.NotFound, Is.True);

        // ...but Sam still holds the pending request (no withdrawal notification sent).
        var samPending = await sam.Connections.GetIncomingRequestFrom(frodo.Identity);
        Assert.That(samPending.IsSuccessStatusCode, Is.True);
        Assert.That(samPending.Content, Is.Not.Null,
            $"Sam should still have a pending request from Frodo after a one-sided cancel (status: {samPending.StatusCode})");
    }

    private static Task<ApiResponse<HttpContent>> DeleteSentRequestTo(
        OwnerSession owner, OdinId recipient, bool notifyRemote) =>
        owner.RefitFor<IRefitUniversalCircleNetworkRequests>()
            .DeleteSentRequest(new OdinIdRequest { OdinId = recipient }, notifyRemote);
}
