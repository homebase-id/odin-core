#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Home.Service;
using Refit;

namespace Odin.Hosting.Tests.BuiltIn.Home
{
    [TestFixture]
    public class HomeQueryBatchCollectionCachingTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var folder = GetType().Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [SetUp]
        public void Setup()
        {
            _scaffold.ClearAssertLogEventsAction();
            _scaffold.ClearLogEvents();
        }

        [TearDown]
        public void TearDown()
        {
            _scaffold.AssertLogEvents();
        }


        [Test]
        public async Task CanQueryHomeDataEndPoint()
        {
            var identity = TestIdentities.Pippin;

            var ownerClient = _scaffold.CreateOwnerApiClient(identity);

            //
            // Create 3 drives and grant ReadWrite
            //
            var channelDrive1 = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType), "Channel Drive 1", "", true);
            var channelDrive2 = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType), "Channel Drive 2", "", true);
            var channelDrive3 = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Another Drive 3", "", true);

            //
            // Upload 3 files
            //
            var header1 = await UploadStandardRandomFileHeadersUsingOwnerApi(identity, channelDrive1.TargetDriveInfo, AccessControlList.Anonymous);
            var header2 = await UploadStandardRandomFileHeadersUsingOwnerApi(identity, channelDrive2.TargetDriveInfo, AccessControlList.Anonymous);
            var header3 = await UploadStandardRandomFileHeadersUsingOwnerApi(identity, channelDrive3.TargetDriveInfo, AccessControlList.Anonymous);

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
                    QueryParams = new FileQueryParams()
                    {
                        TargetDrive = channelDrive1.TargetDriveInfo,
                        ClientUniqueIdAtLeastOne = new List<Guid>() { header1.uploadedMetadata.AppData.UniqueId.GetValueOrDefault() }
                    }
                },
                new()
                {
                    Name = section2Name,
                    QueryParams = new FileQueryParams()
                    {
                        TargetDrive = channelDrive2.TargetDriveInfo,
                        ClientUniqueIdAtLeastOne = new List<Guid>() { header2.uploadedMetadata.AppData.UniqueId.GetValueOrDefault() }
                    }
                },
                new()
                {
                    Name = section3Name,
                    QueryParams = new FileQueryParams()
                    {
                        TargetDrive = channelDrive3.TargetDriveInfo,
                        ClientUniqueIdAtLeastOne = new List<Guid>() { header3.uploadedMetadata.AppData.UniqueId.GetValueOrDefault() }
                    }
                }
            };

            var anonClient = _scaffold.CreateAnonymousApiHttpClient(identity.OdinId);
            var svc = RestService.For<IRefitHomeDriveQuery>(anonClient);

            var queryBatchResponse = await svc.QueryBatchCollection(new QueryBatchCollectionRequest()
            {
                Queries = sections
            });

            Assert.IsTrue(queryBatchResponse.Headers.Contains("Cache-Control"));

            Assert.IsTrue(queryBatchResponse.IsSuccessStatusCode);
            var queryResult = queryBatchResponse.Content;
            Assert.IsNotNull(queryResult);

            Assert.IsTrue(queryResult.Results.Count == 3, "Should be 3 sections");

            Assert.IsNotNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == section1Name &&
                r.SearchResults.SingleOrDefault(r2 => r2.FileId == header1.uploadResult.File.FileId) != null));

            Assert.IsNotNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == section1Name &&
                r.InvalidDrive == false));

            Assert.IsNotNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == section2Name &&
                r.SearchResults.SingleOrDefault(r2 => r2.FileId == header2.uploadResult.File.FileId) != null));

            Assert.IsNotNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == section2Name &&
                r.InvalidDrive == false));

            Assert.IsNotNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == section3Name &&
                r.SearchResults.SingleOrDefault(r2 => r2.FileId == header3.uploadResult.File.FileId) != null));


            Assert.IsNotNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == section3Name &&
                r.InvalidDrive == false));
        }

        [Test]
        public async Task CanInvalidateCache()
        {
            var identity = TestIdentities.Pippin;
            var anonClient = _scaffold.CreateAnonymousApiHttpClient(identity.OdinId);
            var svc = RestService.For<IRefitHomeDriveQuery>(anonClient);

            var drives = await UploadData(identity);

            HomeCachingService.ResetCacheStats();
            Assert.IsTrue(HomeCachingService.CacheMiss == 0);

            await QueryData(identity, drives.ToArray());
            Assert.IsTrue(HomeCachingService.CacheMiss == 1, "Cache should have not been used");

            await QueryData(identity, drives.ToArray());
            Assert.IsTrue(HomeCachingService.CacheMiss == 1, "cache misses should not have changed.");

            //
            // Invalidate and query again
            //
            var invalidateCacheResponse = await svc.InvalidateCache();
            Assert.IsTrue(invalidateCacheResponse.IsSuccessStatusCode);

            await QueryData(identity, drives.ToArray());

            Assert.IsTrue(HomeCachingService.CacheMiss == 2, "cache miss should have increased");

            HomeCachingService.ResetCacheStats();
        }


        [Test]
        public async Task CanInvalidateCache_ByAddingFileToChannel()
        {
            var identity = TestIdentities.Pippin;

            var drives = await UploadData(identity);

            HomeCachingService.ResetCacheStats();
            Assert.IsTrue(HomeCachingService.CacheMiss == 0);

            await QueryData(identity, drives.ToArray());
            Assert.IsTrue(HomeCachingService.CacheMiss == 1, "Cache should have not been used");

            await QueryData(identity, drives.ToArray());
            Assert.IsTrue(HomeCachingService.CacheMiss == 1, "cache misses should not have changed.");

            //
            // Add a new channel
            //
            await UploadStandardRandomFileHeadersUsingOwnerApi(identity, drives[0], AccessControlList.Anonymous, HomeCachingService.ChannelFileType);

            await QueryData(identity, drives.ToArray());
            Assert.IsTrue(HomeCachingService.CacheMiss == 2, "cache miss should have increased");

            HomeCachingService.ResetCacheStats();
        }

        private async Task<(UploadResult uploadResult, UploadFileMetadata uploadedMetadata)> UploadStandardRandomFileHeadersUsingOwnerApi(TestIdentity identity,
            TargetDrive targetDrive, AccessControlList acl = null, int fileType = HomeCachingService.PostFileType)
        {
            var client = _scaffold.CreateOwnerApiClient(identity);
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

            var result = await client.Drive.UploadFile(FileSystemType.Standard, targetDrive, fileMetadata);
            return (result, fileMetadata);
        }

        private async Task<List<TargetDrive>> UploadData(TestIdentity identity)
        {
            var ownerClient = _scaffold.CreateOwnerApiClient(identity);

            //
            // Create 3 drives and grant ReadWrite
            //
            var channelDrive1 = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType), "Channel Drive 1", "", true);
            var channelDrive2 = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType), "Channel Drive 2", "", true);
            var channelDrive3 = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Another Drive 3", "", true);

            //
            // Upload 3 files
            //
            var header1 = await UploadStandardRandomFileHeadersUsingOwnerApi(identity, channelDrive1.TargetDriveInfo, AccessControlList.Anonymous);
            var header2 = await UploadStandardRandomFileHeadersUsingOwnerApi(identity, channelDrive2.TargetDriveInfo, AccessControlList.Anonymous);
            var header3 = await UploadStandardRandomFileHeadersUsingOwnerApi(identity, channelDrive3.TargetDriveInfo, AccessControlList.Anonymous);

            return new List<TargetDrive>()
            {
                channelDrive1.TargetDriveInfo,
                channelDrive2.TargetDriveInfo,
                channelDrive3.TargetDriveInfo
            };
        }

        private async Task QueryData(TestIdentity identity, params TargetDrive[] drives)
        {
            var sections = drives.Select(d => new CollectionQueryParamSection()
            {
                Name = d.ToKey().ToBase64(),
                QueryParams = new FileQueryParams()
                {
                    TargetDrive = d,
                    FileType = new[] { HomeCachingService.PostFileType }
                }
            }).ToList();

            var anonClient = _scaffold.CreateAnonymousApiHttpClient(identity.OdinId);
            var svc = RestService.For<IRefitHomeDriveQuery>(anonClient);

            var queryBatchResponse = await svc.QueryBatchCollection(new QueryBatchCollectionRequest()
            {
                Queries = sections
            });

            Assert.IsTrue(queryBatchResponse.Headers.Contains("Cache-Control"));

            Assert.IsTrue(queryBatchResponse.IsSuccessStatusCode);
            var queryResult = queryBatchResponse.Content;
            Assert.IsNotNull(queryResult);

            Assert.IsTrue(queryResult.Results.Count == 3, "Should be 3 sections");

            Assert.IsNotNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == sections[0].Name &&
                r.SearchResults.SingleOrDefault(r2 => r2.FileMetadata.AppData.FileType == HomeCachingService.PostFileType) != null));

            Assert.IsNotNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == sections[0].Name &&
                r.InvalidDrive == false));

            Assert.IsNotNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == sections[1].Name &&
                r.SearchResults.SingleOrDefault(r2 => r2.FileMetadata.AppData.FileType == HomeCachingService.PostFileType) != null));

            Assert.IsNotNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == sections[1].Name &&
                r.InvalidDrive == false));

            Assert.IsNotNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == sections[2].Name &&
                r.SearchResults.SingleOrDefault(r2 => r2.FileMetadata.AppData.FileType == HomeCachingService.PostFileType) != null));

            Assert.IsNotNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == sections[2].Name &&
                r.InvalidDrive == false));
        }
    }
}
#endif