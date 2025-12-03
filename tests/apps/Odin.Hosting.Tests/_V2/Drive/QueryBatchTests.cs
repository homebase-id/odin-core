using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
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

public class QueryBatchTests
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

    public static IEnumerable TestCasesAnonDrive()
    {
        yield return new object[] { new GuestTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Read), HttpStatusCode.OK };
        yield return new object[] { new AppTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Read), HttpStatusCode.OK };

        yield return new object[] { new GuestTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Write), HttpStatusCode.OK };
        yield return new object[] { new AppTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Write), HttpStatusCode.OK };

        yield return new object[] { new OwnerTestCase(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
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
    [TestCaseSource(nameof(TestCasesAnonDrive))]
    public async Task CanQueryBatch(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        const int fileType = 100;
        var metadata = SampleMetadataData.Create(fileType: fileType);
        metadata.AccessControlList = AccessControlList.Anonymous;
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();

        var file = await UploadFile(identity, metadata, payload, allowAnonymousReadsOnDrive: true, callerContext);

        await callerContext.Initialize(ownerApiClient);
        var client = new DriveV2Client(identity.OdinId, callerContext.GetFactory());

        var getBatchResponse = await client.GetBatchAsync(new QueryBatchRequest
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
        
        ClassicAssert.IsTrue(batch.SearchResults.Single().FileId == file.FileId);
        
    }

    private async Task<ExternalFileIdentifier> UploadFile(TestIdentity identity, UploadFileMetadata uploadedFileMetadata,
        TestPayloadDefinition payloadDefinition,
        bool allowAnonymousReadsOnDrive,
        IApiClientContext callerContext)
    {
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var targetDrive = callerContext.TargetDrive;
        // create drive
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReadsOnDrive);

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