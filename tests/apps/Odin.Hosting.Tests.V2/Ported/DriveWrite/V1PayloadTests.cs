using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>_Universal/DriveTests/DirectDrivePayloadTests_1</c>. Covers the happy-path payload
/// features of a drive used directly by its own identity: fetching a payload by key and checking the
/// response headers it carries, adding a payload to an already-uploaded file (and the metadata the
/// server updates as a side effect), and deleting a payload off an existing file — each across the
/// Owner / App / Guest caller matrix.
/// </summary>
/// <remarks>
/// These drive the <b>V1</b> payload endpoints through the in-process host: the V1-shaped
/// <see cref="UniversalDriveApiClient"/> is reused unchanged via <c>caller.V1.Drive</c> /
/// <c>owner.V1.Drive</c>.
///
/// The original fixture declared two identities (Pippin and Samwise) and pinned one per test, but no
/// test crossed between them — the split was per-test isolation, which <see cref="V2Fixture"/>'s
/// per-test reset already provides. One identity therefore suffices.
///
/// Note the ordering shift the fast framework imposes: the original created the drive, uploaded as
/// owner, and only then called <c>callerContext.Initialize</c>; here
/// <see cref="V2Fixture.SetupCallerWithOwner"/> creates the drive and builds the App / Guest caller in
/// one step, so the caller's registration and grants now exist before the owner's seed upload. No
/// assertion depends on that order.
/// </remarks>
[TestFixture]
public class V1PayloadTests : V2Fixture
{
    /// <summary>
    /// The original's single case source. Payload reads and writes here need a write-capable grant on
    /// the drive, so owner and a write-only app succeed while a write-only guest is refused.
    /// </summary>
    public static IEnumerable<object[]> PayloadCases()
    {
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.Forbidden];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(PayloadCases))]
    public async Task CanGetPayloadByKeyIncludesCorrectHeaders(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        var uploadedPayloadDefinition = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var testPayloads = new List<TestPayloadDefinition> { uploadedPayloadDefinition };

        var uploadManifest = new UploadManifest
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var response = await ownerDriveClient.UploadNewFile(spec.TargetDrive, uploadedFileMetadata, uploadManifest, testPayloads);

        Assert.That(response.IsSuccessStatusCode, Is.True);
        var uploadResult = response.Content;
        Assert.That(uploadResult, Is.Not.Null);

        // get the file header
        var getHeaderResponse = await ownerDriveClient.GetFileHeader(uploadResult.File);
        Assert.That(getHeaderResponse.IsSuccessStatusCode, Is.True);
        var header = getHeaderResponse.Content;
        Assert.That(header, Is.Not.Null);
        Assert.That(header.FileMetadata.Payloads.Count(), Is.EqualTo(1));

        var payloadFromHeader = header.FileMetadata.GetPayloadDescriptor(uploadedPayloadDefinition.Key);
        Assert.That(payloadFromHeader, Is.Not.Null, "payload not found in header");
        Assert.That(payloadFromHeader.Iv, Is.EqualTo(uploadedPayloadDefinition.Iv));

        var callerDriveClient = caller.V1.Drive;

        // Get the payload and check the headers
        var getPayloadKey1Response = await callerDriveClient.GetPayload(uploadResult.File, uploadedPayloadDefinition.Key);

        Assert.That(getPayloadKey1Response.StatusCode, Is.EqualTo(expected));

        if (expected != HttpStatusCode.OK) return;

        //test more
        Assert.That(getPayloadKey1Response.ContentHeaders, Is.Not.Null);
        Assert.That(getPayloadKey1Response.Headers, Is.Not.Null);

        Assert.That(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadEncrypted, out var isEncryptedValues), Is.True);
        Assert.That(bool.Parse(isEncryptedValues.Single()), Is.False);

        Assert.That(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadKey, out var payloadKeyValues), Is.True);
        Assert.That(payloadKeyValues.Single(), Is.EqualTo(uploadedPayloadDefinition.Key));
        Assert.That(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.DecryptedContentType, out var contentTypeValues), Is.True);
        Assert.That(contentTypeValues.Single(), Is.EqualTo(uploadedPayloadDefinition.ContentType));

        Assert.That(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedKeyHeader64, out _), Is.False);

        Assert.That(DriveFileUtility.TryParseLastModifiedHeader(getPayloadKey1Response.ContentHeaders, out var lastModifiedHeaderValue), Is.True);
        Assert.That(lastModifiedHeaderValue.GetValueOrDefault().seconds, Is.EqualTo(payloadFromHeader.LastModified.seconds));
    }

    [Test, TestCaseSource(nameof(PayloadCases))]
    public async Task CanModifyPayloadOnExistingFileAndMetadataIsAutomaticallyUpdated(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        var uploadNewMetadataResponse = await ownerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);

        Assert.That(uploadNewMetadataResponse.IsSuccessStatusCode, Is.True);
        var uploadResult = uploadNewMetadataResponse.Content;
        Assert.That(uploadResult, Is.Not.Null);

        var targetFile = uploadResult.File;
        var targetVersionTag = uploadResult.NewVersionTag;

        //
        // Get the header before we make changes so we have a baseline
        //
        var getHeaderBeforeUploadResponse = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(getHeaderBeforeUploadResponse.IsSuccessStatusCode, Is.True);
        var headerBeforeUpload = getHeaderBeforeUploadResponse.Content;
        Assert.That(headerBeforeUpload, Is.Not.Null);

        //
        // Now add a payload
        //
        var uploadedPayloadDefinition = SamplePayloadDefinitions.GetPayloadDefinition1();
        var testPayloads = new List<TestPayloadDefinition>
        {
            uploadedPayloadDefinition
        };

        var uploadManifest = new UploadManifest
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var callerDriveClient = caller.V1.Drive;

        var uploadPayloadResponse = await callerDriveClient.UploadPayloads(targetFile, targetVersionTag, uploadManifest, testPayloads);
        Assert.That(uploadPayloadResponse.StatusCode, Is.EqualTo(expected));

        if (expected != HttpStatusCode.OK) return;

        //test more
        Assert.That(uploadPayloadResponse.Content!.NewVersionTag, Is.Not.EqualTo(targetVersionTag), "Version tag should have changed");

        // Get the latest file header
        var getHeaderAfterPayloadUploadedResponse = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(getHeaderAfterPayloadUploadedResponse.IsSuccessStatusCode, Is.True);
        var headerAfterPayloadWasUploaded = getHeaderAfterPayloadUploadedResponse.Content;
        Assert.That(headerAfterPayloadWasUploaded, Is.Not.Null);

        Assert.That(headerAfterPayloadWasUploaded.FileMetadata.VersionTag, Is.EqualTo(uploadPayloadResponse.Content.NewVersionTag),
            "Version tag should match the one set by uploading the new payload");

        // Payload should be listed
        Assert.That(headerAfterPayloadWasUploaded.FileMetadata.Payloads.Count(), Is.EqualTo(1));
        var thePayloadDescriptor = headerAfterPayloadWasUploaded.FileMetadata.Payloads
            .SingleOrDefault(p => p.KeyEquals(uploadedPayloadDefinition.Key));
        Assert.That(thePayloadDescriptor, Is.Not.Null);
        Assert.That(thePayloadDescriptor.ContentType, Is.EqualTo(uploadedPayloadDefinition.ContentType));
        Assert.That(thePayloadDescriptor.Thumbnails, Is.EquivalentTo(uploadedPayloadDefinition.Thumbnails));
        Assert.That(thePayloadDescriptor.BytesWritten, Is.EqualTo(uploadedPayloadDefinition.Content.Length));

        // Last modified should be changed
        Assert.That(thePayloadDescriptor.LastModified.milliseconds, Is.GreaterThan(headerBeforeUpload.FileMetadata.Updated.milliseconds));

        // Get the payload
        var getPayloadResponse = await ownerDriveClient.GetPayload(targetFile, uploadedPayloadDefinition.Key);
        Assert.That(getPayloadResponse.IsSuccessStatusCode, Is.True);
        var payloadBytes = await getPayloadResponse.Content.ReadAsByteArrayAsync();
        Assert.That(payloadBytes.Length, Is.EqualTo(thePayloadDescriptor.BytesWritten));
    }

    [Test, TestCaseSource(nameof(PayloadCases))]
    public async Task CanDeletePayloadOnExistingFileAndMetadataIsAutomaticallyUpdated(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        var uploadedPayloadDefinition = SamplePayloadDefinitions.GetPayloadDefinition1();
        var testPayloads = new List<TestPayloadDefinition>
        {
            uploadedPayloadDefinition
        };

        var uploadManifest = new UploadManifest
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var uploadNewFileResponse = await ownerDriveClient.UploadNewFile(spec.TargetDrive, uploadedFileMetadata, uploadManifest, testPayloads);
        Assert.That(uploadNewFileResponse.IsSuccessStatusCode, Is.True);
        var uploadResult = uploadNewFileResponse.Content;
        Assert.That(uploadResult, Is.Not.Null);

        var targetFile = uploadResult.File;
        var targetVersionTag = uploadResult.NewVersionTag;

        // Validate payload exists on the file

        // Get the latest file header
        var getHeaderBeforeDeletingPayloadResponse = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(getHeaderBeforeDeletingPayloadResponse.IsSuccessStatusCode, Is.True);
        var headerBeforePayloadDeleted = getHeaderBeforeDeletingPayloadResponse.Content;
        Assert.That(headerBeforePayloadDeleted, Is.Not.Null);

        // Payload should be listed
        Assert.That(headerBeforePayloadDeleted.FileMetadata.Payloads.Count(), Is.EqualTo(1));
        var thePayloadDescriptor = headerBeforePayloadDeleted.FileMetadata.Payloads
            .SingleOrDefault(p => p.KeyEquals(uploadedPayloadDefinition.Key));
        Assert.That(thePayloadDescriptor, Is.Not.Null);
        Assert.That(thePayloadDescriptor.ContentType, Is.EqualTo(uploadedPayloadDefinition.ContentType));
        Assert.That(thePayloadDescriptor.Thumbnails, Is.EquivalentTo(uploadedPayloadDefinition.Thumbnails));
        Assert.That(thePayloadDescriptor.BytesWritten, Is.EqualTo(uploadedPayloadDefinition.Content.Length));

        var callerDriveClient = caller.V1.Drive;

        // Delete the payload
        var deletePayloadResponse = await callerDriveClient.DeletePayload(targetFile, targetVersionTag, uploadedPayloadDefinition.Key);
        Assert.That(deletePayloadResponse.StatusCode, Is.EqualTo(expected));

        // Test More
        if (expected != HttpStatusCode.OK) return;

        var deletePayloadResult = deletePayloadResponse.Content;
        Assert.That(deletePayloadResult, Is.Not.Null);

        Assert.That(deletePayloadResult.NewVersionTag, Is.Not.EqualTo(targetVersionTag), "version tag should have changed");
        Assert.That(deletePayloadResult.NewVersionTag, Is.Not.EqualTo(Guid.Empty));

        // Get the latest file header
        var getHeaderAfterPayloadUploadedResponse = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(getHeaderAfterPayloadUploadedResponse.IsSuccessStatusCode, Is.True);
        var headerAfterPayloadWasUploaded = getHeaderAfterPayloadUploadedResponse.Content;
        Assert.That(headerAfterPayloadWasUploaded, Is.Not.Null);

        Assert.That(headerAfterPayloadWasUploaded.FileMetadata.VersionTag, Is.EqualTo(deletePayloadResult.NewVersionTag),
            "Version tag should match the one set by deleting the payload");

        // Payload should not be in header
        Assert.That(headerAfterPayloadWasUploaded.FileMetadata.Payloads.Any(), Is.False);

        // Payload should return 404
        var getPayloadResponse = await ownerDriveClient.GetPayload(targetFile, uploadedPayloadDefinition.Key);
        Assert.That(getPayloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
