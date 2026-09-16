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
/// Port of <c>_Universal/Peer/UpdateBatch/UpdateBatchWithRecipientsTests</c>. Covers update-batch
/// with <see cref="UpdateLocale.Local"/> <b>and</b> peer recipients: the file is distributed on
/// upload, then updated locally and the update fanned out to every recipient, who must end up with
/// the same content, data type and version tag. Second case adds a payload to the initial upload and
/// has the update delete it, so the recipients' copy must 404 on that payload key.
/// </summary>
/// <remarks>
/// Drives the <b>V1</b> drive endpoints (<c>/api/owner/v1/drive/files/update</c>) through
/// <c>caller.V1.Drive</c>; the peer hop runs in-process via <c>TestPeerHttpClientFactory</c>.
///
/// Departures from the original, all forced by the framework rather than chosen:
/// <list type="bullet">
/// <item>The original's <c>PrepareScenario</c>/<c>SetupRecipients</c> hand-roll is
/// <see cref="PeerFlow.ConnectAsync"/> here. The drives are still created by hand rather than by
/// <see cref="PeerFlow.CreatePeerDriveAsync"/> because they need the
/// <see cref="BuiltInDriveAttributes.IsCollaborativeChannel"/> attribute, which no framework helper
/// carries; on a collaboration drive <c>PeerFileUpdateWriter</c> keeps the sender's ACL instead of
/// narrowing the recipient's copy to owner-only, so the flag is load-bearing for this suite.</item>
/// <item><c>WaitForEmptyOutbox</c> is a passive poll of a background service the fast host never
/// starts; every call becomes <c>Sync.DrainOutboxAsync</c> plus an explicit
/// <c>Sync.ProcessInboxAsync</c> on each recipient.</item>
/// <item>The original's <c>DisableAutoAcceptIntroductions</c> calls and trailing <c>Disconnect</c>
/// are gone: both exist to stop state leaking between tests on the shared WebScaffold, which
/// <c>V2Fixture</c>'s per-test reset already guarantees.</item>
/// <item>The caller is built after the seed, exactly where the original called
/// <c>callerContext.Initialize</c>, so <c>SetupCallerWithOwner</c>'s create-drive-then-build
/// ordering is not in play here.</item>
/// </list>
/// The identities are the fixture defaults rather than the original's Pippin/Frodo/Merry — every
/// host boots its own tenants, so which name plays sender is arbitrary.
///
/// <b>Carried defect:</b> <see cref="CanUpdateBatchAndDistributeToRecipientsWith1PayloadsAnd1Thumbnails"/>
/// seeds <c>SamplePayloadDefinitions.GetPayloadDefinition1()</c>, whose <c>Thumbnails</c> list is
/// empty — so the thumbnail half of the name has never been exercised. Left alone: a port is a move.
/// </remarks>
[TestFixture]
public class UpdateBatchWithRecipientsTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Merry];

    /// <summary>
    /// The original's four stacked case sources, inline. Both allowed rows carry
    /// <see cref="PermissionKeys.UseTransitWrite"/>, since the update fans out over peer.
    /// </summary>
    public static IEnumerable<object[]> UpdateBatchCases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write, [PermissionKeys.UseTransitWrite]), HttpStatusCode.OK];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.Forbidden];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Forbidden];
    }

    [Test, TestCaseSource(nameof(UpdateBatchCases))]
    public async Task CanUpdateBatchAndDistributeToRecipients(CallerSpec spec, HttpStatusCode expected)
    {
        var sender = await LoginAsOwner();
        var targetDrive = spec.TargetDrive;
        await UpdateBatchPeerScenario.CreateCollaborationDrive(sender, targetDrive);

        //
        // Setup - upload a new file with payloads
        //

        var recipients = await UpdateBatchPeerScenario.SetupRecipients(Host, sender, [Identities.Sam, Identities.Merry], targetDrive);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        uploadedFileMetadata.AllowDistribution = true;
        var transitOptions = new TransitOptions
        {
            IsTransient = false,
            Recipients = recipients.Select(r => (string)r.Identity).ToList(),
            RemoteTargetDrive = null,
            Priority = OutboxPriority.High
        };

        var uploadNewFileResponse = await sender.V1.Drive.UploadNewMetadata(targetDrive, uploadedFileMetadata, transitOptions);
        uploadedFileMetadata.AccessControlList = AccessControlList.Authenticated;
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
    public async Task CanUpdateBatchAndDistributeToRecipientsWith1PayloadsAnd1Thumbnails(CallerSpec spec, HttpStatusCode expected)
    {
        var sender = await LoginAsOwner();
        var targetDrive = spec.TargetDrive;
        await UpdateBatchPeerScenario.CreateCollaborationDrive(sender, targetDrive);

        //
        // Setup - upload a new file with payloads
        //

        var recipients = await UpdateBatchPeerScenario.SetupRecipients(Host, sender, [Identities.Sam, Identities.Merry], targetDrive);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        uploadedFileMetadata.AllowDistribution = true;
        var transitOptions = new TransitOptions
        {
            IsTransient = false,
            Recipients = recipients.Select(r => (string)r.Identity).ToList(),
            RemoteTargetDrive = null,
            Priority = OutboxPriority.High
        };

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
