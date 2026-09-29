#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Home.Service;
using Odin.Hosting.Tests.BuiltIn.Home;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Home;

/// <summary>
/// Port of <c>BuiltIn/Home/HomeQueryBatchCollectionCachingTests</c>. The anonymous home
/// query-batch-collection endpoint and the cache in front of it: results are cached, and adding a
/// channel file or calling invalidate clears them.
/// </summary>
/// <remarks>
/// Checked port.
/// <list type="bullet">
/// <item><c>#if DEBUG</c> is carried verbatim from the original. <c>HomeCachingService.CacheMiss</c>
/// and <c>ResetCacheStats</c> only exist in a Debug build, so the whole file compiles away in
/// Release — which is why these three cases never run in the CI Release build either.</item>
/// <item>No caller matrix in the original and none here — the endpoint is anonymous.</item>
/// <item>The original pinned Pippin; nothing in the assertions names an identity, so this uses the
/// fixture default.</item>
/// <item><b>Carried hazard:</b> <c>HomeCachingService.CacheMiss</c> is a <i>process-wide static</i>,
/// while V2 fixtures run under <c>ParallelScope.Fixtures</c>. It is only ever incremented from
/// <c>HomeCachingService.GetResult</c>, i.e. from the home cacheable <c>/qbc</c> endpoint, and this
/// is the only fixture in the suite that calls it — so the counts are deterministic today. A second
/// fixture hitting that endpoint would make both of them flaky. Not fixed here (a port is a move);
/// the fix would be to make the counter per-tenant or to read the cache stats through the host.</item>
/// <item>The original's <c>ClassicAssert.IsTrue(x == y, "msg")</c> assertions become
/// <c>Assert.That(x, Is.EqualTo(y))</c>, so a failure prints the counts.</item>
/// </list>
/// </remarks>
[TestFixture]
public class HomeQueryBatchCollectionCachingTests : V2Fixture
{
    [Test]
    public async Task CanQueryHomeDataEndPoint()
    {
        var owner = await LoginAsOwner();
        var seeded = await UploadData(owner);

        //
        // QueryBatchCollection, one section per seeded drive, each pinned to that drive's file
        //
        var sections = seeded.Select((s, i) => new CollectionQueryParamSection()
        {
            Name = $"s{i + 1}",
            QueryParams = new FileQueryParamsV1()
            {
                TargetDrive = s.Drive,
                ClientUniqueIdAtLeastOne = new List<Guid>() { s.Metadata.AppData.UniqueId.GetValueOrDefault() }
            }
        }).ToList();

        var queryResult = await QueryBatchCollectionAsync(sections);

        AssertOneSectionEach(queryResult, sections,
            (section, i) => header => header.FileId == seeded[i].UploadResult.File.FileId);
    }

    [Test]
    public Task CanInvalidateCache() => AssertQueryIsCachedUntilAsync(async (_, _) =>
    {
        using var anonClient = Host.CreateAnonymousClient(PrimaryIdentity);
        var svc = RestService.For<IRefitHomeDriveQuery>(anonClient);

        var invalidateCacheResponse = await svc.InvalidateCache();
        Assert.That(invalidateCacheResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    });

    [Test]
    public Task CanInvalidateCache_ByAddingFileToChannel() => AssertQueryIsCachedUntilAsync(
        (owner, drives) => UploadStandardRandomFileHeadersUsingOwnerApi(
            owner, drives[0], AccessControlList.Anonymous, HomeCachingService.ChannelFileType));

    /// <summary>
    /// The shape both invalidation tests share: seed three drives, query twice (one miss, then a
    /// hit), run <paramref name="invalidate"/>, and query once more expecting a fresh miss.
    /// </summary>
    private async Task AssertQueryIsCachedUntilAsync(Func<OwnerSession, List<TargetDrive>, Task> invalidate)
    {
        var owner = await LoginAsOwner();
        var drives = (await UploadData(owner)).Select(s => s.Drive).ToList();

        HomeCachingService.ResetCacheStats();
        Assert.That(HomeCachingService.CacheMiss, Is.EqualTo(0));

        await QueryData(drives.ToArray());
        Assert.That(HomeCachingService.CacheMiss, Is.EqualTo(1), "Cache should have not been used");

        await QueryData(drives.ToArray());
        Assert.That(HomeCachingService.CacheMiss, Is.EqualTo(1), "cache misses should not have changed.");

        await invalidate(owner, drives);

        await QueryData(drives.ToArray());
        Assert.That(HomeCachingService.CacheMiss, Is.EqualTo(2), "cache miss should have increased");
    }

    private sealed record SeededDrive(TargetDrive Drive, UploadResult UploadResult, UploadFileMetadata Metadata);

    private static async Task<(UploadResult uploadResult, UploadFileMetadata uploadedMetadata)>
        UploadStandardRandomFileHeadersUsingOwnerApi(
            OwnerSession owner,
            TargetDrive targetDrive,
            AccessControlList acl = null,
            int fileType = HomeCachingService.PostFileType)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            IsEncrypted = false,
            AllowDistribution = false,
            AppData = new()
            {
                FileType = fileType,
                Content = $"Some json content {Guid.NewGuid()}",
                UniqueId = Guid.NewGuid(),
            },
            AccessControlList = acl ?? AccessControlList.OwnerOnly
        };

