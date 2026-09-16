using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// Assertions that recur across drive fixtures, in the same spirit as <see cref="Peer.PeerFlow"/>:
/// shared test vocabulary rather than a client facade.
/// </summary>
/// <remarks>
/// These exist because the ported fixtures kept re-inlining them — the query-by-datatype check
/// alone had seven copies across two files. Both V1 and V2 overloads are provided: a fixture whose
/// system under test is a V1 endpoint should seed and verify over V1 so the port keeps exercising
/// the path the original did.
/// </remarks>
public static class DriveAsserts
{
    /// <summary>
    /// The drive returns exactly one file for <paramref name="dataType"/>, and it is the expected
    /// one. V1 overload — queries through <see cref="UniversalDriveApiClient"/>.
    /// </summary>
    public static async Task AssertFileFoundByDataType(
        UniversalDriveApiClient client, TargetDrive targetDrive, int dataType, Guid expectedFileId)
    {
        var search = await client.QueryBatch(new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = targetDrive, DataType = [dataType] },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        Assert.That(search.IsSuccessStatusCode, Is.True);
        var hit = search.Content!.SearchResults.SingleOrDefault();
        Assert.That(hit, Is.Not.Null);
        Assert.That(hit!.FileId, Is.EqualTo(expectedFileId));
    }

    /// <summary>
    /// As the V1 overload, querying through the owner's V2 reader instead.
    /// </summary>
    public static async Task AssertFileFoundByDataType(
        OwnerSession owner, TargetDrive targetDrive, int dataType, Guid expectedFileId)
    {
        var search = await owner.Drives.Reader.GetBatchAsync(targetDrive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = targetDrive, DataType = [dataType] },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        Assert.That(search.IsSuccessStatusCode, Is.True);
        var hit = search.Content!.SearchResults.SingleOrDefault();
        Assert.That(hit, Is.Not.Null);
        Assert.That(hit!.FileId, Is.EqualTo(expectedFileId));
    }
}
