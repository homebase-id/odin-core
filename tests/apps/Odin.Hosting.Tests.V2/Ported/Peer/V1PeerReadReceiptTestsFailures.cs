using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/_Universal/Peer/ReadReceipt/PeerReadReceiptTestsFailures.cs
///
/// The refusal paths of the read-receipt round trip: the recipient lacks Write on the original
/// sender's drive, either side has severed the connection, and an identity trying to read-receipt its
/// own file. In every case the sender's transfer history must stay un-read.
/// </summary>
/// <remarks>
/// Port notes:
/// <list type="bullet">
///   <item><description>
///     The original's <c>TestCases()</c> had one live row (owner); its guest and app rows were already
///     commented out. Only the live row is carried, so <c>ReadReceiptCases</c> is a single-row matrix.
///   </description></item>
///   <item><description>
///     The three <c>[Ignore("how do i test this scenario?")]</c> placeholders are carried verbatim,
///     bodies included.
///   </description></item>
///   <item><description>
///     <see cref="PeerFlow.ConnectAsync"/> is not used here: this fixture's whole point is an
///     <em>asymmetric</em> grant (the sender grants the recipient Read while the recipient grants the
///     sender Write), and <c>ConnectAsync(bidirectional: true)</c> grants the same permission both
///     ways. The connect is therefore still hand-rolled, as in the original.
///   </description></item>
///   <item><description>
///     Every <c>WaitForEmptyOutbox</c> / <c>ProcessInbox</c> became <c>Sync.DrainOutboxAsync</c> /
///     <c>Sync.ProcessInboxAsync</c>; the V1 spellings poll the outbox background service, which this
///     host registers but never starts. Where the receipt is expected to be refused the drain returns
///     with the item still queued, which is what the original's polling call also ended up doing (it
///     gave up after its timeout).
///   </description></item>
///   <item><description>
///     <c>SetupCallerWithOwner</c> is not used: the drive has to exist on both identities and the two
///     circles have to be in place before the caller is built. Nothing from the original ran between
///     drive-create and caller-build.
///   </description></item>
/// </list>
/// </remarks>
[TestFixture]
public class V1PeerReadReceiptTestsFailures : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    public static IEnumerable<object[]> ReadReceiptCases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Secured()), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(ReadReceiptCases))]
    public async Task FailToSendReadReceiptWhenRecipientDoesNotHaveWriteAccessToOriginalSendersDrive(
        CallerSpec spec, HttpStatusCode expected)
    {
        const DrivePermission senderDrivePermissions = DrivePermission.Write;
        const DrivePermission recipientDrivePermissions = DrivePermission.Read;

        var (sender, recipient, targetDrive) =
            await PrepareScenarioAsync(spec, senderDrivePermissions, recipientDrivePermissions);

        var transitOptions = new TransitOptions
        {
            Recipients = [recipient.Identity]
        };

        var (uploadResult, recipientFile) =
            await AssertCanUploadEncryptedMetadataAsync(sender, recipient, targetDrive, transitOptions);

        await recipient.Sync.ProcessInboxAsync(uploadResult.File.TargetDrive);

        var driveClient = (await spec.Build(recipient)).V1.Drive;

        //
        // Send the read receipt
        //
        var fileForReadReceipt = new ExternalFileIdentifier
        {
            FileId = recipientFile.FileId,
            TargetDrive = recipientFile.TargetDrive
        };

        var sendReadReceiptResponse = await driveClient.SendReadReceipt([fileForReadReceipt]);
        await recipient.Sync.DrainOutboxAsync();

        Assert.That(sendReadReceiptResponse.StatusCode, Is.EqualTo(expected));
        var sendReadReceiptResult = sendReadReceiptResponse.Content;
        Assert.That(sendReadReceiptResult, Is.Not.Null);
        var item = sendReadReceiptResult.Results.SingleOrDefault(d => d.File == fileForReadReceipt);
        Assert.That(item, Is.Not.Null, "no record for file");
        var statusItem = item.Status.SingleOrDefault(i => i.Recipient == sender.Identity);
        Assert.That(statusItem, Is.Not.Null);
        Assert.That(statusItem.Status, Is.EqualTo(SendReadReceiptResultStatus.Enqueued));

        //TODO: there is no way to check the status of an item in the outbox; so the best
        //we can do is check if the target file is not updated

        //
        // Assert the read receipt was not updated on the sender's file
        //
        await sender.Sync.ProcessInboxAsync(targetDrive);

        AssertNotReadByRecipient(await GetHistoryAsync(sender, uploadResult), recipient, uploadResult);
    }

    [Test, TestCaseSource(nameof(ReadReceiptCases))]
    public async Task FailToSendReadReceiptWhenNotConnectedOnRecipientSide(CallerSpec spec, HttpStatusCode expected)
    {
        const DrivePermission senderDrivePermissions = DrivePermission.Write;
        const DrivePermission recipientDrivePermissions = DrivePermission.Read;

        var (frodo, sam, targetDrive) =
            await PrepareScenarioAsync(spec, senderDrivePermissions, recipientDrivePermissions);

        var transitOptions = new TransitOptions
        {
            Recipients = [sam.Identity]
        };

        var (uploadResult, recipientFile) =
            await AssertCanUploadEncryptedMetadataAsync(frodo, sam, targetDrive, transitOptions);

        await sam.Sync.ProcessInboxAsync(uploadResult.File.TargetDrive);

        var driveClient = (await spec.Build(sam)).V1.Drive;

        //
        // Send the read receipt
        //
        var fileForReadReceipt = new ExternalFileIdentifier
        {
            FileId = recipientFile.FileId,
            TargetDrive = recipientFile.TargetDrive
        };

        //
        // Severe the connection
        //
        await sam.Connections.DisconnectFrom(frodo.Identity);

        var sendReadReceiptResponse = await driveClient.SendReadReceipt([fileForReadReceipt]);

        Assert.That(sendReadReceiptResponse.StatusCode, Is.EqualTo(expected));
        var sendReadReceiptResult = sendReadReceiptResponse.Content;
        Assert.That(sendReadReceiptResult, Is.Not.Null);
        var item = sendReadReceiptResult.Results.SingleOrDefault(d => d.File == fileForReadReceipt);
        Assert.That(item, Is.Not.Null, "no record for file");
        var statusItem = item.Status.SingleOrDefault(i => i.Recipient == frodo.Identity);
        Assert.That(statusItem, Is.Not.Null);
        Assert.That(statusItem.Status, Is.EqualTo(SendReadReceiptResultStatus.NotConnectedToOriginalSender));

        //
        // Assert the read receipt was not updated on the sender's file
        //
        await frodo.Sync.ProcessInboxAsync(targetDrive);

        AssertNotReadByRecipient(await GetHistoryAsync(frodo, uploadResult), sam, uploadResult);
    }

    [Test, TestCaseSource(nameof(ReadReceiptCases))]
    public async Task FailToSendReadReceiptWhenNotConnectedOnSenderSide(CallerSpec spec, HttpStatusCode expected)
    {
        const DrivePermission senderDrivePermissions = DrivePermission.Write;
        const DrivePermission recipientDrivePermissions = DrivePermission.Read;

        var (sender, recipient, targetDrive) =
            await PrepareScenarioAsync(spec, senderDrivePermissions, recipientDrivePermissions);

        var transitOptions = new TransitOptions
        {
            Recipients = [recipient.Identity]
        };

        var (uploadResult, recipientFile) =
            await AssertCanUploadEncryptedMetadataAsync(sender, recipient, targetDrive, transitOptions);

        await recipient.Sync.ProcessInboxAsync(uploadResult.File.TargetDrive);

        var driveClient = (await spec.Build(recipient)).V1.Drive;

        //
        // Send the read receipt
        //
        var fileForReadReceipt = new ExternalFileIdentifier
        {
            FileId = recipientFile.FileId,
            TargetDrive = recipientFile.TargetDrive
        };

        //
        // Severe the connection
        //
        await sender.Connections.DisconnectFrom(recipient.Identity);

        var sendReadReceiptResponse = await driveClient.SendReadReceipt([fileForReadReceipt]);

        Assert.That(sendReadReceiptResponse.StatusCode, Is.EqualTo(expected));
        var sendReadReceiptResult = sendReadReceiptResponse.Content;
        Assert.That(sendReadReceiptResult, Is.Not.Null);
        var item = sendReadReceiptResult.Results.SingleOrDefault(d => d.File == fileForReadReceipt);
        Assert.That(item, Is.Not.Null, "no record for file");
        var statusItem = item.Status.SingleOrDefault(i => i.Recipient == sender.Identity);
        Assert.That(statusItem, Is.Not.Null);
        Assert.That(statusItem.Status, Is.EqualTo(SendReadReceiptResultStatus.Enqueued));

        //TODO: we cannot check if the original sender rejected the read-receipt because this
        //now in the outbox and there's no mechanism for that; therefore the best we can do is
        //validate the original sender file was not updated

        await recipient.Sync.DrainOutboxAsync();

        //
        // Assert the read receipt was not updated on the sender's file
        //
        await sender.Sync.ProcessInboxAsync(targetDrive);

        AssertNotReadByRecipient(await GetHistoryAsync(sender, uploadResult), recipient, uploadResult);
    }

    [Test, TestCaseSource(nameof(ReadReceiptCases))]
    [Ignore("how do i test this scenario?")]
    public Task SendReadReceiptWhenOriginalSenderIdentityIsNotResponding(CallerSpec spec, HttpStatusCode expected)
    {
        Assert.Inconclusive("");
        return Task.CompletedTask;
    }

    [Test, TestCaseSource(nameof(ReadReceiptCases))]
    [Ignore("how do i test this scenario?")]
    public Task SendReadReceiptWithInvalidGlobalTransitId(CallerSpec spec, HttpStatusCode expected)
    {
        Assert.Inconclusive("");
        return Task.CompletedTask;
    }

    [Test, TestCaseSource(nameof(ReadReceiptCases))]
    public async Task FailToSendReadReceiptToSendersFiles(CallerSpec spec, HttpStatusCode expected)
    {
        const DrivePermission senderDrivePermissions = DrivePermission.Write;
        const DrivePermission recipientDrivePermissions = DrivePermission.Read;

        var (sender, recipient, targetDrive) =
            await PrepareScenarioAsync(spec, senderDrivePermissions, recipientDrivePermissions);

        var transitOptions = new TransitOptions
        {
            Recipients = [recipient.Identity]
        };

        var (senderUploadResult, _) =
            await AssertCanUploadEncryptedMetadataAsync(sender, recipient, targetDrive, transitOptions);

        await recipient.Sync.ProcessInboxAsync(senderUploadResult.File.TargetDrive);

        // NOTE: unlike its siblings, this test's caller is the *sender* -- it read-receipts its own file.
        var driveClient = (await spec.Build(sender)).V1.Drive;

        //
        // Send the read receipt
        //
        var fileForReadReceipt = new ExternalFileIdentifier
        {
            FileId = senderUploadResult.File.FileId,
            TargetDrive = senderUploadResult.File.TargetDrive
        };

        var sendReadReceiptResponse = await driveClient.SendReadReceipt([fileForReadReceipt]);
        await sender.Sync.DrainOutboxAsync();

        Assert.That(sendReadReceiptResponse.StatusCode, Is.EqualTo(expected));
        var sendReadReceiptResult = sendReadReceiptResponse.Content;
        Assert.That(sendReadReceiptResult, Is.Not.Null);
        var item = sendReadReceiptResult.Results.SingleOrDefault(d => d.File == fileForReadReceipt);
        Assert.That(item, Is.Not.Null);
        Assert.That(item.Status.Single().Recipient, Is.Null);
        Assert.That(item.Status.Single().Status, Is.EqualTo(SendReadReceiptResultStatus.CannotSendReadReceiptToSelf));

        //
        // Assert the read receipt was not updated on the sender's file
        //
        await sender.Sync.ProcessInboxAsync(targetDrive);

        AssertNotReadByRecipient(await GetHistoryAsync(sender, senderUploadResult), recipient, senderUploadResult);
    }

    [Test, TestCaseSource(nameof(ReadReceiptCases))]
    [Ignore("how do i test this scenario?")]
    public Task SendReadReceiptWhenNeverHaveReceivedTheOriginalFile(CallerSpec spec, HttpStatusCode expected)
    {
        Assert.Inconclusive("");
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------------------------------------

    private static async Task<FileTransferHistoryResponse> GetHistoryAsync(OwnerSession sender, UploadResult uploadResult)
    {
        var getHistoryResponse = await sender.V1.Drive.GetTransferHistory(uploadResult.File);
        Assert.That(getHistoryResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var theHistory = getHistoryResponse.Content;
        Assert.That(theHistory, Is.Not.Null);
        return theHistory;
    }

    /// <summary>
    /// The shape every refusal in this fixture ends on: the file was delivered, but never marked read.
    /// </summary>
    private static void AssertNotReadByRecipient(
        FileTransferHistoryResponse theHistory, OwnerSession recipient, UploadResult uploadResult)
    {
        var recipientStatus = theHistory.GetHistoryItem(recipient.Identity);

        Assert.That(recipientStatus, Is.Not.Null, "There should be a status update for the recipient");
        Assert.That(recipientStatus.IsReadByRecipient, Is.False, "the file should not be marked as read");
        Assert.That(recipientStatus.LatestTransferStatus, Is.EqualTo(LatestTransferStatus.Delivered));
        Assert.That(recipientStatus.LatestSuccessfullyDeliveredVersionTag, Is.EqualTo(uploadResult.NewVersionTag));
    }

    private static async Task<(UploadResult uploadResult, SharedSecretEncryptedFileHeader recipientFile)>
        AssertCanUploadEncryptedMetadataAsync(
            OwnerSession sender,
            OwnerSession recipient,
            TargetDrive targetDrive,
            TransitOptions transitOptions)
    {
        const string uploadedContent = "pie";

        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            IsEncrypted = true,
            AppData = new()
            {
                Content = uploadedContent,
                FileType = default,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.Connected
        };

        var storageOptions = new StorageOptions
        {
            Drive = targetDrive
        };

        var (uploadResponse, _) = await sender.V1.Drive.UploadNewEncryptedMetadata(
            fileMetadata,
            storageOptions,
            transitOptions);

        Assert.That(uploadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var senderUploadResult = uploadResponse.Content;
        Assert.That(senderUploadResult, Is.Not.Null);
        Assert.That(senderUploadResult.RecipientStatus.Count, Is.EqualTo(1));
        Assert.That(senderUploadResult.RecipientStatus[transitOptions.Recipients.Single()],
            Is.EqualTo(TransferStatus.Enqueued));

        await sender.Sync.DrainOutboxAsync();

        // validate recipient got the file
        await recipient.Sync.ProcessInboxAsync(senderUploadResult.File.TargetDrive);

        var recipientFileResponse = await recipient.V1.Drive.QueryByGlobalTransitId(senderUploadResult.GlobalTransitIdFileIdentifier);
        Assert.That(recipientFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var file = recipientFileResponse.Content.SearchResults.SingleOrDefault();
        Assert.That(file, Is.Not.Null);

        return (senderUploadResult, file);
    }

    /// <summary>
    /// The original's <c>PrepareScenario</c>, kept hand-rolled because the two grants differ: the
    /// sender's circle gives the recipient <paramref name="drivePermissionsGrantedToRecipient"/> on the
    /// sender's drive, and the recipient's circle gives the sender
    /// <paramref name="drivePermissionsGrantedToSender"/> on the recipient's. The connection-info
    /// assertion the original ended on is setup validation, so it is dropped.
    /// </summary>
    private async Task<(OwnerSession Sender, OwnerSession Recipient, TargetDrive Drive)> PrepareScenarioAsync(
        CallerSpec spec,
        DrivePermission drivePermissionsGrantedToSender,
        DrivePermission drivePermissionsGrantedToRecipient)
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var targetDrive = spec.TargetDrive;
        await recipient.Admin.CreateDrive(targetDrive, "Target drive on recipient", allowAnonymousReads: false);
        await sender.Admin.CreateDrive(targetDrive, "Target drive on sender", allowAnonymousReads: false);

        var senderCircleId = Guid.NewGuid();
        await sender.Admin.CreateCircle(senderCircleId,
            "Circle with drive access for the recipient to send back a read-receipt",
            DriveGrant(targetDrive, drivePermissionsGrantedToRecipient));

        var recipientCircleId = Guid.NewGuid();
        await recipient.Admin.CreateCircle(recipientCircleId, "Circle with drive access",
            DriveGrant(targetDrive, drivePermissionsGrantedToSender));

        //
        // Sender sends connection request
        //
        await sender.Connections.SendConnectionRequest(recipient.Identity, [senderCircleId]);

        //
        // Recipient accepts; grants access to circle
        //
        await recipient.Connections.AcceptConnectionRequest(sender.Identity, [recipientCircleId]);

        return (sender, recipient, targetDrive);
    }

    private static PermissionSetGrantRequest DriveGrant(TargetDrive drive, DrivePermission permission) =>
        new()
        {
            Drives =
            [
                new DriveGrantRequest
                {
                    PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = permission }
                }
            ],
            PermissionSet = new PermissionSet(new List<int>())
        };
}
