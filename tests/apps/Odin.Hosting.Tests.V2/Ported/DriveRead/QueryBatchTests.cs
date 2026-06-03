using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Read;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;

namespace Odin.Hosting.Tests.V2.Ported.DriveRead;

/// <summary>
/// Port of <c>_V2/Tests/Drive/DriveReaderTests/QueryBatchTests_Anon</c> + <c>QueryBatchTests_Secured</c>.
/// Combined here because the new framework's <see cref="CallerSpec"/> + <see cref="DriveSpec"/>
/// parameterization makes anon-vs-secured a single dimension of the fan-out. Three [Test] methods
/// (Batch, SmartBatch, BatchCollection) × ten caller-and-drive variants. The CDN caller case from
/// the original (returning <c>Unauthorized</c>) is deferred to phase 4 when the CDN client wrapper
/// lands.
/// </summary>
[TestFixture]
public class QueryBatchTests : V2Fixture
{
    private const int FileType1 = 100;
    private const int FileType2 = 202;

    public static IEnumerable<object[]> AllCases()
    {
        // Anon-drive: every caller (Read or Write) can query — drive's anon-readable.
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.OK];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];

        // Secured-drive: only read-permissioned callers (and owner) succeed.
        yield return [CallerSpec.Guest(DriveSpec.Secured(), DrivePermission.Read), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Secured(), DrivePermission.Read), HttpStatusCode.OK];
        yield return [CallerSpec.Guest(DriveSpec.Secured(), DrivePermission.Write), HttpStatusCode.Forbidden];
        yield return [CallerSpec.App(DriveSpec.Secured(), DrivePermission.Write), HttpStatusCode.Forbidden];
        yield return [CallerSpec.Owner(DriveSpec.Secured()), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(AllCases))]
    public async Task CanQueryBatch(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var file1 = await UploadSampleFile(owner, spec.TargetDrive, FileType1);
        var file2 = await UploadSampleFile(owner, spec.TargetDrive, FileType1);

        var response = await caller.Drives.Reader.GetBatchAsync(spec.TargetDrive.Alias, BuildSingleTypeRequest(spec.TargetDrive, FileType1));

        Assert.That(response.StatusCode, Is.EqualTo(expected));
        if (expected != HttpStatusCode.OK) return;

        var batch = response.Content!;
        Assert.That(batch.SearchResults.SingleOrDefault(r => r.FileId == file1.FileId), Is.Not.Null);
        Assert.That(batch.SearchResults.SingleOrDefault(r => r.FileId == file2.FileId), Is.Not.Null);
    }

    [Test, TestCaseSource(nameof(AllCases))]
    public async Task CanQuerySmartBatch(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var file1 = await UploadSampleFile(owner, spec.TargetDrive, FileType1);
        var file2 = await UploadSampleFile(owner, spec.TargetDrive, FileType1);

        var response = await caller.Drives.Reader.GetSmartBatchAsync(spec.TargetDrive.Alias, BuildSingleTypeRequest(spec.TargetDrive, FileType1));

        Assert.That(response.StatusCode, Is.EqualTo(expected));
        if (expected != HttpStatusCode.OK) return;

        var batch = response.Content!;
        Assert.That(batch.SearchResults.SingleOrDefault(r => r.FileId == file1.FileId), Is.Not.Null);
        Assert.That(batch.SearchResults.SingleOrDefault(r => r.FileId == file2.FileId), Is.Not.Null);
    }

    [Test, TestCaseSource(nameof(AllCases))]
    public async Task CanQueryBatchCollection(CallerSpec spec, HttpStatusCode singleBatchExpected)
    {
        // Unlike single-drive Batch/SmartBatch, the V2 BatchCollection controller doesn't 403 on a
        // drive the caller lacks read access for — it returns the collection with empty result
        // sections for inaccessible drives (V2DriveBatchQueryController calls fs.Query.GetBatchCollection
        // which filters by ACL rather than gating at the controller). So callers that single-Batch
        // would Forbid still get 200 here, with no matching files. We derive "should see results" from
        // singleBatchExpected — OK means the caller has read access; anything else means they don't.
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var file1 = await UploadSampleFile(owner, spec.TargetDrive, FileType1);
        var file2 = await UploadSampleFile(owner, spec.TargetDrive, FileType2);

        var driveAlias = spec.TargetDrive.Alias;
        var q1 = new CollectionQueryParamSectionV2
        {
            Name = "q1",
            DriveId = driveAlias,
            QueryParams = new FileQueryParams { FileType = [FileType1] },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        };
        var q2 = new CollectionQueryParamSectionV2
        {
            Name = "q2",
            DriveId = driveAlias,
            QueryParams = new FileQueryParams { FileType = [FileType2] },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        };

        var response = await caller.Drives.Reader.GetBatchCollectionAsync(new QueryBatchCollectionRequestV2
        {
            Queries = [q1, q2]
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var batches = response.Content!.Results;
        Assert.That(batches, Is.Not.Empty);

        var batch1 = batches.SingleOrDefault(b => b.Name == "q1");
        Assert.That(batch1, Is.Not.Null);
        var batch2 = batches.SingleOrDefault(b => b.Name == "q2");
        Assert.That(batch2, Is.Not.Null);

        if (singleBatchExpected != HttpStatusCode.OK) return;

        Assert.That(batch1!.SearchResults.SingleOrDefault(r => r.FileId == file1.FileId), Is.Not.Null);
        Assert.That(batch2!.SearchResults.SingleOrDefault(r => r.FileId == file2.FileId), Is.Not.Null);
    }

    private static QueryBatchRequest BuildSingleTypeRequest(TargetDrive targetDrive, int fileType) =>
        new()
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = targetDrive,
                FileType = [fileType],
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        };

    private static async Task<dynamic> UploadSampleFile(OwnerSession owner, TargetDrive drive, int fileType)
    {
        var metadata = SampleMetadataData.Create(fileType: fileType);
        // Files always carry Anonymous ACL — drive-level ACL governs access. Mirrors the original
        // tests' shape, where the anon-vs-secured axis comes from the drive's AllowAnonymousReads.
        metadata.AccessControlList = AccessControlList.Anonymous;
        var response = await owner.Drives.Writer.UploadNewMetadata(drive.Alias, metadata);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"owner upload failed: {response.StatusCode}");
        return response.Content!;
    }
}
