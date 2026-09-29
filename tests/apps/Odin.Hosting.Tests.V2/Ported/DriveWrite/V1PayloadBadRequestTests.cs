using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>_Universal/DriveTests/DirectDrivePayloadTests_BadRequest_Tests</c>. Covers the
/// malformed-request paths of the payload endpoints across the Owner / App / Guest matrix: deleting
/// or adding a payload against a stale version tag, uploading a file whose manifest carries duplicate
/// payload keys, and payload keys that break the key format rules.
/// </summary>
/// <remarks>
/// These drive the <b>V1</b> payload endpoints through the in-process host: the V1-shaped
/// <see cref="UniversalDriveApiClient"/> is reused unchanged, resolved against the fixture's
/// <c>Factory</c> via <c>InProcessApiClientFactory</c>'s V1 path normalization.
///
/// The original fixture declared two identities (Pippin and Samwise) but each test used exactly one
/// of them and never crossed between them — the split was per-test isolation, which
/// <see cref="V2Fixture"/>'s per-test reset already provides. One identity therefore suffices.
/// </remarks>
[TestFixture]
public class V1PayloadBadRequestTests : V2Fixture
{
    /// <summary>
    /// The version-tag tests: owner and app get as far as the handler and are refused with
    /// BadRequest, while a write-only guest is turned away at authz first.
    /// </summary>
    public static IEnumerable<object[]> VersionTagCases()
    {
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.BadRequest];
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.BadRequest];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.Forbidden];
    }

    /// <summary>
    /// The manifest-validation tests: the request is rejected before any permission-sensitive work,
    /// so every caller — guest included — sees BadRequest.
    /// </summary>
    public static IEnumerable<object[]> BadRequestCases()
    {
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.BadRequest];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.BadRequest];
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.BadRequest];
    }

    [Test, TestCaseSource(nameof(VersionTagCases))]
    public async Task FailToDeletePayloadOnExistingFileWhenInvalidVersionTagIsSpecified(CallerSpec spec, HttpStatusCode expected)
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

        var uploadResponse = await ownerDriveClient.UploadNewFile(spec.TargetDrive, uploadedFileMetadata, uploadManifest, testPayloads);
        Assert.That(uploadResponse.IsSuccessStatusCode, Is.True);
        var uploadResult = uploadResponse.Content;
        Assert.That(uploadResult, Is.Not.Null);

        var targetFile = uploadResult.File;
        var targetVersionTag = Guid.Parse("00000000-0000-0000-0000-128d8b157c80"); // an invalid version tag

        // Validate payload exists on the file

        // Get the latest file header
        var getHeaderBeforeDeletingPayloadResponse = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(getHeaderBeforeDeletingPayloadResponse.IsSuccessStatusCode, Is.True);
        var headerBeforePayloadDeleted = getHeaderBeforeDeletingPayloadResponse.Content;
        Assert.That(headerBeforePayloadDeleted, Is.Not.Null);

        // Payload should be listed
        Assert.That(headerBeforePayloadDeleted.FileMetadata.Payloads.Count(), Is.EqualTo(1));
        var thePayloadDescriptor = headerBeforePayloadDeleted.FileMetadata.Payloads.SingleOrDefault(p => p.KeyEquals(uploadedPayloadDefinition.Key));
        Assert.That(thePayloadDescriptor, Is.Not.Null);
        Assert.That(thePayloadDescriptor.ContentType, Is.EqualTo(uploadedPayloadDefinition.ContentType));
        Assert.That(thePayloadDescriptor.Thumbnails, Is.EquivalentTo(uploadedPayloadDefinition.Thumbnails));
        Assert.That(thePayloadDescriptor.BytesWritten, Is.EqualTo(uploadedPayloadDefinition.Content.Length));

        // Attempt Delete the payload
        var callerDriveClient = caller.V1.Drive;

        var deletePayloadResponse = await callerDriveClient.DeletePayload(targetFile, targetVersionTag, uploadedPayloadDefinition.Key);
        Assert.That(deletePayloadResponse.StatusCode, Is.EqualTo(expected));
        Assert.That(deletePayloadResponse.Content, Is.Null);

        // Get the latest file header
        var getHeaderAfterPayloadUploadedResponse = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(getHeaderAfterPayloadUploadedResponse.IsSuccessStatusCode, Is.True);
        var headerAfterPayloadWasUploaded = getHeaderAfterPayloadUploadedResponse.Content;
        Assert.That(headerAfterPayloadWasUploaded, Is.Not.Null);

        // Payload should still be in header
        Assert.That(headerBeforePayloadDeleted.FileMetadata.Payloads.Count(), Is.EqualTo(1));
        var thePayloadDescriptorAfterAttemptingDelete =
            headerBeforePayloadDeleted.FileMetadata.Payloads.SingleOrDefault(p => p.KeyEquals(uploadedPayloadDefinition.Key));
        Assert.That(thePayloadDescriptorAfterAttemptingDelete, Is.Not.Null);
        Assert.That(thePayloadDescriptorAfterAttemptingDelete.ContentType, Is.EqualTo(uploadedPayloadDefinition.ContentType));
        Assert.That(thePayloadDescriptorAfterAttemptingDelete.Thumbnails, Is.EquivalentTo(uploadedPayloadDefinition.Thumbnails));
        Assert.That(thePayloadDescriptorAfterAttemptingDelete.BytesWritten, Is.EqualTo(uploadedPayloadDefinition.Content.Length));

        // Payload should still be on server
        var getPayloadResponse = await ownerDriveClient.GetPayload(targetFile, uploadedPayloadDefinition.Key);
        Assert.That(getPayloadResponse.IsSuccessStatusCode, Is.True);
    }

    [Test, TestCaseSource(nameof(VersionTagCases))]
    public async Task FailWhenModifyingPayloadOnExistingFileAndInvalidVersionTagIsSpecified(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        var uploadNewMetadataResponse = await ownerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);

        Assert.That(uploadNewMetadataResponse.IsSuccessStatusCode, Is.True);
        var uploadResult = uploadNewMetadataResponse.Content;
        Assert.That(uploadResult, Is.Not.Null);

        var targetFile = uploadResult.File;
        var targetVersionTag = Guid.Parse("00000000-0000-0000-0000-928d8b157c80"); // an invalid version tag

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
    }

    [Test, TestCaseSource(nameof(BadRequestCases))]
    public async Task FailWhenDuplicatePayloadKeys(CallerSpec spec, HttpStatusCode expected)
    {
        var caller = await SetupCaller(spec);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        //Note: the duplicate keys
        var testPayloads = new List<TestPayloadDefinition>
        {
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1(), //Note: the duplicate keys are intentional
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1() //Note: the duplicate keys are intentional
        };

        var uploadManifest = new UploadManifest
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var callerDriveClient = caller.V1.Drive;

        var response = await callerDriveClient.UploadNewFile(spec.TargetDrive, uploadedFileMetadata, uploadManifest, testPayloads);
        Assert.That(response.StatusCode, Is.EqualTo(expected));
    }

    [Test, TestCaseSource(nameof(BadRequestCases))]
    public async Task FailIfPayloadKeyIncludesInvalidChars(CallerSpec spec, HttpStatusCode expected)
    {
        var caller = await SetupCaller(spec);
        var callerDriveClient = caller.V1.Drive;

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        const string pkTooLong = "abckjalcialakk";
        const string pkNoCapitalLettersAllowed = "ABC23duu";

        var invalidKeys = new List<string>
        {
            pkTooLong, pkNoCapitalLettersAllowed
        };

        foreach (var invalidKey in invalidKeys)
        {
            var testPayloads = new List<TestPayloadDefinition>
            {
                new()
                {
                    Key = invalidKey,
                    ContentType = "text/plain",
                    Content = "some content for payload key 1".ToUtf8ByteArray(),
                }
            };

            var uploadManifest = new UploadManifest
            {
                PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
            };

            var response = await callerDriveClient.UploadNewFile(spec.TargetDrive, uploadedFileMetadata, uploadManifest, testPayloads);
            Assert.That(response.StatusCode, Is.EqualTo(expected), $"invalid key {invalidKey} should have failed");
        }
    }
}
