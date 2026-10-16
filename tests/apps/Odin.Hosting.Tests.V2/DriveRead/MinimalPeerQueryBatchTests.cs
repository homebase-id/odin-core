using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;

namespace Odin.Hosting.Tests.V2.DriveRead;

/// <summary>
/// query-batch with <c>resultShape: minimal</c> on a connected identity's drive: the calling server re-encrypts the
/// headers of a peer answer for its caller, and must pass file refs and paging through as they are.
/// </summary>
[TestFixture]
public class MinimalPeerQueryBatchTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task PagesAPeersDriveWithTheSameRowsAsFullHeaders()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var drive = TargetDrive.NewTargetDrive();
        await sam.Admin.CreateDrive(drive, "Sam's drive", allowAnonymousReads: false);
        await frodo.Admin.CreateDrive(drive, "Frodo's drive", allowAnonymousReads: false);
        await PeerFlow.ConnectAsync(frodo, sam, drive, DrivePermission.Read);
        var files = new[]
        {
            await AppFileUploads.SeedTextFileAsync(sam, drive, "one"),
            await AppFileUploads.SeedTextFileAsync(sam, drive, "two"),
            await AppFileUploads.SeedTextFileAsync(sam, drive, "three"),
        };

        var full = await QueryAsync(frodo, sam, drive, QueryBatchResultShape.Full);
        var minimal = await QueryAsync(frodo, sam, drive, QueryBatchResultShape.Minimal);

        var expected = files.Take(2).Select(f => f.FileId);
        Assert.That(full.SearchResults.Select(h => h.FileId), Is.EqualTo(expected));
        Assert.That(minimal.FileRefs?.Select(r => r.FileId), Is.EqualTo(expected));
        Assert.That(minimal.FileRefs!.Select(r => r.VersionTag), Is.EqualTo(full.SearchResults.Select(h => h.FileMetadata.VersionTag)));
        Assert.That(minimal.SearchResults, Is.Null);
        Assert.That(minimal.CursorState, Is.EqualTo(full.CursorState));
        // Two of three rows read: both shapes must say more follow (the peer mapping used to drop hasMoreRows)
        Assert.That((full.HasMoreRows, minimal.HasMoreRows), Is.EqualTo((true, true)));
    }

    private static async Task<QueryBatchResponse> QueryAsync(OwnerSession frodo, OwnerSession sam, TargetDrive drive,
        QueryBatchResultShape shape)
    {
        var response = await frodo.Drives.Peer.QueryBatchAsync(sam.Identity, drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = drive },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                MaxRecords = 2,
                ResultShape = shape,
                Sorting = QueryBatchSortField.AnyChangeDate,
                Ordering = QueryBatchSortOrder.OldestFirst
            }
        });
        Assert.That(response.IsSuccessStatusCode, Is.True, $"{shape} peer query failed: {response.StatusCode} {response.Error?.Content}");
        return response.Content!;
    }
}
