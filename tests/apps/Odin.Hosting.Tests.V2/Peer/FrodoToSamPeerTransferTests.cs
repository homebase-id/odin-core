#nullable enable
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Peer;

/// <summary>
/// First end-to-end V2 peer-flow sentinel: Frodo connects to Sam (granted Write), Frodo uploads a
/// file destined for Sam, the outbox drains via <see cref="ITestSync.DrainOutboxAsync"/>, Sam's
/// inbox is processed via <see cref="ITestSync.ProcessInboxAsync"/>, and Sam can read the
/// transferred file back via its <c>GlobalTransitId</c>. Exercises both the in-process peer HTTP
/// routing (<see cref="TestPeerHttpClientFactory"/>) and the peer-auth bypass in
/// <c>PeerCapiAuthenticationHandler</c>.
/// </summary>
[TestFixture]
public class FrodoToSamPeerTransferTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task FrodoSendsFile_SamReceivesIt()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        var sharedDrive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(sharedDrive, "Frodo's shared drive");
        await sam.Admin.CreateDrive(sharedDrive, "Sam's shared drive");

        await PeerFlow.ConnectAsync(frodo, sam, sharedDrive, DrivePermission.Write);

        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        metadata.AllowDistribution = true;

        var send = await frodo.Drives.Writer.UploadNewMetadata(
            sharedDrive.Alias,
            metadata,
            transitOptions: new TransitOptions
            {
                Recipients = new System.Collections.Generic.List<string> { sam.Identity },
            });

        Assert.That(send.IsSuccessStatusCode, Is.True, $"Frodo upload failed: {send.StatusCode}");
        var sendResult = send.Content!;
        Assert.That(sendResult.GlobalTransitId, Is.Not.Null,
            "Outbox transfer requires a GlobalTransitId to be assigned on upload.");

        await frodo.Sync.DrainOutboxAsync();
        await sam.Sync.ProcessInboxAsync(sharedDrive);

        // Sam's FileId differs from Frodo's. The canonical probe is by GlobalTransitId.
        var query = await sam.Drives.Reader.GetBatchAsync(sharedDrive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                GlobalTransitId = new[] { sendResult.GlobalTransitId!.Value },
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                MaxRecords = 10,
                IncludeMetadataHeader = true,
            },
        });
        Assert.That(query.IsSuccessStatusCode, Is.True, $"Sam query failed: {query.StatusCode}");
        var hits = query.Content!.SearchResults.ToList();
        Assert.That(hits.Count, Is.EqualTo(1), "Sam should have received exactly one file with that GlobalTransitId.");
        Assert.That(hits[0].FileMetadata.AppData.Content, Is.EqualTo(metadata.AppData.Content),
            "Transferred file's app content should round-trip intact.");
    }
}
