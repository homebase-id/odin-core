using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.AppNotifications.Data;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Port of <c>_Universal/Peer/UpdateBatch/UpdateBatchWithRecipientsRemoteUpsertEncryptedTests</c>
/// (class <c>UpdateBatchWithRecipientsRemoteUpsertEncrypted</c>). The encrypted twin of
/// <see cref="UpdateBatchWithRecipientsRemoteUpsertTests"/>: the update carries a new key header and
/// an app notification, and each recipient must both decrypt the distributed content with its own
/// shared secret and end up holding the notification.
/// </summary>
/// <remarks>
/// See <see cref="UpdateBatchWithRecipientsTests"/> for the framework departures shared by all three
/// of these ports; the recipient arrange and the refusal act live in
/// <see cref="UpdateBatchPeerScenario"/>.
///
/// The original stacked only <c>OwnerAllowed</c> + <c>AppAllowed</c> on the first test and all four
/// sources on the other two, so this fixture keeps two matrices rather than one. The app row here is
/// <c>AppReadWriteAccessToDrive</c>, hence <see cref="DrivePermission.ReadWrite"/> and not Write.
///
/// The original's first two tests were character-identical apart from which recipients were given
/// the file on upload, so they share <see cref="RunRemoteUpsertEncrypted"/>: the first names no
/// recipient on the upload (nobody holds the file), the second names half of them.
///
/// <b>Carried defects</b>, left alone because a port is a move:
/// <list type="bullet">
/// <item>In <see cref="CanUpdateBatchAndDistributeToRecipientsWith1PayloadsAnd1ThumbnailsWhenTargetFileDoesNotExistOnRemoteServer_Encrypted"/>
/// the two payload assertions inside the per-recipient loop read the <i>sender's</i> local header,
/// already asserted just above, instead of the recipient's. The recipient's payload set is therefore
/// never checked; the assertions restate the local claim once per recipient. That is why
/// <see cref="AssertEncryptedUpdateLandedEverywhere"/>'s per-recipient hook is handed both headers.</item>
/// <item>The same test's <c>PreviewThumbnail = default</c> / empty <c>Thumbnails</c> on the added
/// payload descriptor mean the "1Thumbnails" half of its name applies only to the payload being
/// deleted.</item>
/// </list>
/// </remarks>
[TestFixture]
public class UpdateBatchWithRecipientsRemoteUpsertEncryptedTests : V2Fixture
{
    protected override string[] HostIdentities =>
        [Identities.Frodo, Identities.Sam, Identities.Merry, Identities.Pippin, Identities.TomBombadil];

    private const string OriginalUploadedContent = "some content here..";
    private const string UpdatedContentToBeDistributed = "some new content to be distributed";