        var response = await owner.V1.Drive.UploadNewMetadata(targetDrive, fileMetadata, FileSystemType.Standard);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (response.Content!, fileMetadata);
    }

    /// <summary>
    /// Creates the three anonymously-readable drives every test here queries (two channel drives and
    /// one ordinary drive) and puts one post on each.
    /// </summary>
    private static async Task<List<SeededDrive>> UploadData(OwnerSession owner)
    {
        var drives = new[]
        {
            (Drive: TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType), Name: "Channel Drive 1"),
            (Drive: TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType), Name: "Channel Drive 2"),
            (Drive: TargetDrive.NewTargetDrive(), Name: "Another Drive 3")
        };

        var seeded = new List<SeededDrive>();
        foreach (var (drive, name) in drives)
        {
            await owner.Admin.CreateDrive(drive, name, allowAnonymousReads: true);
            var (uploadResult, metadata) =
                await UploadStandardRandomFileHeadersUsingOwnerApi(owner, drive, AccessControlList.Anonymous);
            seeded.Add(new SeededDrive(drive, uploadResult, metadata));
        }

        return seeded;
    }

    private async Task QueryData(params TargetDrive[] drives)
    {
        var sections = drives.Select(d => new CollectionQueryParamSection()
        {
            Name = d.ToKey().ToBase64(),
            QueryParams = new FileQueryParamsV1()
            {
                TargetDrive = d,
                FileType = new[] { HomeCachingService.PostFileType }
            }
        }).ToList();

        var queryResult = await QueryBatchCollectionAsync(sections);

        AssertOneSectionEach(queryResult, sections,
            (section, i) => header => header.FileMetadata.AppData.FileType == HomeCachingService.PostFileType);
    }

    /// <summary>
    /// POSTs <paramref name="sections"/> to the anonymous home query-batch-collection endpoint and
    /// asserts the response shape both tests rely on: cacheable, OK, and one result per section.
    /// </summary>
    private async Task<QueryBatchCollectionResponse> QueryBatchCollectionAsync(
        List<CollectionQueryParamSection> sections)
    {
        using var anonClient = Host.CreateAnonymousClient(PrimaryIdentity);
        var svc = RestService.For<IRefitHomeDriveQuery>(anonClient);

        var queryBatchResponse = await svc.QueryBatchCollection(new QueryBatchCollectionRequest()
        {
            Queries = sections
        });

        Assert.That(queryBatchResponse.Headers.Contains("Cache-Control"), Is.True,
            "the home query-batch-collection response should carry a Cache-Control header");
        Assert.That(queryBatchResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var queryResult = queryBatchResponse.Content;
        Assert.That(queryResult, Is.Not.Null);
        Assert.That(queryResult!.Results.Count, Is.EqualTo(sections.Count),
            $"Should be {sections.Count} sections");

        return queryResult;
    }

    /// <summary>
    /// For each submitted section, asserts exactly one result carries that name and holds exactly one
    /// file matching <paramref name="matchFile"/>, and exactly one carries that name with
    /// <c>InvalidDrive == false</c>.
    /// </summary>
    private static void AssertOneSectionEach(
        QueryBatchCollectionResponse queryResult,
        List<CollectionQueryParamSection> sections,
        Func<CollectionQueryParamSection, int, Func<SharedSecretEncryptedFileHeader, bool>> matchFile)
    {
        for (var i = 0; i < sections.Count; i++)
        {
            var sectionName = sections[i].Name;
            var isExpectedFile = matchFile(sections[i], i);

            Assert.That(queryResult.Results, Has.Exactly(1).Matches<QueryBatchResponse>(r =>
                    r.Name == sectionName &&
                    r.SearchResults.SingleOrDefault(isExpectedFile) != null),
                $"section {i} is missing its post");

            Assert.That(queryResult.Results, Has.Exactly(1).Matches<QueryBatchResponse>(r =>
                    r.Name == sectionName &&
                    r.InvalidDrive == false),
                $"section {i} reported an invalid drive");
        }
    }
}
#endif
