using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Port of <c>_Universal/Peer/UpdateBatch/UpdateBatchWithRecipientsRemoteUpsertTests</c>. Same
/// update-batch fan-out as <see cref="UpdateBatchWithRecipientsTests"/>, but the file was <i>not</i>
/// distributed on upload, so each recipient has to create its copy from the update alone (the remote
/// upsert path). Three cases: no recipient has the file, a mix of recipients who do and don't, and a
/// recipient-side upsert of a file whose single payload the update deletes.
/// </summary>
/// <remarks>
/// See <see cref="UpdateBatchWithRecipientsTests"/> for the framework departures that apply to all
/// three of these ports (collaboration-drive attribute, <c>WaitForEmptyOutbox</c> to
/// <see cref="UpdateBatchPeerScenario.Distribute"/>, dropped connection cleanup); the arrange half is
/// shared through <see cref="UpdateBatchPeerScenario"/>.
///
/// <b>Carried defect:</b> the third case is named
/// <c>...With1PayloadsAnd1Thumbnails...</c> but seeds
/// <c>SamplePayloadDefinitions.GetPayloadDefinition1()</c>, which has no thumbnails at all — so the
/// thumbnail half of the name never ran in the original either. Left as-is: a port is a move.
/// </remarks>
[TestFixture]
public class UpdateBatchWithRecipientsRemoteUpsertTests : V2Fixture
{
    protected override string[] HostIdentities =>
        [Identities.Frodo, Identities.Sam, Identities.Merry, Identities.Pippin, Identities.TomBombadil];

