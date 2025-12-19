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
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._V2.Tests.Drive.DriveReaderTests;

public class GetFileTests
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

    public static IEnumerable TestCasesSecuredDriveForEncryptedFiles()
    {
        yield return new object[] { new AppTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Read), HttpStatusCode.OK };

        yield return new object[] { new GuestTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Write), HttpStatusCode.Forbidden };
        yield return new object[] { new AppTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Write), HttpStatusCode.Forbidden };

        yield return new object[] { new OwnerTestCase(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    [Test]
    [TestCaseSource(nameof(TestCasesAnonDrive))]
    public async Task CanGetHeaderAndPayloadAndThumbnailsOnAnonymousDriveV2ByFileId(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AccessControlList = AccessControlList.Anonymous;
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var file = await UploadFile(identity, metadata, payload, allowAnonymousReadsOnDrive: true, callerContext);

        // get the file header using v2 api
        // var client = new ApiClientV2(ownerApi: null, identity);

        await callerContext.Initialize(ownerApiClient);
        var client = new DriveReaderV2Client(identity.OdinId, callerContext.GetFactory());

        //
        // can get header
        //
        var getHeaderResponse = await client.GetFileHeaderAsync(file.DriveId, file.FileId);
        ClassicAssert.IsTrue(getHeaderResponse.IsSuccessStatusCode, $"code was {getHeaderResponse.StatusCode}");
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
        var getPayloadKey1Response = await client.GetPayloadAsync(file.DriveId, file.FileId, payload.Key);

        ClassicAssert.IsTrue(getPayloadKey1Response.StatusCode == expectedStatusCode, $"Code should have been {expectedStatusCode} but" +
                                                                                      $" was {getPayloadKey1Response.StatusCode}");

        // can get thumbnail

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            ClassicAssert.IsNotNull(getPayloadKey1Response.ContentHeaders);
            ClassicAssert.IsNotNull(getPayloadKey1Response.Headers);

            ClassicAssert.IsTrue(
                getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadEncrypted, out var isEncryptedValues));
            ClassicAssert.IsFalse(bool.Parse(isEncryptedValues!.Single()));

            ClassicAssert.IsTrue(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadKey, out var payloadKeyValues));
            ClassicAssert.IsTrue(payloadKeyValues!.Single() == payload.Key);
            ClassicAssert.IsTrue(
                getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.DecryptedContentType, out var contentTypeValues));
            ClassicAssert.IsTrue(contentTypeValues!.Single() == payload.ContentType);

            // Assert: header must not exist, or if it does, must be empty/null
            getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedKeyHeader64,
                out var encryptedHeader64Values);
            ClassicAssert.IsTrue(
                encryptedHeader64Values == null || encryptedHeader64Values.All(string.IsNullOrEmpty),
                "SharedSecretEncryptedHeader64 should not be present, or should be null/empty when file not encrypted"
            );


            ClassicAssert.IsTrue(DriveFileUtility.TryParseLastModifiedHeader(getPayloadKey1Response.ContentHeaders,
                out var lastModifiedHeaderValue));
            //Note commented as I'm having some conversion issues i think
            ClassicAssert.IsTrue(lastModifiedHeaderValue.GetValueOrDefault().seconds == payloadFromHeader.LastModified.seconds);


            //
            // Ge the thumbnail
            //

            var thumbnail = payload.Thumbnails.Single();
            var getThumbnailResponse = await client.GetThumbnailAsync(file.DriveId, file.FileId, payload.Key, 
                thumbnail.PixelWidth, thumbnail.PixelHeight, directMatchOnly: true);

            ClassicAssert.IsTrue(getThumbnailResponse.StatusCode == HttpStatusCode.OK,
                $"get thumbnail failed - code was {getThumbnailResponse.StatusCode}");

            var expectedThumbnail = payload.Thumbnails.Single();
            var actualThumbnailBytes = await getThumbnailResponse.Content.ReadAsByteArrayAsync();

            ClassicAssert.IsTrue(expectedThumbnail.Content.ToBase64() == actualThumbnailBytes.ToBase64(),
                "thumbnail content is not as expected");

            ClassicAssert.IsNotNull(getThumbnailResponse.ContentHeaders);
            ClassicAssert.IsNotNull(getThumbnailResponse.Headers);

            ClassicAssert.IsTrue(
                getThumbnailResponse.Headers.TryGetValues(HttpHeaderConstants.PayloadEncrypted, out var thumbnailIsEncryptedValues));
            ClassicAssert.IsFalse(bool.Parse(thumbnailIsEncryptedValues!.Single()));

            ClassicAssert.IsTrue(getThumbnailResponse.Headers.TryGetValues(HttpHeaderConstants.DecryptedContentType,
                out var thumbnailContentTypeValues));
            ClassicAssert.IsTrue(thumbnailContentTypeValues!.Single() == thumbnail.ContentType);

            // Assert: header must not exist, or if it does, must be empty/null
            getThumbnailResponse.Headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedKeyHeader64,
                out var thumbnailSharedSecretValues);
            ClassicAssert.IsTrue(
                thumbnailSharedSecretValues == null || thumbnailSharedSecretValues.All(string.IsNullOrEmpty),
                "SharedSecretEncryptedHeader64 should not be present, or should be null/empty when file not encrypted"
            );

            ClassicAssert.IsTrue(DriveFileUtility.TryParseLastModifiedHeader(getThumbnailResponse.ContentHeaders,
                out var thumbnailLastModifiedHeaderValue));

            
            // get the thumbnail w/o width and height
            var getThumbnailResponse2 = await client.GetThumbnailAsync(file.DriveId, file.FileId, payload.Key);
            ClassicAssert.IsTrue(getThumbnailResponse2.StatusCode == HttpStatusCode.OK,
                $"get thumbnail failed - code was {getThumbnailResponse2.StatusCode}");

            
            //Note commented as I'm having some conversion issues i think
            ClassicAssert.IsTrue(thumbnailLastModifiedHeaderValue.GetValueOrDefault().seconds == payloadFromHeader.LastModified.seconds);
        }
    }

    [Test]
    [TestCaseSource(nameof(TestCasesSecuredDrive))]
    public async Task CanGetHeaderAndPayloadAndThumbnailsOnSecuredDriveV2ByFileId(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AccessControlList = AccessControlList.Authenticated;
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();

        var file = await UploadFile(identity, metadata, payload, allowAnonymousReadsOnDrive: false, callerContext);

        // get the file header using v2 api
        // var client = new ApiClientV2(ownerApi: null, identity);

        await callerContext.Initialize(ownerApiClient);
        var client = new DriveReaderV2Client(identity.OdinId, callerContext.GetFactory());

        //
        // can get header
        //
        var getHeaderResponse = await client.GetFileHeaderAsync(file.DriveId, file.FileId);
        ClassicAssert.IsTrue(getHeaderResponse.StatusCode == expectedStatusCode, $"code was {getHeaderResponse.StatusCode}");


        // can get thumbnail

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
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
            var getPayloadKey1Response = await client.GetPayloadAsync(file.DriveId, file.FileId, payload.Key);

            ClassicAssert.IsTrue(getPayloadKey1Response.StatusCode == expectedStatusCode,
                $"Code should have been {expectedStatusCode} but" +
                $" was {getPayloadKey1Response.StatusCode}");

            ClassicAssert.IsNotNull(getPayloadKey1Response.ContentHeaders);
            ClassicAssert.IsNotNull(getPayloadKey1Response.Headers);

            ClassicAssert.IsTrue(
                getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadEncrypted, out var isEncryptedValues));
            ClassicAssert.IsFalse(bool.Parse(isEncryptedValues!.Single()));

            ClassicAssert.IsTrue(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadKey, out var payloadKeyValues));
            ClassicAssert.IsTrue(payloadKeyValues!.Single() == payload.Key);
            ClassicAssert.IsTrue(
                getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.DecryptedContentType, out var contentTypeValues));
            ClassicAssert.IsTrue(contentTypeValues!.Single() == payload.ContentType);

            // Assert: header must not exist, or if it does, must be empty/null
            getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedKeyHeader64,
                out var encryptedHeader64Values);
            ClassicAssert.IsTrue(
                encryptedHeader64Values == null || encryptedHeader64Values.All(string.IsNullOrEmpty),
                "SharedSecretEncryptedHeader64 should not be present, or should be null/empty when file not encrypted"
            );

            ClassicAssert.IsTrue(DriveFileUtility.TryParseLastModifiedHeader(getPayloadKey1Response.ContentHeaders,
                out var lastModifiedHeaderValue));
            //Note commented as I'm having some conversion issues i think
            ClassicAssert.IsTrue(lastModifiedHeaderValue.GetValueOrDefault().seconds == payloadFromHeader.LastModified.seconds);
        }
    }

    // ///

    [Test]
    [TestCaseSource(nameof(TestCasesAnonDrive))]
    public async Task CanGetHeaderAndPayloadAndThumbnailsOnAnonymousDriveV2ByUniqueId(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var clientUniqueId = Guid.NewGuid();
        var driveId = callerContext.TargetDrive.Alias;

        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AccessControlList = AccessControlList.Anonymous;
        metadata.AppData.UniqueId = clientUniqueId;
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();

        await UploadFile(identity, metadata, payload, allowAnonymousReadsOnDrive: true, callerContext);

        // get the file header using v2 api
        // var client = new ApiClientV2(ownerApi: null, identity);

        await callerContext.Initialize(ownerApiClient);
        var client = new DriveReaderV2Client(identity.OdinId, callerContext.GetFactory());

        //
        // can get header
        //
        var getHeaderResponse = await client.GetFileHeaderByUniqueIdAsync(clientUniqueId, driveId);
        ClassicAssert.IsTrue(getHeaderResponse.IsSuccessStatusCode, $"code was {getHeaderResponse.StatusCode}");
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
        var getPayloadKey1Response = await client.GetPayloadByUniqueIdAsync(clientUniqueId, driveId, payload.Key);

        ClassicAssert.IsTrue(getPayloadKey1Response.StatusCode == expectedStatusCode, $"Code should have been {expectedStatusCode} but" +
                                                                                      $" was {getPayloadKey1Response.StatusCode}");

        // can get thumbnail

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            ClassicAssert.IsNotNull(getPayloadKey1Response.ContentHeaders);
            ClassicAssert.IsNotNull(getPayloadKey1Response.Headers);

            ClassicAssert.IsTrue(
                getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadEncrypted, out var isEncryptedValues));
            ClassicAssert.IsFalse(bool.Parse(isEncryptedValues!.Single()));

            ClassicAssert.IsTrue(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadKey, out var payloadKeyValues));
            ClassicAssert.IsTrue(payloadKeyValues!.Single() == payload.Key);
            ClassicAssert.IsTrue(
                getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.DecryptedContentType, out var contentTypeValues));
            ClassicAssert.IsTrue(contentTypeValues!.Single() == payload.ContentType);

            ClassicAssert.IsFalse(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedKeyHeader64, out _),
                "SharedSecretEncryptedHeader64 should not exist on unencrypted file");

            ClassicAssert.IsTrue(DriveFileUtility.TryParseLastModifiedHeader(getPayloadKey1Response.ContentHeaders,
                out var lastModifiedHeaderValue));
            //Note commented as I'm having some conversion issues i think
            ClassicAssert.IsTrue(lastModifiedHeaderValue.GetValueOrDefault().seconds == payloadFromHeader.LastModified.seconds);
        }
    }

    [Test]
    [TestCaseSource(nameof(TestCasesSecuredDrive))]
    public async Task CanGetHeaderAndPayloadAndThumbnailsOnUnEncryptedFileOnSecuredDriveV2ByUniqueId(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var clientUniqueId = Guid.NewGuid();
        var driveId = callerContext.TargetDrive.Alias;

        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AccessControlList = AccessControlList.Authenticated;
        metadata.AppData.UniqueId = clientUniqueId;
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();

        await UploadFile(identity, metadata, payload, allowAnonymousReadsOnDrive: false, callerContext);

        // get the file header using v2 api
        // var client = new ApiClientV2(ownerApi: null, identity);

        await callerContext.Initialize(ownerApiClient);
        var client = new DriveReaderV2Client(identity.OdinId, callerContext.GetFactory());

        //
        // can get header
        //
        var getHeaderResponse = await client.GetFileHeaderByUniqueIdAsync(clientUniqueId, driveId);
        ClassicAssert.IsTrue(getHeaderResponse.StatusCode == expectedStatusCode, $"code was {getHeaderResponse.StatusCode}");


        // can get thumbnail

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
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
            var getPayloadKey1Response = await client.GetPayloadByUniqueIdAsync(clientUniqueId, driveId, payload.Key);

            ClassicAssert.IsTrue(getPayloadKey1Response.StatusCode == expectedStatusCode,
                $"Code should have been {expectedStatusCode} but" +
                $" was {getPayloadKey1Response.StatusCode}");

            ClassicAssert.IsNotNull(getPayloadKey1Response.ContentHeaders);
            ClassicAssert.IsNotNull(getPayloadKey1Response.Headers);

            ClassicAssert.IsTrue(
                getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadEncrypted, out var isEncryptedValues));
            ClassicAssert.IsFalse(bool.Parse(isEncryptedValues!.Single()));

            ClassicAssert.IsTrue(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadKey, out var payloadKeyValues));
            ClassicAssert.IsTrue(payloadKeyValues!.Single() == payload.Key);
            ClassicAssert.IsTrue(
                getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.DecryptedContentType, out var contentTypeValues));
            ClassicAssert.IsTrue(contentTypeValues!.Single() == payload.ContentType);


            ClassicAssert.IsFalse(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedKeyHeader64, out _),
                "SharedSecretEncryptedHeader64 should not exist on unencrypted file");

            ClassicAssert.IsTrue(DriveFileUtility.TryParseLastModifiedHeader(getPayloadKey1Response.ContentHeaders,
                out var lastModifiedHeaderValue));
            //Note commented as I'm having some conversion issues i think
            ClassicAssert.IsTrue(lastModifiedHeaderValue.GetValueOrDefault().seconds == payloadFromHeader.LastModified.seconds);
        }
    }

    [Test]
    [TestCaseSource(nameof(TestCasesSecuredDriveForEncryptedFiles))]
    public async Task CanGetHeaderAndPayloadAndThumbnailsOn_Encrypted_FileOnSecuredDriveV2ByUniqueId(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var clientUniqueId = Guid.NewGuid();
        var driveId = callerContext.TargetDrive.Alias;

        const string unencryptedContent = "some content here";
        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AppData.Content = unencryptedContent;
        metadata.AccessControlList = AccessControlList.Connected;
        metadata.AppData.UniqueId = clientUniqueId;

        var unencryptedPayload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        unencryptedPayload.Iv = ByteArrayUtil.GetRndByteArray(16);

        var unencryptedThumbnail = unencryptedPayload.Thumbnails.Single();

        var keyHeader = KeyHeader.NewRandom16();
        var (uploadResult, encryptedJsonContent64, _, _) =
            await UploadEncryptedFile(identity, metadata, unencryptedPayload, keyHeader, callerContext);

        await callerContext.Initialize(ownerApiClient);
        var client = new DriveReaderV2Client(identity.OdinId, callerContext.GetFactory());

        //
        // can get header
        //
        var getHeaderResponse = await client.GetFileHeaderByUniqueIdAsync(clientUniqueId, driveId);
        ClassicAssert.IsTrue(getHeaderResponse.StatusCode == expectedStatusCode, $"code was {getHeaderResponse.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            var header = getHeaderResponse.Content;
            ClassicAssert.IsNotNull(header);
            ClassicAssert.IsTrue(header.FileId == uploadResult.FileId);
            ClassicAssert.IsTrue(header.FileMetadata.Payloads.Count() == 1);

            ClassicAssert.IsTrue(header.FileMetadata.AppData.Content == encryptedJsonContent64, "encrypted content does not match");
            var decryptedContentBytes = keyHeader.Decrypt(header.FileMetadata.AppData.Content.FromBase64());
            ClassicAssert.IsTrue(decryptedContentBytes.ToStringFromUtf8Bytes() == unencryptedContent);

            //
            // can get payload
            //
            var payloadFromHeader = header.FileMetadata.GetPayloadDescriptor(unencryptedPayload.Key);
            ClassicAssert.IsNotNull(payloadFromHeader, "payload not found in header");
            ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(payloadFromHeader.Iv, unencryptedPayload.Iv));


            // Get the payload and check the headers
            var getPayloadKey1Response = await client.GetPayloadByUniqueIdAsync(clientUniqueId, driveId, unencryptedPayload.Key);

            ClassicAssert.IsTrue(getPayloadKey1Response.StatusCode == expectedStatusCode,
                $"Code should have been {expectedStatusCode} but" +
                $" was {getPayloadKey1Response.StatusCode}");

            ClassicAssert.IsNotNull(getPayloadKey1Response.ContentHeaders);
            ClassicAssert.IsNotNull(getPayloadKey1Response.Headers);

            ClassicAssert.IsTrue(
                getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadEncrypted, out var isEncryptedValues));
            ClassicAssert.IsTrue(bool.Parse(isEncryptedValues!.Single()));

            ClassicAssert.IsTrue(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadKey, out var payloadKeyValues));
            ClassicAssert.IsTrue(payloadKeyValues!.Single() == unencryptedPayload.Key);
            ClassicAssert.IsTrue(
                getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.DecryptedContentType, out var contentTypeValues));
            ClassicAssert.IsTrue(contentTypeValues!.Single() == unencryptedPayload.ContentType);

            ClassicAssert.IsTrue(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedKeyHeader64,
                out var payloadSharedSecretEncryptedHeader64Values));

            // decrypt the payload the same as an FE client
            var payloadEkh = EncryptedKeyHeader.FromBase64(payloadSharedSecretEncryptedHeader64Values!.Single());

            ClassicAssert.IsNotNull(payloadEkh);
            ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(payloadEkh.Iv, unencryptedPayload.Iv));
            ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(payloadEkh.EncryptedAesKey,
                header.SharedSecretEncryptedKeyHeader.EncryptedAesKey));

            var sharedSecret = client.GetSharedSecret();
            var decryptedPayloadKeyHeader = payloadEkh.DecryptAesToKeyHeader(ref sharedSecret);

            var decryptedPayloadBytes = decryptedPayloadKeyHeader.Decrypt(await getPayloadKey1Response.Content.ReadAsByteArrayAsync());
            ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(decryptedPayloadBytes, unencryptedPayload.Content));

            ClassicAssert.IsTrue(DriveFileUtility.TryParseLastModifiedHeader(getPayloadKey1Response.ContentHeaders,
                out var lastModifiedHeaderValue));
            //Note commented as I'm having some conversion issues I think
            ClassicAssert.IsTrue(lastModifiedHeaderValue.GetValueOrDefault().seconds == payloadFromHeader.LastModified.seconds);

            //
            // now get the thumbnail and decrypt
            //
            var getThumbnailResponse = await client.GetThumbnailUniqueIdAsync(clientUniqueId, driveId, unencryptedThumbnail.PixelWidth,
                unencryptedThumbnail.PixelHeight,
                unencryptedPayload.Key, true);

            ClassicAssert.IsTrue(getThumbnailResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(getThumbnailResponse.ContentHeaders!.LastModified.HasValue);
            ClassicAssert.IsTrue(getThumbnailResponse.ContentHeaders.LastModified.GetValueOrDefault() < DateTimeOffset.Now.AddSeconds(10));

            ClassicAssert.IsTrue(getThumbnailResponse.Headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedKeyHeader64,
                out var thumbnailSharedSecretEncryptedHeader64));

            // decrypt the payload the same as an FE client
            var thumbnailEkh = EncryptedKeyHeader.FromBase64(thumbnailSharedSecretEncryptedHeader64!.Single());
            ClassicAssert.IsNotNull(thumbnailEkh);
            ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnailEkh.Iv, unencryptedPayload.Iv));
            ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnailEkh.EncryptedAesKey,
                header.SharedSecretEncryptedKeyHeader.EncryptedAesKey));

            var thumbContentBytes = await getThumbnailResponse.Content.ReadAsByteArrayAsync();
            var decryptedThumbnailKeyHeader = thumbnailEkh.DecryptAesToKeyHeader(ref sharedSecret);
            var decryptedThumbnailBytes = decryptedThumbnailKeyHeader.Decrypt(thumbContentBytes);
            ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(decryptedThumbnailBytes, unencryptedThumbnail.Content));
        }
    }

    private async Task<CreateFileResult> UploadFile(TestIdentity identity, UploadFileMetadata uploadedFileMetadata,
        TestPayloadDefinition payloadDefinition,
        bool allowAnonymousReadsOnDrive,
        IApiClientContext callerContext)
    {
        // create drive
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReadsOnDrive);

        // upload file
        var v2Owner = _scaffold.CreateOwnerV2ClientCollection(identity);
        var testPayloads = new List<TestPayloadDefinition> { payloadDefinition };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var response = await v2Owner.DriveWriter.CreateNewUnencryptedFile(callerContext.DriveId, uploadedFileMetadata, uploadManifest, testPayloads);

        // send back details - fileid
        ClassicAssert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        ClassicAssert.IsNotNull(uploadResult);
        return uploadResult;
    }

    private async
        Task<(CreateFileResult uploadResult, string encryptedJsonContent64, List<EncryptedAttachmentUploadResult>
            uploadedThumbnails, List<EncryptedAttachmentUploadResult> uploadedPayloads)>
        UploadEncryptedFile(TestIdentity identity,
            UploadFileMetadata uploadedFileMetadata,
            TestPayloadDefinition payloadDefinition,
            KeyHeader keyHeader,
            IApiClientContext callerContext)
    {
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        // create drive
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", false);

        // upload file
        var testPayloads = new List<TestPayloadDefinition> { payloadDefinition };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };
        
        var v2Owner = _scaffold.CreateOwnerV2ClientCollection(identity);
        var (response, encryptedJsonContent64, uploadedThumbnails, uploadedPayloads) =
            await v2Owner.DriveWriter.CreateEncryptedFile(
                fileMetadata: uploadedFileMetadata,
                storageOptions: new StorageOptions
                {
                    DriveId = callerContext.DriveId
                },
                transitOptions: new TransitOptions(),
                uploadManifest: uploadManifest,
                payloads: testPayloads,
                notificationOptions: null,
                keyHeader: keyHeader
            );

        // send back details - fileid
        ClassicAssert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        ClassicAssert.IsNotNull(uploadResult);

        return (uploadResult, encryptedJsonContent64, uploadedThumbnails, uploadedPayloads);
    }
}