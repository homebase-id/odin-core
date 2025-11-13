using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Hosting.Tests._Universal;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient.TestCases;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests._V2;

public class GetPayloadTests
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

    public static IEnumerable TestCases()
    {
        yield return new object[] { new GuestTestCase(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
        yield return new object[] { new AppTestCase(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
        yield return new object[] { new OwnerTestCase(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanGetHeaderAndPayloadAndThumbnailsOnV2(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var metadata = SampleMetadataData.Create(fileType: 100);
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();

        var file = await UploadFile(identity, metadata, payload, callerContext);

        // get the file header using v2 api
        // var client = new ApiClientV2(ownerApi: null, identity);

        await callerContext.Initialize(ownerApiClient);
        var client = new DriveV2Client(identity.OdinId, callerContext.GetFactory());
        
        //
        // can get header
        //
        var getHeaderResponse = await client.GetFileHeader(file);
        ClassicAssert.IsTrue(getHeaderResponse.IsSuccessStatusCode,$"code was {getHeaderResponse.StatusCode}");
        var header = getHeaderResponse.Content;
        ClassicAssert.IsNotNull(header);
        ClassicAssert.IsTrue(header.FileMetadata.Payloads.Count() == 1);

        //
        // can get payload
        //
        var payloadFromHeader = header.FileMetadata.GetPayloadDescriptor(payload.Key);
        ClassicAssert.IsNotNull(payloadFromHeader, "payload not found in header");
        ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(payloadFromHeader.Iv, payload.Iv));


        // Get the payload and check the headers
        var getPayloadKey1Response = await client.GetPayload(file, payload.Key);

        ClassicAssert.IsTrue(getPayloadKey1Response.StatusCode == expectedStatusCode);

        // can get thumbnail

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            ClassicAssert.IsNotNull(getPayloadKey1Response.ContentHeaders);
            ClassicAssert.IsNotNull(getPayloadKey1Response.Headers);

            ClassicAssert.IsTrue(
                getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadEncrypted, out var isEncryptedValues));
            ClassicAssert.IsFalse(bool.Parse(isEncryptedValues.Single()));

            ClassicAssert.IsTrue(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadKey, out var payloadKeyValues));
            ClassicAssert.IsTrue(payloadKeyValues.Single() == payload.Key);
            ClassicAssert.IsTrue(
                getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.DecryptedContentType, out var contentTypeValues));
            ClassicAssert.IsTrue(contentTypeValues.Single() == payload.ContentType);

            ClassicAssert.IsTrue(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedHeader64,
                out var encryptedHeader64Values));

            var payloadEkh = EncryptedKeyHeader.FromBase64(encryptedHeader64Values.Single());
            ClassicAssert.IsNotNull(payloadEkh);
            ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(payloadEkh.Iv, payload.Iv));
            ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(payloadEkh.EncryptedAesKey,
                header.SharedSecretEncryptedKeyHeader.EncryptedAesKey));

            ClassicAssert.IsTrue(DriveFileUtility.TryParseLastModifiedHeader(getPayloadKey1Response.ContentHeaders,
                out var lastModifiedHeaderValue));
            //Note commented as I'm having some conversion issues i think
            ClassicAssert.IsTrue(lastModifiedHeaderValue.GetValueOrDefault().seconds == payloadFromHeader.LastModified.seconds);
        }
    }


    private async Task<ExternalFileIdentifier> UploadFile(TestIdentity identity, UploadFileMetadata uploadedFileMetadata,
        TestPayloadDefinition payloadDefinition,
        IApiClientContext callerContext)
    {
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var targetDrive = callerContext.TargetDrive;
        // create drive
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

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