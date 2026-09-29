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
/// <see cref="UpdateBatchPeerScenario.SetupRecipients"/> here, over
/// <see cref="PeerFlow.CreatePeerDriveAsync"/>. The drive is a <see cref="DriveSpec.Collab"/> one:
/// on a collaboration drive <c>PeerFileUpdateWriter</c> keeps the sender's ACL instead of narrowing
/// the recipient's copy to owner-only, so the flag is load-bearing for this suite.</item>
/// <item><c>WaitForEmptyOutbox</c> is a passive poll of a background service the fast host never
/// starts; every call becomes <see cref="PeerFlow.DistributeAsync(OwnerSession, IEnumerable{OwnerSession}, TargetDrive)"/>
/// — drain the sender's outbox, then process each recipient's inbox.</item>
/// <item>The original's <c>DisableAutoAcceptIntroductions</c> calls and trailing <c>Disconnect</c>
/// are gone: both exist to stop state leaking between tests on the shared WebScaffold, which
/// <c>V2Fixture</c>'s per-test reset already guarantees.</item>
/// <item>The caller is built by <c>SetupCallerWithOwner</c>, i.e. before the seed rather than after
/// it as in the original. Checked and inert: the seed is uploaded by the owner session, never by the
/// caller, and the sibling port <c>V1LocalUpdateBatchTests</c> drives the same endpoint and matrix
/// the same way.</item>
/// <item>The rows the matrix expects to be refused stop at
/// <see cref="UpdateBatchPeerScenario.AssertUpdateRefused"/>, which seeds one local file and sends
/// the update at it. Those rows never reach the peer half of the flow, so running the recipient
/// arrange for them — four logins, four drives and two connection handshakes apiece — bought
/// nothing; see that method's remarks for why the seed file itself has to stay.</item>
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

    private static readonly string[] RecipientIdentities = [Identities.Sam, Identities.Merry];

    /// <summary>
    /// The original's four stacked case sources, inline. Both allowed rows carry
    /// <see cref="PermissionKeys.UseTransitWrite"/>, since the update fans out over peer.
    /// </summary>
    public static IEnumerable<object[]> UpdateBatchCases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Collab()), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Collab(), DrivePermission.Write, [PermissionKeys.UseTransitWrite]), HttpStatusCode.OK];
        yield return [CallerSpec.Guest(DriveSpec.Collab(), DrivePermission.Write), HttpStatusCode.Forbidden];
        yield return [CallerSpec.Guest(DriveSpec.Collab(), DrivePermission.Read), HttpStatusCode.Forbidden];
    }

    [Test, TestCaseSource(nameof(UpdateBatchCases))]
    public async Task CanUpdateBatchAndDistributeToRecipients(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, sender) = await SetupCallerWithOwner(spec);
        var targetDrive = spec.TargetDrive;

        if (expected != HttpStatusCode.OK)
        {
            await UpdateBatchPeerScenario.AssertUpdateRefused(caller, sender, targetDrive, RecipientIdentities, expected);
            return;
        }

        //
        // Setup - upload a new file and distribute it to the recipients
        //

        var recipients = await UpdateBatchPeerScenario.SetupRecipients(Host, sender, RecipientIdentities, targetDrive);

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
        Assert.That(uploadNewFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await PeerFlow.DistributeAsync(sender, recipients, targetDrive);

        var uploadResult = uploadNewFileResponse.Content;
        var targetFile = uploadResult!.File;

        //
        // Act - call update batch with UpdateLocale = Local
        //

        // change around some data
        var updatedFileMetadata = uploadedFileMetadata;
        // The original assigned this to uploadedFileMetadata on the line after the upload; it is the
        // same object, so it never affected the upload and only ever went out with the update.
        updatedFileMetadata.AccessControlList = AccessControlList.Authenticated;
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
    public async Task CanUpdateBatchAndDistributeToRecipientsWith1PayloadsAnd1Thumbnails(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, sender) = await SetupCallerWithOwner(spec);
        var targetDrive = spec.TargetDrive;

        if (expected != HttpStatusCode.OK)
        {
            await UpdateBatchPeerScenario.AssertUpdateRefused(caller, sender, targetDrive, RecipientIdentities, expected);
            return;
        }

        //
        // Setup - upload a new file with payloads and distribute it to the recipients
        //

        var recipients = await UpdateBatchPeerScenario.SetupRecipients(Host, sender, RecipientIdentities, targetDrive);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        uploadedFileMetadata.AllowDistribution = true;
        var transitOptions = new TransitOptions
        {
            IsTransient = false,
            Recipients = recipients.Select(r => (string)r.Identity).ToList(),
            RemoteTargetDrive = null,
            Priority = OutboxPriority.High
        };

        var payloadToBeDeleted = SamplePayloadDefinitions.GetPayloadDefinition1();
        List<TestPayloadDefinition> testPayloads = [payloadToBeDeleted];

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var uploadNewFileResponse =
            await sender.V1.Drive.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, testPayloads, transitOptions);
        Assert.That(uploadNewFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await PeerFlow.DistributeAsync(sender, recipients, targetDrive);

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
