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

        //
        // Create 3 drives and grant ReadWrite
        //
        var channelDrive1 = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        var channelDrive2 = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        var channelDrive3 = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(channelDrive1, "Channel Drive 1", allowAnonymousReads: true);
        await owner.Admin.CreateDrive(channelDrive2, "Channel Drive 2", allowAnonymousReads: true);
        await owner.Admin.CreateDrive(channelDrive3, "Another Drive 3", allowAnonymousReads: true);

        //
        // Upload 3 files
        //
        var header1 = await UploadStandardRandomFileHeadersUsingOwnerApi(owner, channelDrive1, AccessControlList.Anonymous);
        var header2 = await UploadStandardRandomFileHeadersUsingOwnerApi(owner, channelDrive2, AccessControlList.Anonymous);
        var header3 = await UploadStandardRandomFileHeadersUsingOwnerApi(owner, channelDrive3, AccessControlList.Anonymous);

        const string section1Name = "s1";
        const string section2Name = "s2";
        const string section3Name = "s3";

        //
        // QueryBatchCollection
        //
        var sections = new List<CollectionQueryParamSection>()
        {
            new()
            {
                Name = section1Name,
                QueryParams = new FileQueryParamsV1()
                {
                    TargetDrive = channelDrive1,
                    ClientUniqueIdAtLeastOne = new List<Guid>() { header1.uploadedMetadata.AppData.UniqueId.GetValueOrDefault() }
                }
            },
            new()
            {
                Name = section2Name,
                QueryParams = new FileQueryParamsV1()
                {
                    TargetDrive = channelDrive2,
                    ClientUniqueIdAtLeastOne = new List<Guid>() { header2.uploadedMetadata.AppData.UniqueId.GetValueOrDefault() }
                }
            },
            new()
            {
                Name = section3Name,
                QueryParams = new FileQueryParamsV1()
                {
                    TargetDrive = channelDrive3,
                    ClientUniqueIdAtLeastOne = new List<Guid>() { header3.uploadedMetadata.AppData.UniqueId.GetValueOrDefault() }
                }
            }
        };

        using var anonClient = Host.CreateAnonymousClient(PrimaryIdentity);
        var svc = RestService.For<IRefitHomeDriveQuery>(anonClient);

        var queryBatchResponse = await svc.QueryBatchCollection(new QueryBatchCollectionRequest()
        {
            Queries = sections
        });

        Assert.That(queryBatchResponse.Headers.Contains("Cache-Control"), Is.True);
        Assert.That(queryBatchResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var queryResult = queryBatchResponse.Content;
        Assert.That(queryResult, Is.Not.Null);

        Assert.That(queryResult!.Results.Count, Is.EqualTo(3), "Should be 3 sections");

        Assert.That(queryResult.Results, Has.Exactly(1).Matches<QueryBatchResponse>(r =>
            r.Name == section1Name &&
            r.SearchResults.SingleOrDefault(r2 => r2.FileId == header1.uploadResult.File.FileId) != null));

        Assert.That(queryResult.Results, Has.Exactly(1).Matches<QueryBatchResponse>(r =>
            r.Name == section1Name &&
            r.InvalidDrive == false));

        Assert.That(queryResult.Results, Has.Exactly(1).Matches<QueryBatchResponse>(r =>
            r.Name == section2Name &&
            r.SearchResults.SingleOrDefault(r2 => r2.FileId == header2.uploadResult.File.FileId) != null));

        Assert.That(queryResult.Results, Has.Exactly(1).Matches<QueryBatchResponse>(r =>
            r.Name == section2Name &&
            r.InvalidDrive == false));

        Assert.That(queryResult.Results, Has.Exactly(1).Matches<QueryBatchResponse>(r =>
            r.Name == section3Name &&
            r.SearchResults.SingleOrDefault(r2 => r2.FileId == header3.uploadResult.File.FileId) != null));

        Assert.That(queryResult.Results, Has.Exactly(1).Matches<QueryBatchResponse>(r =>
            r.Name == section3Name &&
            r.InvalidDrive == false));
    }

    [Test]
    public async Task CanInvalidateCache()
    {
        var owner = await LoginAsOwner();

        using var anonClient = Host.CreateAnonymousClient(PrimaryIdentity);
        var svc = RestService.For<IRefitHomeDriveQuery>(anonClient);

        var drives = await UploadData(owner);

        HomeCachingService.ResetCacheStats();
        Assert.That(HomeCachingService.CacheMiss, Is.EqualTo(0));

        await QueryData(drives.ToArray());
        Assert.That(HomeCachingService.CacheMiss, Is.EqualTo(1), "Cache should have not been used");

        await QueryData(drives.ToArray());
        Assert.That(HomeCachingService.CacheMiss, Is.EqualTo(1), "cache misses should not have changed.");

        //
        // Invalidate and query again
        //
        var invalidateCacheResponse = await svc.InvalidateCache();
        Assert.That(invalidateCacheResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await QueryData(drives.ToArray());

        Assert.That(HomeCachingService.CacheMiss, Is.EqualTo(2), "cache miss should have increased");

        HomeCachingService.ResetCacheStats();
    }

    [Test]
    public async Task CanInvalidateCache_ByAddingFileToChannel()
    {
        var owner = await LoginAsOwner();

        var drives = await UploadData(owner);

        HomeCachingService.ResetCacheStats();
        Assert.That(HomeCachingService.CacheMiss, Is.EqualTo(0));

        await QueryData(drives.ToArray());
        Assert.That(HomeCachingService.CacheMiss, Is.EqualTo(1), "Cache should have not been used");

        await QueryData(drives.ToArray());
        Assert.That(HomeCachingService.CacheMiss, Is.EqualTo(1), "cache misses should not have changed.");

        //
        // Add a new channel
        //
        await UploadStandardRandomFileHeadersUsingOwnerApi(owner, drives[0], AccessControlList.Anonymous,
            HomeCachingService.ChannelFileType);

        await QueryData(drives.ToArray());
        Assert.That(HomeCachingService.CacheMiss, Is.EqualTo(2), "cache miss should have increased");

        HomeCachingService.ResetCacheStats();
    }

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

    private static async Task<List<TargetDrive>> UploadData(OwnerSession owner)
    {
        //
        // Create 3 drives and grant ReadWrite
        //
        var channelDrive1 = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        var channelDrive2 = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        var channelDrive3 = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(channelDrive1, "Channel Drive 1", allowAnonymousReads: true);
        await owner.Admin.CreateDrive(channelDrive2, "Channel Drive 2", allowAnonymousReads: true);
        await owner.Admin.CreateDrive(channelDrive3, "Another Drive 3", allowAnonymousReads: true);

        //
        // Upload 3 files
        //
        await UploadStandardRandomFileHeadersUsingOwnerApi(owner, channelDrive1, AccessControlList.Anonymous);
        await UploadStandardRandomFileHeadersUsingOwnerApi(owner, channelDrive2, AccessControlList.Anonymous);
        await UploadStandardRandomFileHeadersUsingOwnerApi(owner, channelDrive3, AccessControlList.Anonymous);

        return new List<TargetDrive>()
        {
            channelDrive1,
            channelDrive2,
            channelDrive3
        };
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

        using var anonClient = Host.CreateAnonymousClient(PrimaryIdentity);
        var svc = RestService.For<IRefitHomeDriveQuery>(anonClient);

        var queryBatchResponse = await svc.QueryBatchCollection(new QueryBatchCollectionRequest()
        {
            Queries = sections
        });

        Assert.That(queryBatchResponse.Headers.Contains("Cache-Control"), Is.True);
        Assert.That(queryBatchResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var queryResult = queryBatchResponse.Content;
        Assert.That(queryResult, Is.Not.Null);

        Assert.That(queryResult!.Results.Count, Is.EqualTo(3), "Should be 3 sections");

        for (var i = 0; i < sections.Count; i++)
        {
            var sectionName = sections[i].Name;
            Assert.That(queryResult.Results, Has.Exactly(1).Matches<QueryBatchResponse>(r =>
                    r.Name == sectionName &&
                    r.SearchResults.SingleOrDefault(r2 => r2.FileMetadata.AppData.FileType == HomeCachingService.PostFileType) != null),
                $"section {i} is missing its post");

            Assert.That(queryResult.Results, Has.Exactly(1).Matches<QueryBatchResponse>(r =>
                    r.Name == sectionName &&
                    r.InvalidDrive == false),
                $"section {i} reported an invalid drive");
        }
    }
}
#endif
