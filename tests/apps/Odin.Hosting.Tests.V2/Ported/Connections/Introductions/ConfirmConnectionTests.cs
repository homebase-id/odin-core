#nullable enable
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using static Odin.Hosting.Tests.V2.Ported.Connections.Introductions.IntroductionTestUtils;

namespace Odin.Hosting.Tests.V2.Ported.Connections.Introductions;

/// <summary>
/// Port of <c>_Universal/Owner/Connections/Introductions/ConfirmConnectionTests</c>. Two identities
/// introduced by a third auto-connect into the Auto-connected circle; the owner's confirm moves the
/// contact out of it and into Confirmed Connections.
/// </summary>
/// <remarks>
/// <para>
/// Both passive polls are gone: <c>WaitForEmptyOutbox(TransientTempDrive)</c> on the introducer and
/// <c>AwaitIntroductionsProcessing()</c> on each introducee become <c>Sync.DrainOutboxAsync()</c>.
/// They were the same call underneath — a poll of the transient-temp-drive outbox — and the fast
/// host registers the outbox background service but never starts it. Draining the introducee's
/// outbox is what actually sends the introductory connection request
/// (<c>ConnectIntroduceeOutboxWorker</c>), so the substitution is behaviour-preserving rather than
/// merely timing-related.
/// </para>
/// <para>
/// Carried defect, behaviour left exactly as found: the final block has Sam confirm Merry and then
/// re-reads <b>Merry's</b> view of <b>Sam</b> — the record the previous block already moved to
/// Confirmed Connections. Sam's own view of Merry, the thing <c>samConfirmationResponse</c> changed,
/// is never asserted, so those last four assertions pass whatever Sam's confirm did.
/// </para>
/// <para>
/// The trailing <c>Cleanup()</c> (delete every introduction, disconnect every pairing) was lifecycle
/// only and asserted nothing; per-test reset owns it. The <c>DeleteAllIntroductions</c> calls inside
/// <c>Prepare</c> are kept — they are part of the arrange the assertions read against — as
/// <see cref="IntroductionTestUtils.PrepareIntroducerAndClearIntroductionsAsync"/>.
/// <c>SetupCallerWithOwner</c> is not in play — no caller matrix, <c>LoginAsOwner</c> only.
/// </para>
/// </remarks>
[TestFixture]
public class ConfirmConnectionTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Merry, Identities.Sam];

    [Test]
    public async Task CanConfirmConnection()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        await PrepareIntroducerAndClearIntroductionsAsync(frodo, sam, merry);

        var response = await Requests(frodo).SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam.Identity, merry.Identity]
        });

        await frodo.Sync.DrainOutboxAsync();

        var introResult = response.Content!;
        Assert.That(introResult.RecipientStatus[sam.Identity], Is.True);
        Assert.That(introResult.RecipientStatus[merry.Identity], Is.True);

        await merry.Sync.DrainOutboxAsync();
        await sam.Sync.DrainOutboxAsync();

        //validate they are connected
        var samConnectionInfoResponse = await merry.Connections.GetConnectionInfo(sam.Identity);
        Assert.That(samConnectionInfoResponse.IsSuccessStatusCode, Is.True);
        Assert.That(samConnectionInfoResponse.Content!.Status, Is.EqualTo(ConnectionStatus.Connected));
        Assert.That(samConnectionInfoResponse.Content.AccessGrant.CircleGrants,
            Has.Some.Matches<RedactedCircleGrant>(cg => cg.CircleId == SystemCircleConstants.AutoConnectionsCircleId));
        Assert.That(samConnectionInfoResponse.Content.AccessGrant.CircleGrants,
            Has.None.Matches<RedactedCircleGrant>(cg => cg.CircleId == SystemCircleConstants.ConfirmedConnectionsCircleId));

        //merry confirms - now sam should be in a new circle
        var merryConfirmationResponse = await merry.Connections.ConfirmConnection(sam.Identity);
        Assert.That(merryConfirmationResponse.IsSuccessStatusCode, Is.True);

        var samConnectionInfoResponse2 = await merry.Connections.GetConnectionInfo(sam.Identity);
        Assert.That(samConnectionInfoResponse2.IsSuccessStatusCode, Is.True);
        Assert.That(samConnectionInfoResponse2.Content!.AccessGrant.CircleGrants,
            Has.None.Matches<RedactedCircleGrant>(cg => cg.CircleId == SystemCircleConstants.AutoConnectionsCircleId));
        Assert.That(samConnectionInfoResponse2.Content.AccessGrant.CircleGrants,
            Has.Some.Matches<RedactedCircleGrant>(cg => cg.CircleId == SystemCircleConstants.ConfirmedConnectionsCircleId));

        var samConfirmationResponse = await sam.Connections.ConfirmConnection(merry.Identity);
        Assert.That(samConfirmationResponse.IsSuccessStatusCode, Is.True,
            $"status code was {samConfirmationResponse.StatusCode}");

        var merryConnectionInfo = await merry.Connections.GetConnectionInfo(sam.Identity);
        Assert.That(merryConnectionInfo.IsSuccessStatusCode, Is.True);
        Assert.That(merryConnectionInfo.Content!.AccessGrant.CircleGrants,
            Has.None.Matches<RedactedCircleGrant>(cg => cg.CircleId == SystemCircleConstants.AutoConnectionsCircleId));
        Assert.That(merryConnectionInfo.Content.AccessGrant.CircleGrants,
            Has.Some.Matches<RedactedCircleGrant>(cg => cg.CircleId == SystemCircleConstants.ConfirmedConnectionsCircleId));
    }
}
