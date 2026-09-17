using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
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
///     commented out. One row is not a matrix, so these are plain <c>[Test]</c> methods acting as the
///     owner.
///   </description></item>
///   <item><description>
///     The three <c>[Ignore("how do i test this scenario?")]</c> placeholders are carried verbatim,
///     bodies included.
///   </description></item>
///   <item><description>
///     The connect is <em>asymmetric</em>: the sender grants the recipient Read on its drive while the
///     recipient grants the sender Write — which is the whole point of the fixture, since the receipt
///     needs Write on the sender's drive. <see cref="PeerFlow.CreatePeerDriveAsync"/> expresses that
///     through its two permission parameters, so the handshake is no longer hand-rolled here. The
///     connection-info assertion the original's <c>PrepareScenario</c> ended on is setup validation,
///     so it is dropped; <see cref="PeerFlow.ConnectAsync"/> asserts the two calls succeeded instead.
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
///     circles have to be in place before the test runs. Nothing from the original ran between
///     drive-create and caller-build.
///   </description></item>
/// </list>
/// </remarks>
[TestFixture]
public class V1PeerReadReceiptTestsFailures : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task FailToSendReadReceiptWhenRecipientDoesNotHaveWriteAccessToOriginalSendersDrive()
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

        var sendReadReceiptResponse = await recipient.V1.Drive.SendReadReceipt([fileForReadReceipt]);
        await recipient.Sync.DrainOutboxAsync();

        DriveAsserts.AssertReadReceiptStatus(sendReadReceiptResponse, fileForReadReceipt, sender.Identity,
            SendReadReceiptResultStatus.Enqueued);

        //TODO: there is no way to check the status of an item in the outbox; so the best
        //we can do is check if the target file is not updated

        //
        // Assert the read receipt was not updated on the sender's file
        //
        await sender.Sync.ProcessInboxAsync(targetDrive);

        AssertNotReadByRecipient(await GetHistoryAsync(sender, uploadResult), recipient, uploadResult);
    }

    [Test]
    public async Task FailToSendReadReceiptWhenNotConnectedOnRecipientSide()
    {
        var (frodo, sam, targetDrive) = await PrepareScenarioAsync();

        var transitOptions = new TransitOptions
        {
            Recipients = [sam.Identity]
        };

        var (uploadResult, recipientFile, _, _) =
            await PeerTransferScenario.TransferEncryptedMetadataAsync(frodo, sam, targetDrive, transitOptions);

        //
        // Send the read receipt
        //
        var fileForReadReceipt = PeerTransferScenario.AsExternalFile(recipientFile);

        //
        // Severe the connection
        //
        await sam.Connections.DisconnectFrom(frodo.Identity);

        var sendReadReceiptResponse = await sam.V1.Drive.SendReadReceipt([fileForReadReceipt]);

        DriveAsserts.AssertReadReceiptStatus(sendReadReceiptResponse, fileForReadReceipt, frodo.Identity,
            SendReadReceiptResultStatus.NotConnectedToOriginalSender);

        //
        // Assert the read receipt was not updated on the sender's file
        //
        await frodo.Sync.ProcessInboxAsync(targetDrive);

        AssertNotReadByRecipient(await GetHistoryAsync(frodo, uploadResult), sam, uploadResult);
    }

    [Test]
    public async Task FailToSendReadReceiptWhenNotConnectedOnSenderSide()
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

        //
        // Severe the connection
        //
        await sender.Connections.DisconnectFrom(recipient.Identity);

        var sendReadReceiptResponse = await recipient.V1.Drive.SendReadReceipt([fileForReadReceipt]);

        DriveAsserts.AssertReadReceiptStatus(sendReadReceiptResponse, fileForReadReceipt, sender.Identity,
            SendReadReceiptResultStatus.Enqueued);

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

    [Test]
    [Ignore("how do i test this scenario?")]
    public Task SendReadReceiptWhenOriginalSenderIdentityIsNotResponding()
    {
        Assert.Inconclusive("");
        return Task.CompletedTask;
    }

    [Test]
    [Ignore("how do i test this scenario?")]
    public Task SendReadReceiptWithInvalidGlobalTransitId()
    {
        Assert.Inconclusive("");
        return Task.CompletedTask;
    }

    [Test]
    public async Task FailToSendReadReceiptToSendersFiles()
    {
        var (sender, recipient, targetDrive) = await PrepareScenarioAsync();

        var transitOptions = new TransitOptions
        {
            Recipients = [recipient.Identity]
        };

        var (senderUploadResult, _, _, _) =
            await PeerTransferScenario.TransferEncryptedMetadataAsync(sender, recipient, targetDrive, transitOptions);

        //
        // Send the read receipt
        //
        var fileForReadReceipt = new ExternalFileIdentifier
        {
            FileId = senderUploadResult.File.FileId,
            TargetDrive = senderUploadResult.File.TargetDrive
        };

        // NOTE: unlike its siblings, this test's caller is the *sender* -- it read-receipts its own file.
        var sendReadReceiptResponse = await sender.V1.Drive.SendReadReceipt([fileForReadReceipt]);
        await sender.Sync.DrainOutboxAsync();

        DriveAsserts.AssertReadReceiptStatus(sendReadReceiptResponse, fileForReadReceipt, expectedRecipient: null,
            SendReadReceiptResultStatus.CannotSendReadReceiptToSelf);

        //
        // Assert the read receipt was not updated on the sender's file
        //
        await sender.Sync.ProcessInboxAsync(targetDrive);

        AssertNotReadByRecipient(await GetHistoryAsync(sender, senderUploadResult), recipient, senderUploadResult);
    }

    [Test]
    [Ignore("how do i test this scenario?")]
    public Task SendReadReceiptWhenNeverHaveReceivedTheOriginalFile()
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

    /// <summary>
    /// The original's <c>PrepareScenario</c>, expressed through <see cref="PeerFlow"/>: the sender's
    /// circle gives the recipient Read on the sender's drive, and the recipient's circle gives the
    /// sender Write on the recipient's — so the file transfers, but the receipt coming back has no
    /// Write on the drive it needs to update.
    /// </summary>
    private async Task<(OwnerSession Sender, OwnerSession Recipient, TargetDrive Drive)> PrepareScenarioAsync()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var targetDrive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, DrivePermission.Write,
            label: "target drive",
            recipientPermissionOnSenderDrive: DrivePermission.Read,
            allowAnonymousReads: false);

        return (sender, recipient, targetDrive);
    }
}
