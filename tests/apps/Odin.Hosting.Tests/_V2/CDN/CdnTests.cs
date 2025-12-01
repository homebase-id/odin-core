using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Hosting.Tests._Universal;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient.TestCases;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests._V2.CDN;

public class CdnTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        Environment.SetEnvironmentVariable("Cdn__Enabled", "true");
        Environment.SetEnvironmentVariable("Cdn__PayloadBaseUrl", "https://somecdn.com/");
        Environment.SetEnvironmentVariable("Cdn__RequiredAuthToken", CdnTestCase.GetAuthToken64());

        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities:
        [
            TestIdentities.Pippin, TestIdentities.Samwise
        ]);
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

    public static IEnumerable TestCasesCdn()
    {
        yield return new object[] { new CdnTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Read), HttpStatusCode.OK };
    }

    [Test]
    [TestCaseSource(nameof(TestCasesCdn))]
    public async Task FailToGetHeaderAsCdn(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await callerContext.Initialize(sam);

        // upload some files as sam owner
        var identity = TestIdentities.Samwise;

        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AccessControlList = AccessControlList.Anonymous;
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var file = await UploadFile(identity, metadata, payload, allowAnonymousReadsOnDrive: true, callerContext);

        var client = new DriveV2Client(sam.OdinId, callerContext.GetFactory());

        // fail to get header since it's not an allowed path
        var getHeaderResponse = await client.GetFileHeaderAsync(file);
        ClassicAssert.IsTrue(getHeaderResponse.StatusCode == HttpStatusCode.Unauthorized, $"code was {getHeaderResponse.StatusCode}");
    }

    [Test]
    [TestCaseSource(nameof(TestCasesCdn))]
    public async Task CanGetPayloadAndThumbnailsOnAnonymousDriveV2(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AccessControlList = AccessControlList.Anonymous;
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();

        var file = await UploadFile(identity, metadata, payload, allowAnonymousReadsOnDrive: true, callerContext);

        await callerContext.Initialize(ownerApiClient);
        var client = new DriveV2Client(identity.OdinId, callerContext.GetFactory());

        //
        // can get payload
        //

        // Get the payload and check the headers
        var getPayloadKey1Response = await client.GetPayloadAsync(file, payload.Key);

        ClassicAssert.IsTrue(getPayloadKey1Response.StatusCode == expectedStatusCode, $"Code should have been {expectedStatusCode} but" +
                                                                                      $" was {getPayloadKey1Response.StatusCode}");

        // can get thumbnail

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            ClassicAssert.IsNotNull(getPayloadKey1Response.ContentHeaders);
            ClassicAssert.IsNotNull(getPayloadKey1Response.Headers);

            // payload is not encrypted
            ClassicAssert.IsTrue(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadEncrypted,
                out var isEncryptedValues));
            ClassicAssert.IsFalse(bool.Parse(isEncryptedValues.Single()));

            // payload key is in http header
            ClassicAssert.IsTrue(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadKey, out var payloadKeyValues));
            ClassicAssert.IsTrue(payloadKeyValues.Single() == payload.Key);

            // content type is given
            ClassicAssert.IsTrue(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.DecryptedContentType,
                out var contentTypeValues));
            ClassicAssert.IsTrue(contentTypeValues.Single() == payload.ContentType);

            // Assert: header must not exist, or if it does, must be empty/null
            getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedHeader64, out var values);
            ClassicAssert.IsTrue(
                values == null || values.All(string.IsNullOrEmpty),
                "SharedSecretEncryptedHeader64 should not be present, or should be null/empty when using CDN token type"
            );

            ClassicAssert.IsTrue(DriveFileUtility.TryParseLastModifiedHeader(getPayloadKey1Response.ContentHeaders,
                out var lastModifiedHeaderValue));

            //
            // Get the payload from header using owner client
            //
            var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(file);
            ClassicAssert.IsTrue(getHeaderResponse.StatusCode == expectedStatusCode, $"code was {getHeaderResponse.StatusCode}");

            var header = getHeaderResponse.Content;
            ClassicAssert.IsNotNull(header);
            ClassicAssert.IsTrue(header.FileMetadata.Payloads.Count() == 1);

            var payloadFromHeader = header.FileMetadata.GetPayloadDescriptor(payload.Key);
            ClassicAssert.IsNotNull(payloadFromHeader, "payload not found in header");
            ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(payloadFromHeader.Iv, payload.Iv));

            ClassicAssert.IsTrue(lastModifiedHeaderValue.GetValueOrDefault().seconds == payloadFromHeader.LastModified.seconds);
        }
    }

    [Test]
    [TestCaseSource(nameof(TestCasesCdn))]
    public async Task CanGetPayloadAndThumbnailsOnSecuredDriveV2(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AccessControlList = AccessControlList.Authenticated;
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();

        var file = await UploadFile(identity, metadata, payload, allowAnonymousReadsOnDrive: false, callerContext);

        await callerContext.Initialize(ownerApiClient);
        var client = new DriveV2Client(identity.OdinId, callerContext.GetFactory());

        // can get thumbnail

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            // Get the payload and check the headers
            var getPayloadKey1Response = await client.GetPayloadAsync(file, payload.Key);

            ClassicAssert.IsTrue(getPayloadKey1Response.StatusCode == expectedStatusCode,
                $"Code should have been {expectedStatusCode} but" +
                $" was {getPayloadKey1Response.StatusCode}");

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

            // Assert: header must not exist, or if it does, must be empty/null
            getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedHeader64, out var values);
            ClassicAssert.IsTrue(
                values == null || values.All(string.IsNullOrEmpty),
                "SharedSecretEncryptedHeader64 should not be present, or should be null/empty when using CDN token type"
            );

            //
            // Get the payload from header using owner client
            //
            var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(file);
            ClassicAssert.IsTrue(getHeaderResponse.StatusCode == expectedStatusCode, $"code was {getHeaderResponse.StatusCode}");

            var header = getHeaderResponse.Content;
            ClassicAssert.IsNotNull(header);
            ClassicAssert.IsTrue(header.FileMetadata.Payloads.Count() == 1);

            var payloadFromHeader = header.FileMetadata.GetPayloadDescriptor(payload.Key);
            ClassicAssert.IsNotNull(payloadFromHeader, "payload not found in header");
            ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(payloadFromHeader.Iv, payload.Iv));

            ClassicAssert.IsTrue(DriveFileUtility.TryParseLastModifiedHeader(getPayloadKey1Response.ContentHeaders,
                out var lastModifiedHeaderValue));

            //Note commented as I'm having some conversion issues i think
            ClassicAssert.IsTrue(lastModifiedHeaderValue.GetValueOrDefault().seconds == payloadFromHeader.LastModified.seconds);
        }
    }

    [Test]
    [TestCaseSource(nameof(TestCasesCdn))]
    public async Task CanGetEncryptedPayloadAndThumbnailsOnSecuredDriveV2(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: false);

        // upload metadata
        const string originalContent = "original content is here";
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        uploadedFileMetadata.AppData.Content = originalContent;

        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        payload.Iv = ByteArrayUtil.GetRndByteArray(16);
        List<TestPayloadDefinition> testPayloads = [payload];

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        await callerContext.Initialize(ownerApiClient);

        var originalKeyHeader = KeyHeader.NewRandom16();

        //
        // upload encrypted file with encrypted payloads
        //
        var (response, encryptedJsonContent64, uploadedThumbnails, uploadedPayloads) =
            await ownerApiClient.DriveRedux.UploadNewEncryptedFile(targetDrive, originalKeyHeader, uploadedFileMetadata, uploadManifest,
                testPayloads);
        ClassicAssert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        ClassicAssert.IsNotNull(uploadResult);
        var file = uploadResult.File;

        await callerContext.Initialize(ownerApiClient);
        var client = new DriveV2Client(identity.OdinId, callerContext.GetFactory());

        var getPayloadKey1Response = await client.GetPayloadAsync(file, payload.Key);

        ClassicAssert.IsTrue(getPayloadKey1Response.StatusCode == expectedStatusCode, $"Code should have been {expectedStatusCode} but" +
                                                                                      $" was {getPayloadKey1Response.StatusCode}");

        // can get thumbnail

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            // test the payload content
            var expectedPayload = uploadedPayloads.Single(p => p.Key == payload.Key);
            var actualPayloadBytes = await getPayloadKey1Response.Content.ReadAsByteArrayAsync();
            ClassicAssert.IsTrue(expectedPayload.EncryptedContent64 == actualPayloadBytes.ToBase64(), "payload content is not as expected");

            ClassicAssert.IsNotNull(getPayloadKey1Response.ContentHeaders);
            ClassicAssert.IsNotNull(getPayloadKey1Response.Headers);

            ClassicAssert.IsTrue(
                getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadEncrypted, out var isEncryptedValues));
            ClassicAssert.IsTrue(bool.Parse(isEncryptedValues.Single()));

            ClassicAssert.IsTrue(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadKey, out var payloadKeyValues));
            ClassicAssert.IsTrue(payloadKeyValues.Single() == payload.Key);
            ClassicAssert.IsTrue(
                getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.DecryptedContentType, out var contentTypeValues));
            ClassicAssert.IsTrue(contentTypeValues.Single() == payload.ContentType);

            // Assert: header must not exist, or if it does, must be empty/null
            getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedHeader64, out var values);
            ClassicAssert.IsTrue(
                values == null || values.All(string.IsNullOrEmpty),
                "SharedSecretEncryptedHeader64 should not be present, or should be null/empty when using CDN token type"
            );

            //
            // Get the payload from header using owner client
            //
            var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(file);
            ClassicAssert.IsTrue(getHeaderResponse.StatusCode == expectedStatusCode, $"code was {getHeaderResponse.StatusCode}");

            var header = getHeaderResponse.Content;
            ClassicAssert.IsNotNull(header);
            ClassicAssert.IsTrue(header.FileMetadata.Payloads.Count() == 1);

            var payloadFromHeader = header.FileMetadata.GetPayloadDescriptor(payload.Key);
            ClassicAssert.IsNotNull(payloadFromHeader, "payload not found in header");
            ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(payloadFromHeader.Iv, payload.Iv));

            ClassicAssert.IsTrue(DriveFileUtility.TryParseLastModifiedHeader(getPayloadKey1Response.ContentHeaders,
                out var lastModifiedHeaderValue));

            //Note commented as I'm having some conversion issues i think
            ClassicAssert.IsTrue(lastModifiedHeaderValue.GetValueOrDefault().seconds == payloadFromHeader.LastModified.seconds);


            //
            // Test the Thumbnail content
            //

            var thumbnail = payload.Thumbnails.Single();
            var getThumbnailResponse = await client.GetThumbnailAsync(file, thumbnail.PixelWidth, thumbnail.PixelHeight, payload.Key,
                directMatchOnly: true);
            
            ClassicAssert.IsTrue(getThumbnailResponse.StatusCode == HttpStatusCode.OK,
                $"get thumbnail failed - code was {getThumbnailResponse.StatusCode}");
           
            var expectedThumbnail = uploadedThumbnails.Single(p => p.Key == payload.Key);
            var actualThumbnailBytes = await getPayloadKey1Response.Content.ReadAsByteArrayAsync();
            ClassicAssert.IsTrue(expectedThumbnail.EncryptedContent64 == actualThumbnailBytes.ToBase64(), "thumbnail content is not as expected");

            
            ClassicAssert.IsNotNull(getThumbnailResponse.ContentHeaders);
            ClassicAssert.IsNotNull(getThumbnailResponse.Headers);

            ClassicAssert.IsTrue(
                getThumbnailResponse.Headers.TryGetValues(HttpHeaderConstants.PayloadEncrypted, out var thumbnailIsEncryptedValues));
            ClassicAssert.IsTrue(bool.Parse(thumbnailIsEncryptedValues.Single()));

            ClassicAssert.IsTrue(getThumbnailResponse.Headers.TryGetValues(HttpHeaderConstants.PayloadKey, out var thumbnailPayloadKeyValues));
            ClassicAssert.IsTrue(thumbnailPayloadKeyValues.Single() == payload.Key);
            ClassicAssert.IsTrue(getThumbnailResponse.Headers.TryGetValues(HttpHeaderConstants.DecryptedContentType, out var thumbnailContentTypeValues));
            ClassicAssert.IsTrue(thumbnailContentTypeValues.Single() == thumbnail.ContentType);

            // Assert: header must not exist, or if it does, must be empty/null
            getThumbnailResponse.Headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedHeader64, out var thumbnailSharedSecretValues);
            ClassicAssert.IsTrue(
                thumbnailSharedSecretValues == null || thumbnailSharedSecretValues.All(string.IsNullOrEmpty),
                "SharedSecretEncryptedHeader64 should not be present, or should be null/empty when using CDN token type"
            );

            ClassicAssert.IsTrue(DriveFileUtility.TryParseLastModifiedHeader(getThumbnailResponse.ContentHeaders,
                out var thumbnailLastModifiedHeaderValue));

            //Note commented as I'm having some conversion issues i think
            ClassicAssert.IsTrue(thumbnailLastModifiedHeaderValue.GetValueOrDefault().seconds == payloadFromHeader.LastModified.seconds);
            
        }
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