    /// <summary>The original's <c>OwnerAllowed</c> + <c>AppAllowed</c> pair — no guest rows.</summary>
    public static IEnumerable<object[]> OwnerAndAppCases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Collab()), HttpStatusCode.OK];
        yield return
            [CallerSpec.App(DriveSpec.Collab(), DrivePermission.ReadWrite, [PermissionKeys.UseTransitWrite]), HttpStatusCode.OK];
    }

    /// <summary>As <see cref="OwnerAndAppCases"/>, plus the two guest rows the original stacked on.</summary>
    public static IEnumerable<object[]> UpdateBatchCases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Collab()), HttpStatusCode.OK];
        yield return
            [CallerSpec.App(DriveSpec.Collab(), DrivePermission.ReadWrite, [PermissionKeys.UseTransitWrite]), HttpStatusCode.OK];
        yield return [CallerSpec.Guest(DriveSpec.Collab(), DrivePermission.Write), HttpStatusCode.Forbidden];
        yield return [CallerSpec.Guest(DriveSpec.Collab(), DrivePermission.Read), HttpStatusCode.Forbidden];
    }

    [Test, TestCaseSource(nameof(OwnerAndAppCases))]
    public Task CanUpdateBatchAndDistributeToRecipientsWhenTargetFileDoesNotExistOnRemoteServer_Encrypted_WithAppNotifications(
        CallerSpec spec, HttpStatusCode expected) =>
        RunRemoteUpsertEncrypted(spec, expected,
            identitiesWithFile: [],
            identitiesWithoutFile: [Identities.Sam, Identities.Merry]);

    [Test, TestCaseSource(nameof(UpdateBatchCases))]
    public Task
        CanUpdateBatchAndDistributeToRecipientsWhenTargetFileDoesNotExistOnRemoteServer_SomeRecipientsHaveFile_SomeDoNotHaveFile_Encrypted(
            CallerSpec spec, HttpStatusCode expected) =>
        RunRemoteUpsertEncrypted(spec, expected,
            identitiesWithFile: [Identities.Sam, Identities.Merry],
            identitiesWithoutFile: [Identities.Pippin, Identities.TomBombadil]);

    /// <summary>
    /// Seed an encrypted, header-only file that only <paramref name="identitiesWithFile"/> receive,
    /// then update it with a new key header plus an app notification addressed to everyone, and
    /// require every recipient — those who had the file and those upserting it from the update
    /// alone — to end up with the new content, decryptable under their own shared secret.
    /// </summary>
    private async Task RunRemoteUpsertEncrypted(
        CallerSpec spec,
        HttpStatusCode expected,
        string[] identitiesWithFile,
        string[] identitiesWithoutFile)
    {
        var (caller, sender) = await SetupCallerWithOwner(spec);
        var targetDrive = spec.TargetDrive;

        if (expected != HttpStatusCode.OK)
        {
            await UpdateBatchPeerScenario.AssertUpdateRefused(
                caller, sender, targetDrive, [..identitiesWithFile, ..identitiesWithoutFile], expected);
            return;
        }

        //
        // Setup - upload a new encrypted file, addressed only to the recipients who should hold it
        //

        var recipientsWithFile = await UpdateBatchPeerScenario.SetupRecipients(
            Host, sender, identitiesWithFile, targetDrive);
        var recipientsWithoutFile = await UpdateBatchPeerScenario.SetupRecipients(
            Host, sender, identitiesWithoutFile, targetDrive);
        var allRecipients = recipientsWithFile.Concat(recipientsWithoutFile).ToList();

        var keyHeader = KeyHeader.NewRandom16();

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        uploadedFileMetadata.AllowDistribution = true;
        uploadedFileMetadata.AppData.Content = OriginalUploadedContent;

        // Only the recipients meant to already hold the file are addressed on the initial upload.
        // Where that set is empty the upload carries no recipients at all, so the file exists
        // nowhere but on the sender and every recipient has to upsert it from the update.
        var transitOptions = new TransitOptions
        {
            Recipients = recipientsWithFile.Count == 0
                ? null
                : recipientsWithFile.Select(x => (string)x.Identity).ToList()
        };

        var storageOptions = new StorageOptions()
        {
            Drive = targetDrive,
        };

        var (uploadNewFileResponse, encryptedJsonContent64) = await sender.V1.Drive.UploadNewEncryptedMetadata(
            uploadedFileMetadata,
            storageOptions,
            transitOptions,
            keyHeader);

        Assert.That(uploadNewFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        if (recipientsWithFile.Count > 0)
        {
            await PeerFlow.DistributeAsync(sender, allRecipients, targetDrive);
        }

        var uploadResult = uploadNewFileResponse.Content;
        var targetFile = uploadResult!.File;

        // verify setup

        await AssertSeedHeader(sender, targetFile, encryptedJsonContent64,
            uploadedFileMetadata.AppData.DataType, uploadResult.NewVersionTag, OriginalUploadedContent);

        //
        // Act - call update batch with UpdateLocale = Local
        //

        // change around some data
        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = UpdatedContentToBeDistributed;
        updatedFileMetadata.AppData.DataType = 991;
        updatedFileMetadata.VersionTag = uploadResult.NewVersionTag;

        var updateInstructionSet = BuildUpdateInstructionSet(
            targetFile, allRecipients.Select(x => x.Identity).ToList(), []);

        keyHeader.Iv = ByteArrayUtil.GetRndByteArray(16);

        var (updateFileResponse, updatedEncryptedContent64, _, _) =
            await caller.V1.Drive.UpdateEncryptedFile(updateInstructionSet, updatedFileMetadata, [], keyHeader);
        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(expected));
        Assert.That(updateFileResponse.Content, Is.Not.Null);

        await AssertEncryptedUpdateLandedEverywhere(
            sender, allRecipients, targetDrive, targetFile, uploadResult.GlobalTransitIdFileIdentifier,
            keyHeader, updatedEncryptedContent64, UpdatedContentToBeDistributed,
            updatedFileMetadata.AppData.DataType, updateFileResponse.Content!.NewVersionTag, updateInstructionSet);
    }

    [Test, TestCaseSource(nameof(UpdateBatchCases))]
    public async Task CanUpdateBatchAndDistributeToRecipientsWith1PayloadsAnd1ThumbnailsWhenTargetFileDoesNotExistOnRemoteServer_Encrypted(
        CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, sender) = await SetupCallerWithOwner(spec);
        var targetDrive = spec.TargetDrive;

        if (expected != HttpStatusCode.OK)
        {
            await UpdateBatchPeerScenario.AssertUpdateRefused(
                caller, sender, targetDrive, [Identities.Sam, Identities.Merry], expected);
            return;
        }

        //
        // Setup - upload a new encrypted file with one payload; no recipient holds it yet
        //

        var recipients = await UpdateBatchPeerScenario.SetupRecipients(
            Host, sender, [Identities.Sam, Identities.Merry], targetDrive);

        var keyHeader = KeyHeader.NewRandom16();

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        uploadedFileMetadata.AllowDistribution = true;
        uploadedFileMetadata.AppData.Content = OriginalUploadedContent;

        var payloadToBeDeleted = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        payloadToBeDeleted.Iv = ByteArrayUtil.GetRndByteArray(16);
        List<TestPayloadDefinition> testPayloads = [payloadToBeDeleted];

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        // Note: the upload names no recipients, so the file does not exist on any remote server.
        var (uploadNewFileResponse, encryptedJsonContent64, _, _) = await sender.V1.Drive.UploadNewEncryptedFile(
            targetDrive,
            keyHeader,
            uploadedFileMetadata,
            uploadManifest,
            testPayloads);

        Assert.That(uploadNewFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var uploadResult = uploadNewFileResponse.Content;
        var targetFile = uploadResult!.File;

        // verify setup

        await AssertSeedHeader(sender, targetFile, encryptedJsonContent64,
            uploadedFileMetadata.AppData.DataType, uploadResult.NewVersionTag, OriginalUploadedContent,
            assertPayloads: h =>
            {
                Assert.That(h.FileMetadata.Payloads.Count(), Is.EqualTo(testPayloads.Count));
                Assert.That(h.FileMetadata.Payloads.Select(pd => pd.Key), Does.Contain(payloadToBeDeleted.Key),
                    "payloadToBeDeleted should be in the initial file upload:)");
            });

        //
        // Act - call update batch with UpdateLocale = Local
        //

        // change around some data
        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = UpdatedContentToBeDistributed;
        updatedFileMetadata.AppData.DataType = 991;
        updatedFileMetadata.VersionTag = uploadResult.NewVersionTag;

        var payloadToAdd = SamplePayloadDefinitions.GetPayloadDefinition1();
        var updateInstructionSet = BuildUpdateInstructionSet(
            targetFile,
            recipients.Select(x => x.Identity).ToList(),
            [
                new UploadManifestPayloadDescriptor
                {
                    PayloadUpdateOperationType = PayloadUpdateOperationType.AppendOrOverwrite,
                    Iv = ByteArrayUtil.GetRndByteArray(16),
                    PayloadKey = payloadToAdd.Key,
                    DescriptorContent = null,
                    ContentType = payloadToAdd.ContentType,
                    PreviewThumbnail = default,
                    Thumbnails = new List<UploadedManifestThumbnailDescriptor>(),
                },
                new UploadManifestPayloadDescriptor()
                {
                    PayloadUpdateOperationType = PayloadUpdateOperationType.DeletePayload,
                    PayloadKey = payloadToBeDeleted.Key
                }
            ]);

        keyHeader.Iv = ByteArrayUtil.GetRndByteArray(16);

        var (updateFileResponse, updatedEncryptedContent64, _, _) =
            await caller.V1.Drive.UpdateEncryptedFile(updateInstructionSet, updatedFileMetadata, [payloadToAdd], keyHeader);
        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(expected));
        Assert.That(updateFileResponse.Content, Is.Not.Null);

        await AssertEncryptedUpdateLandedEverywhere(
            sender, recipients, targetDrive, targetFile, uploadResult.GlobalTransitIdFileIdentifier,
            keyHeader, updatedEncryptedContent64, UpdatedContentToBeDistributed,
            updatedFileMetadata.AppData.DataType, updateFileResponse.Content!.NewVersionTag, updateInstructionSet,
            assertSenderPayloads: AssertPayloadSwap,
            // NOTE: the original asserts on the *sender's* local header inside the recipient loop,
            // not on the recipient's copy — carried verbatim; see the class remarks.
            assertRecipientPayloads: (localHeader, _) => AssertPayloadSwap(localHeader));

        return;

        void AssertPayloadSwap(SharedSecretEncryptedFileHeader h)
        {
            Assert.That(h.FileMetadata.Payloads.Select(pd => pd.Key), Does.Contain(payloadToAdd.Key),
                "payloadToAdd should have been, well, added :)");
            Assert.That(h.FileMetadata.Payloads.Select(pd => pd.Key), Does.Not.Contain(payloadToBeDeleted.Key),
                "payload 1 should have been removed:)");
        }
    }

    /// <summary>
    /// The instruction set every test here sends: a local update carrying an app notification
    /// addressed to the same recipients as the update itself.
    /// </summary>
    private static FileUpdateInstructionSet BuildUpdateInstructionSet(
        ExternalFileIdentifier targetFile,
        List<OdinId> recipients,
        List<UploadManifestPayloadDescriptor> payloadDescriptors) =>
        new()
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = targetFile.ToFileIdentifier(),
            Recipients = recipients,
            Manifest = new UploadManifest
            {
                PayloadDescriptors = payloadDescriptors
            },
            UseAppNotification = true,
            AppNotificationOptions = new()
            {
                AppId = Guid.NewGuid(),
                TypeId = Guid.NewGuid(),
                TagId = Guid.NewGuid(),
                Silent = false,
                PeerSubscriptionId = Guid.NewGuid(),
                Recipients = recipients,
                UnEncryptedMessage = "test message here"
            }
        };

    /// <summary>
    /// The seeded file is on the sender, encrypted, and decrypts to what was uploaded.
    /// </summary>
    /// <param name="assertPayloads">Defaults to "no payloads"; the payload case passes its own.</param>
    private static async Task AssertSeedHeader(
        OwnerSession sender,
        ExternalFileIdentifier targetFile,
        string encryptedJsonContent64,
        int expectedDataType,
        Guid expectedVersionTag,
        string expectedPlaintext,
        Action<SharedSecretEncryptedFileHeader> assertPayloads = null)
    {
        var getHeaderToVerifyResponse = await sender.V1.Drive.GetFileHeader(targetFile);
        Assert.That(getHeaderToVerifyResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var headerToVerify = getHeaderToVerifyResponse.Content;
        Assert.That(headerToVerify, Is.Not.Null);
        Assert.That(headerToVerify!.FileMetadata.IsEncrypted, Is.True);
        Assert.That(headerToVerify.FileMetadata.AppData.Content, Is.EqualTo(encryptedJsonContent64));
        Assert.That(headerToVerify.FileMetadata.AppData.DataType, Is.EqualTo(expectedDataType));
        Assert.That(headerToVerify.FileMetadata.VersionTag, Is.EqualTo(expectedVersionTag));
        (assertPayloads ?? (h => Assert.That(h.FileMetadata.Payloads, Is.Empty)))(headerToVerify);

        var localSS = sender.SharedSecret;
        var localKeyHeaderToVerify = headerToVerify.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref localSS);
        var localDecryptedBytes = localKeyHeaderToVerify.Decrypt(headerToVerify.FileMetadata.AppData.Content.FromBase64());
        Assert.That(localDecryptedBytes.ToStringFromUtf8Bytes(), Is.EqualTo(expectedPlaintext));
    }

    /// <summary>
    /// Delivers the update, then asserts the sender's local copy carries it and that every recipient
    /// holds the file, can decrypt it under its own shared secret with the update's key header, and
    /// got the app notification.
    /// </summary>
    /// <param name="assertSenderPayloads">Defaults to "no payloads left on the sender".</param>
    /// <param name="assertRecipientPayloads">
    /// Called per recipient with (sender's local header, recipient's header). Defaults to "no
    /// payloads left on the recipient"; the payload case passes the original's variant, which reads
    /// the local header — see the class remarks.
    /// </param>
    private static async Task AssertEncryptedUpdateLandedEverywhere(
        OwnerSession sender,
        IReadOnlyList<OwnerSession> recipients,
        TargetDrive drive,
        ExternalFileIdentifier targetFile,
        GlobalTransitIdFileIdentifier targetGlobalTransitIdFileIdentifier,
        KeyHeader keyHeader,
        string updatedEncryptedContent64,
        string updatedPlaintext,
        int expectedDataType,
        Guid expectedVersionTag,
        FileUpdateInstructionSet updateInstructionSet,
        Action<SharedSecretEncryptedFileHeader> assertSenderPayloads = null,
        Action<SharedSecretEncryptedFileHeader, SharedSecretEncryptedFileHeader> assertRecipientPayloads = null)
    {
        assertSenderPayloads ??= h => Assert.That(h.FileMetadata.Payloads, Is.Empty);
        assertRecipientPayloads ??= (_, remote) => Assert.That(remote.FileMetadata.Payloads, Is.Empty);

        await PeerFlow.DistributeAsync(sender, recipients, drive);

        //
        // ensure the local file exists and is updated correctly
        //
        var getHeaderResponse = await sender.V1.Drive.GetFileHeader(targetFile);
        Assert.That(getHeaderResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var header = getHeaderResponse.Content;
        Assert.That(header, Is.Not.Null);
        Assert.That(header!.FileMetadata.IsEncrypted, Is.True);
        Assert.That(header.FileMetadata.AppData.Content, Is.EqualTo(updatedEncryptedContent64));
        Assert.That(header.FileMetadata.AppData.DataType, Is.EqualTo(expectedDataType));
        Assert.That(header.FileMetadata.VersionTag, Is.EqualTo(expectedVersionTag));
        assertSenderPayloads(header);

        await DriveAsserts.AssertFileFoundByDataType(
            sender.V1.Drive, targetFile.TargetDrive, expectedDataType, targetFile.FileId);

        // ensure the recipients get the file

        foreach (var recipient in recipients)
        {
            var recipientFileResponse = await recipient.V1.Drive.QueryByGlobalTransitId(targetGlobalTransitIdFileIdentifier);
            var remoteFileHeader = recipientFileResponse.Content!.SearchResults.FirstOrDefault();

            Assert.That(remoteFileHeader, Is.Not.Null, $"recipient {recipient.Identity} should have the file");
            Assert.That(remoteFileHeader!.FileMetadata.IsEncrypted, Is.True);
            Assert.That(remoteFileHeader.FileMetadata.AppData.Content, Is.EqualTo(updatedEncryptedContent64)); //latest update
            Assert.That(remoteFileHeader.FileMetadata.AppData.DataType, Is.EqualTo(expectedDataType));
            Assert.That(remoteFileHeader.FileMetadata.VersionTag, Is.EqualTo(expectedVersionTag));
            assertRecipientPayloads(header, remoteFileHeader);

            // validate we can decrypt it on the recipient

            var sharedSecret = recipient.SharedSecret;
            var remoteKeyHeader = remoteFileHeader.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);
            Assert.That(remoteKeyHeader.Iv, Is.EqualTo(keyHeader.Iv));
            Assert.That(remoteKeyHeader.AesKey.GetKey(), Is.EqualTo(keyHeader.AesKey.GetKey()));
            var decryptedBytes = remoteKeyHeader.Decrypt(remoteFileHeader.FileMetadata.AppData.Content.FromBase64());
            Assert.That(decryptedBytes.ToStringFromUtf8Bytes(), Is.EqualTo(updatedPlaintext));

            // valid recipient got the notification
            await AssertRecipientGotNotification(recipient, sender, updateInstructionSet);
        }
    }

    /// <summary>
    /// The recipient holds exactly one app notification matching the options the update carried.
    /// </summary>
    private static async Task AssertRecipientGotNotification(
        OwnerSession recipient, OwnerSession sender, FileUpdateInstructionSet updateInstructionSet)
    {
        var allNotificationsResponse = await recipient.V1.Notifications.GetList(100);
        Assert.That(allNotificationsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var notifications = allNotificationsResponse.Content;
        Assert.That(notifications, Is.Not.Null);

        var options = updateInstructionSet.AppNotificationOptions;
        Assert.That(notifications!.Results, Has.Exactly(1).Matches<AppNotification>(
            n => n.SenderId == sender.Identity
                 && n.Options.TypeId == options.TypeId
                 && n.Options.PeerSubscriptionId == options.PeerSubscriptionId
                 && n.Options.UnEncryptedMessage == options.UnEncryptedMessage
                 && n.Options.TagId == options.TagId));
    }
}
