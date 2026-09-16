using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Time;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/_Universal/Peer/ReadReceipt/PeerReadReceiptTestsSuccess.cs
///
/// The happy paths of the read-receipt round trip: Frodo sends Sam an encrypted file, Sam marks it
/// read, and the receipt travels back onto Frodo's transfer history. Also covers the local
/// <c>LocalAppData.ReadTime</c> semantics <c>SendReadReceipt</c> owns — an explicit timestamp is
/// honoured, a future one is clamped, an older one never downgrades the stored value, a call with no
/// timestamp never overwrites one already there, and local tag / content edits preserve it.
/// </summary>
/// <remarks>
/// Port notes:
/// <list type="bullet">
///   <item><description>
///     The original's <c>TestCases()</c> had one live row (owner); its guest and app rows were already
///     commented out. One row is not a matrix, so these are plain <c>[Test]</c> methods acting as the
///     owner: Sam — the identity that sends the receipt — is the caller throughout.
///   </description></item>
///   <item><description>
///     Every <c>WaitForEmptyOutbox</c> / <c>ProcessInbox</c> became <c>Sync.DrainOutboxAsync</c> /
///     <c>Sync.ProcessInboxAsync</c> — the V1 spellings poll the outbox background service, which this
///     host registers but never starts. <see cref="CanSendReadReceipt"/> drained the same tenant outbox
///     twice (once through the caller's client, once through the owner's); that is one drain here.
///   </description></item>
///   <item><description>
///     Dropped assertion: <see cref="CanSendReadReceipt"/> ended with
///     <c>_scaffold.AssertHasDebugLogEvent(PeerInboxProcessor.ReadReceiptItemMarkedComplete, count: 1)</c>.
///     This framework has no log-event capture, and the surrounding transfer-history assertions already
///     establish that the receipt was processed. Nothing else in the fixture used log events.
///   </description></item>
///   <item><description>
///     <c>SetupCallerWithOwner</c> is not used: the drive has to exist on both identities and they have
///     to be connected before the test runs, which is <see cref="PeerFlow.CreatePeerDriveAsync"/>'s job.
///     Nothing from the original ran between drive-create and caller-build.
///   </description></item>
/// </list>
/// </remarks>
[TestFixture]
public class V1PeerReadReceiptTestsSuccess : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task CanSendReadReceipt()
    {
        var (sender, recipient, targetDrive) = await PrepareScenarioAsync();

        var transitOptions = new TransitOptions
        {
            Recipients = [recipient.Identity]
        };

        var (uploadResult, recipientFile, _, _) =
            await PeerTransferScenario.TransferEncryptedMetadataAsync(sender, recipient, targetDrive, transitOptions);

        //
        // Send the read receipt
        //
        var fileForReadReceipt = PeerTransferScenario.AsExternalFile(recipientFile);
        await PeerTransferScenario.SendReadReceiptAsync(sender, recipient, fileForReadReceipt);

        //
        // Assert the read receipt was updated on the sender's file
        //
        await PeerFlow.DistributeAsync(recipient, sender, targetDrive);

        var getHistoryResponse = await sender.V1.Drive.GetTransferHistory(uploadResult.File);
        Assert.That(getHistoryResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var theHistory = getHistoryResponse.Content;
        Assert.That(theHistory, Is.Not.Null);
        var recipientStatus = theHistory.GetHistoryItem(recipient.Identity);

        Assert.That(recipientStatus, Is.Not.Null, "There should be a status update for the recipient");
        Assert.That(recipientStatus.IsReadByRecipient, Is.True);
        Assert.That(recipientStatus.LatestTransferStatus, Is.EqualTo(LatestTransferStatus.Delivered));
        Assert.That(recipientStatus.LatestSuccessfullyDeliveredVersionTag, Is.EqualTo(uploadResult.NewVersionTag));
    }

    [Test]
    public async Task CanSendMultipleReadReceipts()
    {
        var (sender, recipient, targetDrive) = await PrepareScenarioAsync();

        // send sam two files
        var (senderUploadResult1, recipientFile1, _, _) = await PeerTransferScenario.TransferEncryptedMetadataAsync(
            sender, recipient, targetDrive,
            new TransitOptions
            {
                Recipients = [recipient.Identity]
            });

        var (senderUploadResult2, recipientFile2, _, _) = await PeerTransferScenario.TransferEncryptedMetadataAsync(
            sender, recipient, targetDrive,
            new TransitOptions
            {
                Recipients = [recipient.Identity]
            });

        //
        // Sam Sends the read receipt
        //
        var fileForReadReceipt1 = PeerTransferScenario.AsExternalFile(recipientFile1);
        var fileForReadReceipt2 = PeerTransferScenario.AsExternalFile(recipientFile2);

        var samSendReadReceiptResponse =
            await recipient.V1.Drive.SendReadReceipt([fileForReadReceipt1, fileForReadReceipt2]);

        //
        //Assert both files read-receipt was accepted into the inbox
        //
        DriveAsserts.AssertReadReceiptStatus(samSendReadReceiptResponse, fileForReadReceipt1, sender.Identity,
            SendReadReceiptResultStatus.Enqueued);
        DriveAsserts.AssertReadReceiptStatus(samSendReadReceiptResponse, fileForReadReceipt2, sender.Identity,
            SendReadReceiptResultStatus.Enqueued);

        await recipient.Sync.DrainOutboxAsync();

        //
        // Assert the read receipt was updated on the sender's file
        //
        await sender.Sync.ProcessInboxAsync(targetDrive, batchSize: 100);

        var getHistoryResponse1 = await sender.V1.Drive.GetTransferHistory(senderUploadResult1.File);
        Assert.That(getHistoryResponse1.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var file1TransferHistory = getHistoryResponse1.Content;
        Assert.That(file1TransferHistory, Is.Not.Null);
        var samRecipientStatus1 = file1TransferHistory.GetHistoryItem(recipient.Identity);

        Assert.That(file1TransferHistory.History.Results.Count, Is.EqualTo(1));
        Assert.That(samRecipientStatus1, Is.Not.Null, "There should be a status update for the sam");
        Assert.That(samRecipientStatus1.IsReadByRecipient, Is.True);
        Assert.That(samRecipientStatus1.LatestTransferStatus, Is.EqualTo(LatestTransferStatus.Delivered));
        Assert.That(samRecipientStatus1.LatestSuccessfullyDeliveredVersionTag, Is.EqualTo(senderUploadResult1.NewVersionTag));

        var getHistoryResponse2 = await sender.V1.Drive.GetTransferHistory(senderUploadResult2.File);
        Assert.That(getHistoryResponse2.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var file1TransferHistory2 = getHistoryResponse2.Content;
        Assert.That(file1TransferHistory2, Is.Not.Null);
        var samRecipientStatus2 = file1TransferHistory2.GetHistoryItem(recipient.Identity);

        Assert.That(file1TransferHistory2.History.Results.Count, Is.EqualTo(1));
        Assert.That(samRecipientStatus2, Is.Not.Null, "There should be a status update for the sam");
        Assert.That(samRecipientStatus2.IsReadByRecipient, Is.True);
        Assert.That(samRecipientStatus2.LatestTransferStatus, Is.EqualTo(LatestTransferStatus.Delivered));
        Assert.That(samRecipientStatus2.LatestSuccessfullyDeliveredVersionTag, Is.EqualTo(senderUploadResult2.NewVersionTag));
    }

    [Test]
    public async Task CanSendReadReceiptWithSpecificTimestamp()
    {
        var (sender, recipient, targetDrive) = await PrepareScenarioAsync();

        var transitOptions = new TransitOptions
        {
            Recipients = [recipient.Identity]
        };

        var (_, recipientFile, _, _) =
            await PeerTransferScenario.TransferEncryptedMetadataAsync(sender, recipient, targetDrive, transitOptions);

        var driveClient = recipient.V1.Drive;
        var fileForReadReceipt = PeerTransferScenario.AsExternalFile(recipientFile);

        // Send read receipt with a specific past timestamp
        var pastTimestamp = UnixTimeUtc.Now().AddSeconds(-30);
        var sendReadReceiptResponse = await driveClient.SendReadReceipt([fileForReadReceipt], pastTimestamp);
        Assert.That(sendReadReceiptResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Verify the local ReadTime matches the supplied timestamp
        var getFileHeaderResponse = await driveClient.GetFileHeader(fileForReadReceipt);
        Assert.That(getFileHeaderResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var readTime = getFileHeaderResponse.Content.FileMetadata.LocalAppData.ReadTime;
        Assert.That(readTime, Is.Not.Null);
        Assert.That(readTime.Value.milliseconds, Is.EqualTo(pastTimestamp.milliseconds));
    }

    [Test]
    public async Task SendReadReceiptWithFutureTimestampGetsClamped()
    {
        var (sender, recipient, targetDrive) = await PrepareScenarioAsync();

        var transitOptions = new TransitOptions
        {
            Recipients = [recipient.Identity]
        };

        var (_, recipientFile, _, _) =
            await PeerTransferScenario.TransferEncryptedMetadataAsync(sender, recipient, targetDrive, transitOptions);

        var driveClient = recipient.V1.Drive;
        var fileForReadReceipt = PeerTransferScenario.AsExternalFile(recipientFile);

        // Send read receipt with a future timestamp (1 hour ahead)
        var beforeSend = UnixTimeUtc.Now();
        var futureTimestamp = UnixTimeUtc.Now().AddSeconds(3600);
        var sendReadReceiptResponse = await driveClient.SendReadReceipt([fileForReadReceipt], futureTimestamp);
        Assert.That(sendReadReceiptResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Verify the ReadTime was clamped to approximately now, not the future value
        var getFileHeaderResponse = await driveClient.GetFileHeader(fileForReadReceipt);
        Assert.That(getFileHeaderResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var readTime = getFileHeaderResponse.Content.FileMetadata.LocalAppData.ReadTime;
        Assert.That(readTime, Is.Not.Null);
        Assert.That(readTime.Value.milliseconds, Is.GreaterThanOrEqualTo(beforeSend.milliseconds),
            "ReadTime should be at or after the time we sent the request");
        Assert.That(readTime.Value.milliseconds, Is.LessThan(futureTimestamp.milliseconds),
            "ReadTime should have been clamped, not set to future value");
    }

    [Test]
    public async Task SendReadReceiptDoesNotDowngradeReadTime()
    {
        var (sender, recipient, targetDrive) = await PrepareScenarioAsync();

        var transitOptions = new TransitOptions
        {
            Recipients = [recipient.Identity]
        };

        var (_, recipientFile, _, _) =
            await PeerTransferScenario.TransferEncryptedMetadataAsync(sender, recipient, targetDrive, transitOptions);

        var driveClient = recipient.V1.Drive;
        var fileForReadReceipt = PeerTransferScenario.AsExternalFile(recipientFile);

        // First read receipt with no timestamp (sets ReadTime to ~now)
        var sendReadReceiptResponse1 = await driveClient.SendReadReceipt([fileForReadReceipt]);
        Assert.That(sendReadReceiptResponse1.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Capture the ReadTime
        var getFileHeaderResponse1 = await driveClient.GetFileHeader(fileForReadReceipt);
        Assert.That(getFileHeaderResponse1.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var originalReadTime = getFileHeaderResponse1.Content.FileMetadata.LocalAppData.ReadTime;
        Assert.That(originalReadTime, Is.Not.Null);

        // Second read receipt with an earlier timestamp — should be rejected
        var olderTimestamp = UnixTimeUtc.Now().AddSeconds(-300);
        var sendReadReceiptResponse2 = await driveClient.SendReadReceipt([fileForReadReceipt], olderTimestamp);
        Assert.That(sendReadReceiptResponse2.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Verify ReadTime was NOT downgraded
        var getFileHeaderResponse2 = await driveClient.GetFileHeader(fileForReadReceipt);
        Assert.That(getFileHeaderResponse2.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var currentReadTime = getFileHeaderResponse2.Content.FileMetadata.LocalAppData.ReadTime;
        Assert.That(currentReadTime, Is.Not.Null);
        Assert.That(currentReadTime.Value.milliseconds, Is.EqualTo(originalReadTime.Value.milliseconds),
            "ReadTime should not have been downgraded to the older timestamp");
    }

    [Test]
    public async Task SendReadReceiptWithoutTimestampDoesNotOverwriteExisting()
    {
        var (sender, recipient, targetDrive) = await PrepareScenarioAsync();

        var transitOptions = new TransitOptions
        {
            Recipients = [recipient.Identity]
        };

        var (_, recipientFile, _, _) =
            await PeerTransferScenario.TransferEncryptedMetadataAsync(sender, recipient, targetDrive, transitOptions);

        var driveClient = recipient.V1.Drive;
        var fileForReadReceipt = PeerTransferScenario.AsExternalFile(recipientFile);

        // First: send read receipt with a specific past timestamp
        var pastTimestamp = UnixTimeUtc.Now().AddSeconds(-60);
        var sendReadReceiptResponse1 = await driveClient.SendReadReceipt([fileForReadReceipt], pastTimestamp);
        Assert.That(sendReadReceiptResponse1.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Capture the ReadTime
        var getFileHeaderResponse1 = await driveClient.GetFileHeader(fileForReadReceipt);
        Assert.That(getFileHeaderResponse1.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var originalReadTime = getFileHeaderResponse1.Content.FileMetadata.LocalAppData.ReadTime;
        Assert.That(originalReadTime, Is.Not.Null);
        Assert.That(originalReadTime.Value.milliseconds, Is.EqualTo(pastTimestamp.milliseconds));

        // Second: send read receipt WITHOUT timestamp — should NOT overwrite
        var sendReadReceiptResponse2 = await driveClient.SendReadReceipt([fileForReadReceipt]);
        Assert.That(sendReadReceiptResponse2.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Verify ReadTime was NOT overwritten with now()
        var getFileHeaderResponse2 = await driveClient.GetFileHeader(fileForReadReceipt);
        Assert.That(getFileHeaderResponse2.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var currentReadTime = getFileHeaderResponse2.Content.FileMetadata.LocalAppData.ReadTime;
        Assert.That(currentReadTime, Is.Not.Null);
        Assert.That(currentReadTime.Value.milliseconds, Is.EqualTo(originalReadTime.Value.milliseconds),
            "ReadTime should not have been overwritten by a call without timestamp");
    }

    [Test]
    public async Task SendReadReceiptWithNewerTimestampUpgradesReadTime()
    {
        var (sender, recipient, targetDrive) = await PrepareScenarioAsync();

        var transitOptions = new TransitOptions
        {
            Recipients = [recipient.Identity]
        };

        var (_, recipientFile, _, _) =
            await PeerTransferScenario.TransferEncryptedMetadataAsync(sender, recipient, targetDrive, transitOptions);

        var driveClient = recipient.V1.Drive;
        var fileForReadReceipt = PeerTransferScenario.AsExternalFile(recipientFile);

        // First: send read receipt with an older past timestamp
        var olderTimestamp = UnixTimeUtc.Now().AddSeconds(-60);
        var sendReadReceiptResponse1 = await driveClient.SendReadReceipt([fileForReadReceipt], olderTimestamp);
        Assert.That(sendReadReceiptResponse1.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Verify ReadTime == olderTimestamp
        var getFileHeaderResponse1 = await driveClient.GetFileHeader(fileForReadReceipt);
        Assert.That(getFileHeaderResponse1.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var readTime1 = getFileHeaderResponse1.Content.FileMetadata.LocalAppData.ReadTime;
        Assert.That(readTime1, Is.Not.Null);
        Assert.That(readTime1.Value.milliseconds, Is.EqualTo(olderTimestamp.milliseconds));

        // Second: send read receipt with a newer (but still past) timestamp
        var newerTimestamp = UnixTimeUtc.Now().AddSeconds(-10);
        var sendReadReceiptResponse2 = await driveClient.SendReadReceipt([fileForReadReceipt], newerTimestamp);
        Assert.That(sendReadReceiptResponse2.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Verify ReadTime was upgraded to newerTimestamp
        var getFileHeaderResponse2 = await driveClient.GetFileHeader(fileForReadReceipt);
        Assert.That(getFileHeaderResponse2.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var readTime2 = getFileHeaderResponse2.Content.FileMetadata.LocalAppData.ReadTime;
        Assert.That(readTime2, Is.Not.Null);
        Assert.That(readTime2.Value.milliseconds, Is.EqualTo(newerTimestamp.milliseconds),
            "ReadTime should have been upgraded to the newer timestamp");
    }

    [Test]
    public async Task UpdatingLocalMetadataTagsOrContentPreservesReadTime()
    {
        // Regression: previously UpdateLocalMetadataTags and UpdateLocalMetadataContent rebuilt
        // LocalAppMetadata without copying ReadTime, silently wiping it back to null on every
        // tag/content edit. ReadTime should be preserved across these updates -- only
        // UpdateLocalReadTime (via SendReadReceipt) should modify it, and only monotonically.

        var (sender, recipient, targetDrive) = await PrepareScenarioAsync();

        var transitOptions = new TransitOptions
        {
            Recipients = [recipient.Identity]
        };

        var (_, recipientFile, _, _) =
            await PeerTransferScenario.TransferEncryptedMetadataAsync(sender, recipient, targetDrive, transitOptions);

        var driveClient = recipient.V1.Drive;
        var fileForReadReceipt = PeerTransferScenario.AsExternalFile(recipientFile);

        // Set ReadTime via SendReadReceipt with a specific past timestamp
        var pastTimestamp = UnixTimeUtc.Now().AddSeconds(-30);
        var sendReadReceiptResponse = await driveClient.SendReadReceipt([fileForReadReceipt], pastTimestamp);
        Assert.That(sendReadReceiptResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Capture initial ReadTime and the latest local version tag (needed for subsequent updates)
        var headerAfterReadReceipt = await driveClient.GetFileHeader(fileForReadReceipt);
        Assert.That(headerAfterReadReceipt.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var originalReadTime = headerAfterReadReceipt.Content.FileMetadata.LocalAppData.ReadTime;
        Assert.That(originalReadTime, Is.Not.Null, "ReadTime should be set after SendReadReceipt");
        Assert.That(originalReadTime.Value.milliseconds, Is.EqualTo(pastTimestamp.milliseconds));

        var localVersionTag = headerAfterReadReceipt.Content.FileMetadata.LocalAppData.VersionTag;

        // Update local metadata tags
        var tagsRequest = new UpdateLocalMetadataTagsRequest
        {
            File = fileForReadReceipt,
            LocalVersionTag = localVersionTag,
            Tags = [Guid.NewGuid(), Guid.NewGuid()]
        };
        var tagsResponse = await driveClient.UpdateLocalAppMetadataTags(tagsRequest);
        Assert.That(tagsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // ReadTime must NOT have been wiped by the tag update
        var headerAfterTags = await driveClient.GetFileHeader(fileForReadReceipt);
        Assert.That(headerAfterTags.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var readTimeAfterTags = headerAfterTags.Content.FileMetadata.LocalAppData.ReadTime;
        Assert.That(readTimeAfterTags, Is.Not.Null, "ReadTime should be preserved after UpdateLocalAppMetadataTags");
        Assert.That(readTimeAfterTags.Value.milliseconds, Is.EqualTo(originalReadTime.Value.milliseconds),
            "ReadTime should be unchanged by UpdateLocalAppMetadataTags");

        // Update local metadata content -- file is encrypted so we must supply a strong IV.
        // Content payload isn't validated server-side; random bytes are sufficient for this test.
        var localVersionTagAfterTags = headerAfterTags.Content.FileMetadata.LocalAppData.VersionTag;
        var contentRequest = new UpdateLocalMetadataContentRequest
        {
            File = fileForReadReceipt,
            LocalVersionTag = localVersionTagAfterTags,
            Iv = ByteArrayUtil.GetRndByteArray(16),
            Content = Convert.ToBase64String(ByteArrayUtil.GetRndByteArray(32))
        };
        var contentResponse = await driveClient.UpdateLocalAppMetadataContent(contentRequest);
        Assert.That(contentResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // ReadTime must NOT have been wiped by the content update
        var headerAfterContent = await driveClient.GetFileHeader(fileForReadReceipt);
        Assert.That(headerAfterContent.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var readTimeAfterContent = headerAfterContent.Content.FileMetadata.LocalAppData.ReadTime;
        Assert.That(readTimeAfterContent, Is.Not.Null, "ReadTime should be preserved after UpdateLocalAppMetadataContent");
        Assert.That(readTimeAfterContent.Value.milliseconds, Is.EqualTo(originalReadTime.Value.milliseconds),
            "ReadTime should be unchanged by UpdateLocalAppMetadataContent");
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Replaces the original's <c>PrepareScenario</c>: the same drive on both identities, connected
    /// with Write in both directions. The reverse grant is what lets Sam's read receipt land — it hits
    /// <c>AssertCanWriteToDrive</c> on Frodo's drive.
    /// </summary>
    private async Task<(OwnerSession Sender, OwnerSession Recipient, TargetDrive Drive)> PrepareScenarioAsync()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var targetDrive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, DrivePermission.Write,
            label: "target drive",
            recipientPermissionOnSenderDrive: DrivePermission.Write,
            allowAnonymousReads: false);

        return (sender, recipient, targetDrive);
    }
}
