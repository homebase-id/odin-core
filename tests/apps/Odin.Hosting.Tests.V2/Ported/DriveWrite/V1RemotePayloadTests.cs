using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>_Universal/DriveTests/RemotePayload/RemotePayloadTests</c>. Covers files whose payloads
/// live on another identity (<see cref="DataSource"/> with <c>PayloadsAreRemote</c>): uploading such a
/// file with payload descriptors but no payload binaries and reading the descriptors back off the
/// header, the two upload rejections (binaries sent anyway; descriptors left empty), and the two
/// rejections when an existing local file is edited to point its payloads at a remote identity
/// (update-batch and metadata-overwrite) — each across the caller matrix.
/// </summary>
/// <remarks>
/// These drive the <b>V1</b> drive endpoints through the in-process host via the V1-shaped
/// <see cref="UniversalDriveApiClient"/>, reached here through <c>caller.V1.Drive</c> /
/// <c>owner.V1.Drive</c>.
///
/// <b>No peer traffic.</b> The original declared Frodo and Samwise, but Frodo is only ever used as a
/// value — the <see cref="DataSource.Identity"/> written into the file metadata. Nothing connects the
/// two identities, nothing is sent over the outbox, and no inbox is processed: every request in the
/// fixture is local to Samwise. Both identities are still hosted, and Samwise is listed first so the
/// default <c>SetupCallerWithOwner</c> identity is the one the original operated as, leaving Frodo as
/// the "remote" identity the data source names.
///
/// The last test is the original's placeholder: it asserts nothing but <c>Assert.Pass</c> and keeps
/// its case source so the expanded case count is unchanged. The four negative tests likewise keep the
/// <c>expected</c> parameter they never read — in the original both rows carry <c>OK</c> (both callers
/// reach the handler) and the assertion is the specific <c>BadRequest</c> error code instead.
/// </remarks>
[TestFixture]
public class V1RemotePayloadTests : V2Fixture
{
    /// <summary>
    /// Samwise first: he is the identity the original ran every request as. Frodo is hosted only
    /// because the original hosted him; he is named as the remote data-source identity but never
    /// contacted.
    /// </summary>
    protected override string[] HostIdentities => [Identities.Sam, Identities.Frodo];

