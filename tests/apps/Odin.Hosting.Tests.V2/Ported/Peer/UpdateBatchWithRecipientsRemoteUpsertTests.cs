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
/// three of these ports (collaboration drive, <c>WaitForEmptyOutbox</c> to
/// <see cref="PeerFlow.DistributeAsync(OwnerSession, IEnumerable{OwnerSession}, TargetDrive)"/>,
/// dropped connection cleanup, refused rows stopping before the peer arrange); the arrange and the assert
/// sweep are shared through <see cref="UpdateBatchPeerScenario"/>.
///
/// Where the upload carries no recipients there is nothing in the outbox, so — unlike the original,
/// which polled the outbox unconditionally — no distribute call follows the seed. The only seed
/// distribute left is the mixed case's, which really does have recipients to deliver to.
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

    private static readonly string[] RecipientIdentities = [Identities.Sam, Identities.Merry];

    private static readonly string[] MixedRecipientIdentities =
        [Identities.Sam, Identities.TomBombadil, Identities.Merry, Identities.Pippin];

    /// <summary>The original's four stacked case sources, inline.</summary>
    public static IEnumerable<object[]> UpdateBatchCases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Collab()), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Collab(), DrivePermission.Write, [PermissionKeys.UseTransitWrite]), HttpStatusCode.OK];
        yield return [CallerSpec.Guest(DriveSpec.Collab(), DrivePermission.Write), HttpStatusCode.Forbidden];
        yield return [CallerSpec.Guest(DriveSpec.Collab(), DrivePermission.Read), HttpStatusCode.Forbidden];
    }

    [Test, TestCaseSource(nameof(UpdateBatchCases))]
    public async Task CanUpdateBatchAndDistributeToRecipientsWhenTargetFileDoesNotExistOnRemoteServer(
        CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, sender) = await SetupCallerWithOwner(spec);
        var targetDrive = spec.TargetDrive;

        if (expected != HttpStatusCode.OK)
        {
            await UpdateBatchPeerScenario.AssertUpdateRefused(caller, sender, targetDrive, RecipientIdentities, expected);
            return;
        }

        //
        // Setup - upload a new file; no recipient holds it yet
        //

        var recipients = await UpdateBatchPeerScenario.SetupRecipients(Host, sender, RecipientIdentities, targetDrive);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Authenticated);
        uploadedFileMetadata.AllowDistribution = true;

        // Note: no transit options on initial upload to ensure
        // the file does not exist on the remote server
        var transitOptions = new TransitOptions();

        var uploadNewFileResponse = await sender.V1.Drive.UploadNewMetadata(targetDrive, uploadedFileMetadata, transitOptions);
        Assert.That(uploadNewFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var uploadResult = uploadNewFileResponse.Content;
        var targetFile = uploadResult!.File;

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

        var updateFileResponse = await caller.V1.Drive.UpdateFile(updateInstructionSet, updatedFileMetadata, []);
        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(expected));
        Assert.That(updateFileResponse.Content, Is.Not.Null);

        await UpdateBatchPeerScenario.AssertUpdateLandedEverywhere(
            sender, recipients, targetDrive, targetFile, uploadResult.GlobalTransitIdFileIdentifier,
            updatedFileMetadata, updateFileResponse.Content!.NewVersionTag);
    }

    [Test, TestCaseSource(nameof(UpdateBatchCases))]
    public async Task CanUpdateBatchAndDistributeToRecipientsWhenTargetFileDoesNotExistOnRemoteServerMixed(
        CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, sender) = await SetupCallerWithOwner(spec);
        var targetDrive = spec.TargetDrive;

        if (expected != HttpStatusCode.OK)
        {
            await UpdateBatchPeerScenario.AssertUpdateRefused(caller, sender, targetDrive, MixedRecipientIdentities, expected);
            return;
        }

        //
        // Setup - upload a new file; only half the recipients are given it up front
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
        await PeerFlow.DistributeAsync(sender, allRecipients, targetDrive);

        var uploadResult = uploadNewFileResponse.Content;
        var targetFile = uploadResult!.File;

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

        var updateFileResponse = await caller.V1.Drive.UpdateFile(updateInstructionSet, updatedFileMetadata, []);
        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(expected));
        Assert.That(updateFileResponse.Content, Is.Not.Null);

        await UpdateBatchPeerScenario.AssertUpdateLandedEverywhere(
            sender, allRecipients, targetDrive, targetFile, uploadResult.GlobalTransitIdFileIdentifier,
            updatedFileMetadata, updateFileResponse.Content!.NewVersionTag);
    }

    [Test, TestCaseSource(nameof(UpdateBatchCases))]
    public async Task CanUpdateBatchAndDistributeToRecipientsWith1PayloadsAnd1ThumbnailsWhenTargetFileDoesNotExistOnRemoteServer(
        CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, sender) = await SetupCallerWithOwner(spec);
        var targetDrive = spec.TargetDrive;

        if (expected != HttpStatusCode.OK)
        {
            await UpdateBatchPeerScenario.AssertUpdateRefused(caller, sender, targetDrive, RecipientIdentities, expected);
            return;
        }

        //
        // Setup - upload a new file with payloads; no recipient holds it yet
        //

        var recipients = await UpdateBatchPeerScenario.SetupRecipients(Host, sender, RecipientIdentities, targetDrive);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Authenticated);
        uploadedFileMetadata.AllowDistribution = true;

        // Note: no transit options on initial upload to ensure
        // the file does not exist on the remote server
        var transitOptions = new TransitOptions();

        var payloadToBeDeleted = SamplePayloadDefinitions.GetPayloadDefinition1();
        List<TestPayloadDefinition> testPayloads = [payloadToBeDeleted];

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var uploadNewFileResponse =
            await sender.V1.Drive.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, testPayloads, transitOptions);
        Assert.That(uploadNewFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var uploadResult = uploadNewFileResponse.Content;
        var targetFile = uploadResult!.File;

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
                        PayloadKey = payloadToBeDeleted.Key,
                    }
                ]
            }
        };

        var updateFileResponse = await caller.V1.Drive.UpdateFile(updateInstructionSet, updatedFileMetadata, []);
        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(expected));
        Assert.That(updateFileResponse.Content, Is.Not.Null);

        await UpdateBatchPeerScenario.AssertUpdateLandedEverywhere(
            sender, recipients, targetDrive, targetFile, uploadResult.GlobalTransitIdFileIdentifier,
            updatedFileMetadata, updateFileResponse.Content!.NewVersionTag,
            deletedPayloadKey: payloadToBeDeleted.Key);
    }
}
