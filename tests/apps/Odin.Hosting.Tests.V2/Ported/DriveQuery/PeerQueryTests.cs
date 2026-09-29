using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests._Universal.ApiClient.Peer.Query;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;

namespace Odin.Hosting.Tests.V2.Ported.DriveQuery;

/// <summary>
/// Port of <c>_Universal/DriveTests/Query/PeerQueryTests</c>. Querying another identity over peer must
/// apply that identity's ACLs, not the caller's hopes: Frodo's posts are restricted to Connected
/// members of the Mordor Crew circle, so Sam (who holds it) gets both, while Pippin (connected, but
/// only in Hobbits) and Merry (not connected at all) get nothing.
/// </summary>
/// <remarks>
/// Drives the <b>V1</b> peer-query endpoint (<c>/transit/query/batch</c>) through the in-process host.
/// <see cref="V1Handles"/> carries no peer-query client, so the Refit interface
/// <see cref="IUniversalRefitPeerQuery"/> is bound directly per caller via
/// <see cref="OwnerSession.RefitFor{T}"/> — the same interface
/// <see cref="UniversalPeerQueryApiClient"/> wraps, reached the way the README sanctions rather than by
/// hand-rolling a client with someone else's factory. All four identities are hosted because all four
/// are genuinely resolved: Frodo serves the query and the other three each send one.
///
/// No caller matrix; the original had none. Frodo is the fixture's primary identity only in the sense
/// of being the queried one — every request here is made by an explicitly named owner session.
/// </remarks>
[TestFixture]
public class PeerQueryTests : V2Fixture
{
    protected override string[] HostIdentities =>
        [Identities.Pippin, Identities.Sam, Identities.Merry, Identities.Frodo];

    [Test]
    public async Task PeerQueryBatchEnforcesPermissionsOnAnonymousDrive()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var pippin = await LoginAsOwner(Identities.Pippin);
        var merry = await LoginAsOwner(Identities.Merry);

        var scenarioConfig = await QueryScenario.ConfigureScenario1Async(frodo, sam, pippin);

        var peerQueryBatchRequest = new PeerQueryBatchRequest()
        {
            OdinId = frodo.Identity,
            QueryParams = new FileQueryParamsV1()
            {
                TargetDrive = scenarioConfig.TargetDrive,
                FileType = [scenarioConfig.FileType]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        };

        // Assert
        // Sam queries frodo over peer (as he would from his owner feed)
        var samOwnerClientQueryResponse = await sam.RefitFor<IUniversalRefitPeerQuery>().GetBatch(peerQueryBatchRequest);
        Assert.That(samOwnerClientQueryResponse.IsSuccessStatusCode, Is.True);
        Assert.That(samOwnerClientQueryResponse.Content.SearchResults.Count(), Is.EqualTo(2));

        // Pippin queries frodo over peer (as he would from his owner feed)
        var pippinOwnerClientQueryResponse = await pippin.RefitFor<IUniversalRefitPeerQuery>().GetBatch(peerQueryBatchRequest);
        Assert.That(pippinOwnerClientQueryResponse.IsSuccessStatusCode, Is.True);
        Assert.That(pippinOwnerClientQueryResponse.Content.SearchResults, Is.Empty);

        // Merry queries frodo over peer (as he would from his owner feed)
        var merryOwnerClientQueryResponse = await merry.RefitFor<IUniversalRefitPeerQuery>().GetBatch(peerQueryBatchRequest);
        Assert.That(merryOwnerClientQueryResponse.IsSuccessStatusCode, Is.True);
        Assert.That(merryOwnerClientQueryResponse.Content.SearchResults, Is.Empty);
    }
}
