using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Storage;
using Odin.Hosting.Tests._Universal;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient.TestCases;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests._V2.Drive;

public class QueryBatchTests_Secured
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Pippin, TestIdentities.Samwise });
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

    public static IEnumerable TestCasesSecuredDrive()
    {
        yield return new object[] { new GuestTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Read), HttpStatusCode.OK };
        yield return new object[] { new AppTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Read), HttpStatusCode.OK };

        yield return new object[] { new GuestTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Write), HttpStatusCode.Forbidden };
        yield return new object[] { new AppTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Write), HttpStatusCode.Forbidden };

        yield return new object[] { new OwnerTestCase(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    [Test]
    [TestCaseSource(nameof(TestCasesSecuredDrive))]
    public async Task CanQueryBatch(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        const int fileType = 100;
        var metadata = SampleMetadataData.Create(fileType: fileType);
        metadata.AccessControlList = AccessControlList.Anonymous;
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();

        var file1 = await UploadFile(identity, metadata, payload, allowAnonymousReadsOnDrive: false, callerContext);
        var file2 = await UploadFile(identity, metadata, payload, allowAnonymousReadsOnDrive: false, callerContext);

        await callerContext.Initialize(ownerApiClient);
        var client = new DriveV2Client(identity.OdinId, callerContext.GetFactory());

        var driveId = callerContext.TargetDrive.Alias;
        var getBatchResponse = await client.GetBatchAsync(driveId, new QueryBatchRequest
        {
            QueryParams = new FileQueryParams
            {
                TargetDrive = callerContext.TargetDrive,
                FileType = [fileType],
                FileState = null,
                DataType = null,
                ArchivalStatus = null,
                Sender = null,
                GroupId = null,
                UserDate = null,
                ClientUniqueIdAtLeastOne = null,
                TagsMatchAtLeastOne = null,
                TagsMatchAll = null,
                LocalTagsMatchAtLeastOne = null,
                LocalTagsMatchAll = null,
                GlobalTransitId = null
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        ClassicAssert.IsTrue(getBatchResponse.StatusCode == expectedStatusCode, $"actual code was {getBatchResponse.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            var batch = getBatchResponse.Content;

            var s1 = batch.SearchResults.SingleOrDefault(x => x.FileId == file1.FileId);
            ClassicAssert.IsNotNull(s1);
            ClassicAssert.IsTrue(s1!.FileMetadata.AppData.FileType == fileType);

            var s2 = batch.SearchResults.SingleOrDefault(x => x.FileId == file2.FileId);
            ClassicAssert.IsNotNull(s2);
            ClassicAssert.IsTrue(s2!.FileMetadata.AppData.FileType == fileType);
        }
    }

    [Test]
    [TestCaseSource(nameof(TestCasesSecuredDrive))]
    public async Task CanQuerySmartBatch(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        const int fileType = 100;
        var metadata = SampleMetadataData.Create(fileType: fileType);
        metadata.AccessControlList = AccessControlList.Anonymous;
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();

        var file1 = await UploadFile(identity, metadata, payload, allowAnonymousReadsOnDrive: false, callerContext);
        var file2 = await UploadFile(identity, metadata, payload, allowAnonymousReadsOnDrive: false, callerContext);

        await callerContext.Initialize(ownerApiClient);
        var client = new DriveV2Client(identity.OdinId, callerContext.GetFactory());

        var driveId = callerContext.TargetDrive.Alias;
        var getBatchResponse = await client.GetSmartBatchAsync(driveId, new QueryBatchRequest
        {
            QueryParams = new FileQueryParams
            {
                TargetDrive = callerContext.TargetDrive,
                FileType = [fileType],
                FileState = null,
                DataType = null,
                ArchivalStatus = null,
                Sender = null,
                GroupId = null,
                UserDate = null,
                ClientUniqueIdAtLeastOne = null,
                TagsMatchAtLeastOne = null,
                TagsMatchAll = null,
                LocalTagsMatchAtLeastOne = null,
                LocalTagsMatchAll = null,
                GlobalTransitId = null
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        ClassicAssert.IsTrue(getBatchResponse.StatusCode == expectedStatusCode);
        var batch = getBatchResponse.Content;

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            var s1 = batch.SearchResults.SingleOrDefault(x => x.FileId == file1.FileId);
            ClassicAssert.IsNotNull(s1);
            ClassicAssert.IsTrue(s1!.FileMetadata.AppData.FileType == fileType);

            var s2 = batch.SearchResults.SingleOrDefault(x => x.FileId == file2.FileId);
            ClassicAssert.IsNotNull(s2);
            ClassicAssert.IsTrue(s2!.FileMetadata.AppData.FileType == fileType);
        }
    }

    [Test]
    [TestCaseSource(nameof(TestCasesSecuredDrive))]
    public async Task CanQueryBatchCollection(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        const int fileType1 = 100;
        var metadata = SampleMetadataData.Create(fileType: fileType1);
        metadata.AccessControlList = AccessControlList.Anonymous;
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();

        var file1 = await UploadFile(identity, metadata, payload, allowAnonymousReadsOnDrive: false, callerContext);

        const int fileType2 = 202;
        var metadata2 = SampleMetadataData.Create(fileType: fileType2);
        metadata2.AccessControlList = AccessControlList.Anonymous;
        var payload2 = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();

        var file2 = await UploadFile(identity, metadata2, payload2, allowAnonymousReadsOnDrive: false, callerContext);

        await callerContext.Initialize(ownerApiClient);
        var client = new DriveV2Client(identity.OdinId, callerContext.GetFactory());

        var driveId = callerContext.TargetDrive.Alias;

        var q1 = new CollectionQueryParamSection
        {
            Name = "q1",
            QueryParams = new FileQueryParams
            {
                TargetDrive = callerContext.TargetDrive,
                FileType = [fileType1],
                FileState = null,
                DataType = null,
                ArchivalStatus = null,
                Sender = null,
                GroupId = null,
                UserDate = null,
                ClientUniqueIdAtLeastOne = null,
                TagsMatchAtLeastOne = null,
                TagsMatchAll = null,
                LocalTagsMatchAtLeastOne = null,
                LocalTagsMatchAll = null,
                GlobalTransitId = null
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        };

        var q2 = new CollectionQueryParamSection
        {
            Name = "q2",
            QueryParams = new FileQueryParams
            {
                TargetDrive = callerContext.TargetDrive,
                FileType = [fileType2],
                FileState = null,
                DataType = null,
                ArchivalStatus = null,
                Sender = null,
                GroupId = null,
                UserDate = null,
                ClientUniqueIdAtLeastOne = null,
                TagsMatchAtLeastOne = null,
                TagsMatchAll = null,
                LocalTagsMatchAtLeastOne = null,
                LocalTagsMatchAll = null,
                GlobalTransitId = null
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        };

        var getBatchResponse = await client.GetBatchCollectionAsync(driveId, new QueryBatchCollectionRequest
        {
            Queries = [q1, q2],
            FileSystemType = FileSystemType.Standard
        });

        //
        // Query batch collection always returns 200 but gives you access details per drive
        //
        ClassicAssert.IsTrue(getBatchResponse.IsSuccessStatusCode);

        if (expectedStatusCode == HttpStatusCode.Forbidden)
        {
            // test forbbin per drive
            var batches = getBatchResponse.Content.Results;
            ClassicAssert.IsNotEmpty(batches);
            var batch1 = batches.SingleOrDefault(x => x.Name == "q1");
            ClassicAssert.IsNotNull(batch1);
            ClassicAssert.IsTrue(batch1!.InvalidDrive);
            
            var batch2 = batches.SingleOrDefault(x => x.Name == "q2");
            ClassicAssert.IsNotNull(batch2);
            ClassicAssert.IsTrue(batch2!.InvalidDrive);
        }
        
        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            var batches = getBatchResponse.Content.Results;
            ClassicAssert.IsNotEmpty(batches);
            var batch1 = batches.SingleOrDefault(x => x.Name == "q1");
            var s1 = batch1.SearchResults.SingleOrDefault(x => x.FileId == file1.FileId);
            ClassicAssert.IsNotNull(s1);
            ClassicAssert.IsTrue(s1!.FileMetadata.AppData.FileType == fileType1);

            var batch2 = batches.SingleOrDefault(x => x.Name == "q2");
            var s2 = batch2.SearchResults.SingleOrDefault(x => x.FileId == file2.FileId);
            ClassicAssert.IsNotNull(s2);
            ClassicAssert.IsTrue(s2!.FileMetadata.AppData.FileType == fileType2);
        }
    }

    private async Task<ExternalFileIdentifier> UploadFile(TestIdentity identity, UploadFileMetadata uploadedFileMetadata,
        TestPayloadDefinition payloadDefinition,
        bool allowAnonymousReadsOnDrive,
        IApiClientContext callerContext)
    {
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var targetDrive = callerContext.TargetDrive;
        var allDrivesResponse = await ownerApiClient.DriveManager.GetDrives(1, 100);
        var allDrives = allDrivesResponse.Content;
        var existingDrive = allDrives.Results.SingleOrDefault(d => d.TargetDrive == callerContext.TargetDrive);
        if (existingDrive == null)
        {
            await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReadsOnDrive);
        }

        // upload file
        var testPayloads = new List<TestPayloadDefinition> { payloadDefinition };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var response = await ownerApiClient.DriveRedux.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, testPayloads);

        // send back details - fileid
        ClassicAssert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        ClassicAssert.IsNotNull(uploadResult);
        return uploadResult.File;
    }
}