    /// <summary>The original's four stacked case sources, inline.</summary>
    public static IEnumerable<object[]> UpdateBatchCases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write, [PermissionKeys.UseTransitWrite]), HttpStatusCode.OK];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.Forbidden];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Forbidden];
    }

    [Test, TestCaseSource(nameof(UpdateBatchCases))]
    public async Task CanUpdateBatchAndDistributeToRecipientsWhenTargetFileDoesNotExistOnRemoteServer(
        CallerSpec spec, HttpStatusCode expected)
    {
        var sender = await LoginAsOwner();
        var targetDrive = spec.TargetDrive;
        await UpdateBatchPeerScenario.CreateCollaborationDrive(sender, targetDrive);

        //
        // Setup - upload a new file with payloads
        //

        var recipients = await UpdateBatchPeerScenario.SetupRecipients(Host, sender, [Identities.Sam, Identities.Merry], targetDrive);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Authenticated);
        uploadedFileMetadata.AllowDistribution = true;

        // Note: no transit options on initial upload to ensure
        // the file does not exist on the remote server
        var transitOptions = new TransitOptions { };

        var uploadNewFileResponse = await sender.V1.Drive.UploadNewMetadata(targetDrive, uploadedFileMetadata, transitOptions);
        Assert.That(uploadNewFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await UpdateBatchPeerScenario.Distribute(sender, recipients, targetDrive);

        var uploadResult = uploadNewFileResponse.Content;
        var targetFile = uploadResult!.File;
        var targetGlobalTransitIdFileIdentifier = uploadResult.GlobalTransitIdFileIdentifier;

        //
        // Act - call update batch with UpdateLocale = Local
        //

        // change around some data
        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here...";
        updatedFileMetadata.AppData.DataType = 991;
        updatedFileMetadata.VersionTag = uploadResult.NewVersionTag;

        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = targetFile.ToFileIdentifier(),
            Recipients = recipients.Select(r => r.Identity).ToList(),
            Manifest = new UploadManifest
            {
                PayloadDescriptors = []
            }
        };

        var caller = await spec.Build(sender);

        var updateFileResponse = await caller.V1.Drive.UpdateFile(updateInstructionSet, updatedFileMetadata, []);
        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(expected));

        // Let's test more
        if (expected != HttpStatusCode.OK) return;

        Assert.That(updateFileResponse.Content, Is.Not.Null);
        await UpdateBatchPeerScenario.Distribute(sender, recipients, targetDrive);

        //
        // ensure the local file exists and is updated correctly
        //
        var getHeaderResponse = await sender.V1.Drive.GetFileHeader(targetFile);
        Assert.That(getHeaderResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var header = getHeaderResponse.Content;
        Assert.That(header, Is.Not.Null);
        Assert.That(header!.FileMetadata.AppData.Content, Is.EqualTo(updatedFileMetadata.AppData.Content));
        Assert.That(header.FileMetadata.AppData.DataType, Is.EqualTo(updatedFileMetadata.AppData.DataType));
        Assert.That(header.FileMetadata.VersionTag, Is.EqualTo(updateFileResponse.Content!.NewVersionTag));
        Assert.That(header.FileMetadata.Payloads, Is.Empty);

        // Ensure we find the file on the recipient
        //
        await DriveAsserts.AssertFileFoundByDataType(
            sender.V1.Drive, targetFile.TargetDrive, updatedFileMetadata.AppData.DataType, targetFile.FileId);

        // ensure the recipients get the file

        foreach (var recipient in recipients)
        {
            var recipientFileResponse = await recipient.V1.Drive.QueryByGlobalTransitId(targetGlobalTransitIdFileIdentifier);
            var remoteFileHeader = recipientFileResponse.Content!.SearchResults.FirstOrDefault();

            Assert.That(remoteFileHeader, Is.Not.Null, $"recipient {recipient.Identity} should have the file");
            Assert.That(remoteFileHeader!.FileMetadata.AppData.Content, Is.EqualTo(updatedFileMetadata.AppData.Content));
            Assert.That(remoteFileHeader.FileMetadata.AppData.DataType, Is.EqualTo(updatedFileMetadata.AppData.DataType));
            Assert.That(remoteFileHeader.FileMetadata.VersionTag, Is.EqualTo(updateFileResponse.Content.NewVersionTag));
            Assert.That(remoteFileHeader.FileMetadata.Payloads, Is.Empty);
        }
    }

    [Test, TestCaseSource(nameof(UpdateBatchCases))]
    public async Task CanUpdateBatchAndDistributeToRecipientsWhenTargetFileDoesNotExistOnRemoteServerMixed(
        CallerSpec spec, HttpStatusCode expected)
    {
        var sender = await LoginAsOwner();
        var targetDrive = spec.TargetDrive;
        await UpdateBatchPeerScenario.CreateCollaborationDrive(sender, targetDrive);

        //
        // Setup - upload a new file with payloads
        //

        var recipientsWithTargetFile = await UpdateBatchPeerScenario.SetupRecipients(
            Host, sender, [Identities.Sam, Identities.TomBombadil], targetDrive);
        var recipientsWithOutTargetFile = await UpdateBatchPeerScenario.SetupRecipients(
            Host, sender, [Identities.Merry, Identities.Pippin], targetDrive);

        var allRecipients = recipientsWithTargetFile.Concat(recipientsWithOutTargetFile).ToList();

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Authenticated);
        uploadedFileMetadata.AllowDistribution = true;

        // Note: only the recipients who are meant to already hold the file are addressed here
        var transitOptions = new TransitOptions
        {
            Recipients = recipientsWithTargetFile.Select(r => (string)r.Identity).ToList(),
        };

        var uploadNewFileResponse = await sender.V1.Drive.UploadNewMetadata(targetDrive, uploadedFileMetadata, transitOptions);
        Assert.That(uploadNewFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await UpdateBatchPeerScenario.Distribute(sender, allRecipients, targetDrive);

        var uploadResult = uploadNewFileResponse.Content;
        var targetFile = uploadResult!.File;
        var targetGlobalTransitIdFileIdentifier = uploadResult.GlobalTransitIdFileIdentifier;

        //
        // Act - call update batch with UpdateLocale = Local
        //

        // change around some data
        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here...";
        updatedFileMetadata.AppData.DataType = 991;
        updatedFileMetadata.VersionTag = uploadResult.NewVersionTag;

        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = targetFile.ToFileIdentifier(),
            Recipients = allRecipients.Select(r => r.Identity).ToList(),
            Manifest = new UploadManifest
            {
                PayloadDescriptors = []
            }
        };

        var caller = await spec.Build(sender);

        var updateFileResponse = await caller.V1.Drive.UpdateFile(updateInstructionSet, updatedFileMetadata, []);
        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(expected));

        // Let's test more
        if (expected != HttpStatusCode.OK) return;

        Assert.That(updateFileResponse.Content, Is.Not.Null);
        await UpdateBatchPeerScenario.Distribute(sender, allRecipients, targetDrive);

        //
        // ensure the local file exists and is updated correctly
        //
        var getHeaderResponse = await sender.V1.Drive.GetFileHeader(targetFile);
        Assert.That(getHeaderResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var header = getHeaderResponse.Content;
        Assert.That(header, Is.Not.Null);
        Assert.That(header!.FileMetadata.AppData.Content, Is.EqualTo(updatedFileMetadata.AppData.Content));
        Assert.That(header.FileMetadata.AppData.DataType, Is.EqualTo(updatedFileMetadata.AppData.DataType));
        Assert.That(header.FileMetadata.VersionTag, Is.EqualTo(updateFileResponse.Content!.NewVersionTag));
        Assert.That(header.FileMetadata.Payloads, Is.Empty);

        // Ensure we find the file on the recipient
        //
        await DriveAsserts.AssertFileFoundByDataType(
            sender.V1.Drive, targetFile.TargetDrive, updatedFileMetadata.AppData.DataType, targetFile.FileId);

        // ensure the recipients get the file

        foreach (var recipient in allRecipients)
        {
            var recipientFileResponse = await recipient.V1.Drive.QueryByGlobalTransitId(targetGlobalTransitIdFileIdentifier);
            var remoteFileHeader = recipientFileResponse.Content!.SearchResults.FirstOrDefault();

            Assert.That(remoteFileHeader, Is.Not.Null, $"recipient {recipient.Identity} should have the file");
            Assert.That(remoteFileHeader!.FileMetadata.AppData.Content, Is.EqualTo(updatedFileMetadata.AppData.Content));
            Assert.That(remoteFileHeader.FileMetadata.AppData.DataType, Is.EqualTo(updatedFileMetadata.AppData.DataType));
            Assert.That(remoteFileHeader.FileMetadata.VersionTag, Is.EqualTo(updateFileResponse.Content.NewVersionTag));
            Assert.That(remoteFileHeader.FileMetadata.Payloads, Is.Empty);
        }
    }

    [Test, TestCaseSource(nameof(UpdateBatchCases))]
    public async Task CanUpdateBatchAndDistributeToRecipientsWith1PayloadsAnd1ThumbnailsWhenTargetFileDoesNotExistOnRemoteServer(
        CallerSpec spec, HttpStatusCode expected)
    {
        var sender = await LoginAsOwner();
        var targetDrive = spec.TargetDrive;
        await UpdateBatchPeerScenario.CreateCollaborationDrive(sender, targetDrive);

        //
        // Setup - upload a new file with payloads
        //

        var recipients = await UpdateBatchPeerScenario.SetupRecipients(Host, sender, [Identities.Sam, Identities.Merry], targetDrive);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Authenticated);
        uploadedFileMetadata.AllowDistribution = true;
        var transitOptions = new TransitOptions { }; // Note: no transit options on initial upload to ensure
        // the file does not exist on the remote server

        var uploadedPayloadDefinition = SamplePayloadDefinitions.GetPayloadDefinition1();
        var testPayloads = new List<TestPayloadDefinition>()
        {
            uploadedPayloadDefinition
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var uploadNewFileResponse =
            await sender.V1.Drive.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, testPayloads, transitOptions);
        Assert.That(uploadNewFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await UpdateBatchPeerScenario.Distribute(sender, recipients, targetDrive);

        var uploadResult = uploadNewFileResponse.Content;
        var targetFile = uploadResult!.File;
        var targetGlobalTransitIdFileIdentifier = uploadResult.GlobalTransitIdFileIdentifier;

        //
        // Act - call update batch with UpdateLocale = Local
        //

        // change around some data
        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here...";
        updatedFileMetadata.AppData.DataType = 991;
        updatedFileMetadata.VersionTag = uploadResult.NewVersionTag;

        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = targetFile.ToFileIdentifier(),
            Recipients = recipients.Select(r => r.Identity).ToList(),
            Manifest = new UploadManifest
            {
                PayloadDescriptors =
                [
                    new UploadManifestPayloadDescriptor
                    {
                        PayloadUpdateOperationType = PayloadUpdateOperationType.DeletePayload,
                        PayloadKey = testPayloads.Single().Key,
                    }
                ]
            }
        };

        var caller = await spec.Build(sender);

        var updateFileResponse = await caller.V1.Drive.UpdateFile(updateInstructionSet, updatedFileMetadata, []);
        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(expected));

        // Let's test more
        if (expected != HttpStatusCode.OK) return;

        Assert.That(updateFileResponse.Content, Is.Not.Null);
        await UpdateBatchPeerScenario.Distribute(sender, recipients, targetDrive);

        //
        // ensure the local file exists and is updated correctly
        //
        var getHeaderResponse = await sender.V1.Drive.GetFileHeader(targetFile);
        Assert.That(getHeaderResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var header = getHeaderResponse.Content;
        Assert.That(header, Is.Not.Null);
        Assert.That(header!.FileMetadata.AppData.Content, Is.EqualTo(updatedFileMetadata.AppData.Content));
        Assert.That(header.FileMetadata.AppData.DataType, Is.EqualTo(updatedFileMetadata.AppData.DataType));
        Assert.That(header.FileMetadata.VersionTag, Is.EqualTo(updateFileResponse.Content!.NewVersionTag));
        Assert.That(header.FileMetadata.Payloads, Is.Empty);

        // Ensure we find the file on the recipient
        //
        await DriveAsserts.AssertFileFoundByDataType(
            sender.V1.Drive, targetFile.TargetDrive, updatedFileMetadata.AppData.DataType, targetFile.FileId);

        // ensure the recipients get the file

        foreach (var recipient in recipients)
        {
            var recipientFileResponse = await recipient.V1.Drive.QueryByGlobalTransitId(targetGlobalTransitIdFileIdentifier);
            var remoteFileHeader = recipientFileResponse.Content!.SearchResults.FirstOrDefault();

            Assert.That(remoteFileHeader, Is.Not.Null, $"recipient {recipient.Identity} should have the file");
            Assert.That(remoteFileHeader!.FileMetadata.AppData.Content, Is.EqualTo(updatedFileMetadata.AppData.Content));
            Assert.That(remoteFileHeader.FileMetadata.AppData.DataType, Is.EqualTo(updatedFileMetadata.AppData.DataType));
            Assert.That(remoteFileHeader.FileMetadata.VersionTag, Is.EqualTo(updateFileResponse.Content.NewVersionTag));
            Assert.That(remoteFileHeader.FileMetadata.Payloads, Is.Empty);

            var getPayloadResponse = await recipient.V1.Drive.GetPayload(new ExternalFileIdentifier()
            {
                FileId = remoteFileHeader.FileId,
                TargetDrive = targetGlobalTransitIdFileIdentifier.TargetDrive
            }, testPayloads.Single().Key);

            Assert.That(getPayloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }
    }
}