    /// <summary>
    /// The original's single case source, inline. Writing to the drive needs a write grant, which both
    /// the write-only app and the owner hold, so both reach the handler.
    /// </summary>
    public static IEnumerable<object[]> RemotePayloadCases()
    {
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(RemotePayloadCases))]
    public async Task CanUploadNewFileWithRemotePayloadIdentityAndDescriptors(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        var uploadedPayloadDefinition = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var testPayloads = new List<TestPayloadDefinition>() { uploadedPayloadDefinition };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        // Note we add descriptors but no payload binary data
        var callerDriveClient = caller.V1.Drive;

        var remotePayloadInfo = new DataSource()
        {
            Identity = new OdinId(Identities.Frodo),
            DriveId = spec.TargetDrive.Alias,
            PayloadsAreRemote = true
        };

        uploadedFileMetadata.DataSource = remotePayloadInfo;
        // dont send any payloads since they remote
        var response = await callerDriveClient.UploadNewFile(spec.TargetDrive, uploadedFileMetadata, uploadManifest, payloads: []);

        Assert.That(response.StatusCode, Is.EqualTo(expected));

        if (expected != HttpStatusCode.OK) return;

        // validate the file is upload and there is a remote payload identity
        var uploadResult = response.Content;
        Assert.That(uploadResult, Is.Not.Null);

        // get the file header
        var getHeaderResponse = await owner.V1.Drive.GetFileHeader(uploadResult.File);
        Assert.That(getHeaderResponse.IsSuccessStatusCode, Is.True);

        var header = getHeaderResponse.Content;
        Assert.That(header, Is.Not.Null);
        Assert.That(header.FileMetadata.Payloads.Count(), Is.EqualTo(1));
        Assert.That(header.FileMetadata.DataSource.Identity, Is.EqualTo(remotePayloadInfo.Identity));
        Assert.That(header.FileMetadata.DataSource.DriveId, Is.EqualTo(remotePayloadInfo.DriveId));

        var payloadDescriptor = header.FileMetadata.GetPayloadDescriptor(uploadedPayloadDefinition.Key);
        Assert.That(payloadDescriptor, Is.Not.Null);
        Assert.That(payloadDescriptor.Key, Is.EqualTo(uploadedPayloadDefinition.Key));
        Assert.That(payloadDescriptor.Iv, Is.EquivalentTo(Guid.Empty.ToByteArray()));
        Assert.That(payloadDescriptor.ContentType, Is.EqualTo(uploadedPayloadDefinition.ContentType));
        Assert.That(payloadDescriptor.DescriptorContent, Is.EqualTo(uploadedPayloadDefinition.DescriptorContent));
        Assert.That(payloadDescriptor.Uid.uniqueTime, Is.EqualTo(0));
        Assert.That(payloadDescriptor.BytesWritten, Is.EqualTo(0));
        Assert.That(payloadDescriptor.PreviewThumbnail.BytesWritten, Is.EqualTo(0));
        Assert.That(payloadDescriptor.PreviewThumbnail.PixelHeight, Is.EqualTo(uploadedPayloadDefinition.PreviewThumbnail.PixelHeight));
        Assert.That(payloadDescriptor.PreviewThumbnail.PixelWidth, Is.EqualTo(uploadedPayloadDefinition.PreviewThumbnail.PixelWidth));
        Assert.That(payloadDescriptor.PreviewThumbnail.ContentType, Is.EqualTo(uploadedPayloadDefinition.PreviewThumbnail.ContentType));
        Assert.That(payloadDescriptor.PreviewThumbnail.Content, Is.EquivalentTo(uploadedPayloadDefinition.PreviewThumbnail.Content));

        foreach (var t in payloadDescriptor.Thumbnails)
        {
            var serverThumbnail = uploadedPayloadDefinition.Thumbnails
                .SingleOrDefault(x => x.PixelHeight == t.PixelHeight && x.PixelWidth == t.PixelWidth);

            Assert.That(serverThumbnail, Is.Not.Null);
            Assert.That(t.BytesWritten, Is.EqualTo(0));
            Assert.That(t.PixelHeight, Is.EqualTo(serverThumbnail.PixelHeight));
            Assert.That(t.PixelWidth, Is.EqualTo(serverThumbnail.PixelWidth));
            Assert.That(t.ContentType, Is.EqualTo(serverThumbnail.ContentType));
        }
    }

    [Test, TestCaseSource(nameof(RemotePayloadCases))]
    public async Task FailToUploadNewFileWhenRemotePayloadIdentityIsSetWithARemotePayloadAndPayloadBinaryIsSentWithUpload(
        CallerSpec spec, HttpStatusCode expected)
    {
        var caller = await SetupCaller(spec);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        var uploadedPayloadDefinition = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var testPayloads = new List<TestPayloadDefinition>() { uploadedPayloadDefinition };

        uploadedFileMetadata.DataSource = new DataSource()
        {
            Identity = new OdinId(Identities.Frodo),
            DriveId = spec.TargetDrive.Alias,
            PayloadsAreRemote = true
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var callerDriveClient = caller.V1.Drive;
        var response = await callerDriveClient.UploadNewFile(spec.TargetDrive, uploadedFileMetadata, uploadManifest, testPayloads);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(WebScaffold.GetErrorCode(response.Error), Is.EqualTo(OdinClientErrorCode.InvalidPayloadContent));
    }

    [Test, TestCaseSource(nameof(RemotePayloadCases))]
    public async Task FailToUploadNewFileWhenRemotePayloadIdentityIsSetWithARemotePayloadAndDescriptorsAreEmpty(
        CallerSpec spec, HttpStatusCode expected)
    {
        var caller = await SetupCaller(spec);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        uploadedFileMetadata.DataSource = new DataSource()
        {
            Identity = new OdinId(Identities.Frodo),
            DriveId = spec.TargetDrive.Alias,
            PayloadsAreRemote = true
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = []
        };

        var callerDriveClient = caller.V1.Drive;
        var response = await callerDriveClient.UploadNewFile(spec.TargetDrive, uploadedFileMetadata, uploadManifest, []);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(WebScaffold.GetErrorCode(response.Error), Is.EqualTo(OdinClientErrorCode.MissingPayloadKeys));
    }

    [Test, TestCaseSource(nameof(RemotePayloadCases))]
    public async Task FailToModifyRemotePayloadIdentityOnExistingFile_UpdateBatch(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var remoteOdinId = new DataSource()
        {
            Identity = new OdinId(Identities.Frodo),
            DriveId = spec.TargetDrive.Alias
        };

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        var uploadedPayloadDefinition = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var testPayloads = new List<TestPayloadDefinition>() { uploadedPayloadDefinition };

        uploadedFileMetadata.DataSource = null;

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var uploadNewFileResponse =
            await owner.V1.Drive.UploadNewFile(spec.TargetDrive, uploadedFileMetadata, uploadManifest, payloads: []);
        Assert.That(uploadNewFileResponse.IsSuccessStatusCode, Is.True);
        var uploadedFile = uploadNewFileResponse.Content;
        Assert.That(uploadedFile, Is.Not.Null);

        //
        // Now try to modify the remote identity
        //

        uploadedFileMetadata.DataSource = remoteOdinId;
        uploadedFileMetadata.VersionTag = uploadedFile.NewVersionTag;

        var callerDriveClient = caller.V1.Drive;
        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = uploadedFile.File.ToFileIdentifier(),
            Manifest = new UploadManifest
            {
                PayloadDescriptors = null
            }
        };

        var response = await callerDriveClient.UpdateFile(updateInstructionSet, uploadedFileMetadata, payloads: []);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var errorCode = WebScaffold.GetErrorCode(response.Error);
        Assert.That(errorCode, Is.EqualTo(OdinClientErrorCode.CannotModifyRemotePayloadIdentity));
    }

    [Test, TestCaseSource(nameof(RemotePayloadCases))]
    public async Task FailToModifyRemotePayloadIdentityOnExistingFile_OverwriteMetadata(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        var uploadedPayloadDefinition = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var testPayloads = new List<TestPayloadDefinition>() { uploadedPayloadDefinition };

        uploadedFileMetadata.DataSource = null;

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var uploadNewFileResponse =
            await owner.V1.Drive.UploadNewFile(spec.TargetDrive, uploadedFileMetadata, uploadManifest, payloads: []);
        Assert.That(uploadNewFileResponse.IsSuccessStatusCode, Is.True);

        var uploadedFile = uploadNewFileResponse.Content;
        Assert.That(uploadedFile, Is.Not.Null);

        //
        // Now try to modify the remote identity
        //

        var remoteOdinId = new DataSource()
        {
            Identity = new OdinId(Identities.Frodo),
            DriveId = spec.TargetDrive.Alias,
        };
        uploadedFileMetadata.DataSource = remoteOdinId;

        var callerDriveClient = caller.V1.Drive;

        var response = await callerDriveClient.UpdateExistingMetadata(uploadedFile.File, uploadedFile.NewVersionTag, uploadedFileMetadata);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var errorCode = WebScaffold.GetErrorCode(response.Error);
        Assert.That(errorCode, Is.EqualTo(OdinClientErrorCode.CannotModifyRemotePayloadIdentity));
    }

    [Test, TestCaseSource(nameof(RemotePayloadCases))]
    public async Task FailToModifyRemotePayloadIdentityOnExistingFile_OverwriteFile(CallerSpec spec, HttpStatusCode expected)
    {
        await Task.CompletedTask;
        Assert.Pass("Overwrite file is going away");
    }
}
