using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/_Universal/Outbox/OutboxProcessingSingleRecipientTestsFailureScenario.cs
///
/// The three ways one recipient's leg of a send can fail permanently: the sender was disconnected
/// before the send, the sender's grant on the recipient's drive is Read rather than Write, and the
/// source file forbids distribution at all.
/// </summary>
/// <remarks>
/// Checked port.
/// <list type="bullet">
/// <item>The original's <c>TestCases()</c> had one live row,
/// <c>OwnerClientContext(TargetDrive.NewTargetDrive())</c>, with two commented-out siblings kept
/// verbatim below. One row is not a matrix, so these are plain <c>[Test]</c>s and the always-true
/// <c>if (expectedStatusCode == OK)</c> guard around each test's assertions is gone.</item>
/// <item>The caller the matrix built was <b>unused</b> beyond <c>WaitForEmptyOutbox</c>: every
/// assertion ran through the sender's own owner client. Nothing here builds a caller.</item>
/// <item><c>WaitForEmptyOutbox</c> becomes <c>Sync.DrainOutboxAsync()</c> — the V1 call is a passive
/// poll on the outbox background service, which the fast host registers but never starts.</item>
/// <item>The third test's <c>await Task.Delay(TimeSpan.FromSeconds(10))</c>, with its comment that
/// "the outbox will never go empty in this test so we just need to sleep for a bit … eww", maps onto
/// the same drain: <c>PeerOutboxProcessorBackgroundService.DrainAsync</c> makes a bounded number of
/// retry passes (<c>DefaultDrainRetryPasses</c>, well under <c>OutboxOperationMaxAttempts</c>) and
/// returns with permanently-failed items still queued, which is exactly what the
/// <c>IsInOutbox</c> assertion then reads. The port is deterministic where the original was a
/// timing bet.</item>
/// <item>Trailing <c>DisconnectFrom</c> / <c>DeleteScenario</c> calls were cleanup only and are
/// dropped — per-test reset covers them.</item>
/// <item>Carried defect: the second test names itself
/// <c>...WhenSenderHasNoAccessToTargetDrive</c> but reaches that state by granting the sender
/// <c>DrivePermission.Read</c> on a drive it needs Write on; the third test
/// (<c>...WhenSourceFileDoesNotAllowDistribution</c>) inherits that same Read grant from its copy of
/// <c>PrepareScenario</c> even though the distribution flag, not the grant, is what it is testing.
/// Both are carried as-is.</item>
/// </list>
/// </remarks>
[TestFixture]
public class OutboxProcessingSingleRecipientTestsFailureScenario : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    // The original's live row was [OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.OK]
    // — one row, so these are plain [Test]s. Its two commented-out siblings, kept verbatim:
    //   GuestWriteOnlyAccessToDrive -> HttpStatusCode.MethodNotAllowed
    //   AppWriteOnlyAccessToDrive   -> HttpStatusCode.OK
    [Test]
    public async Task RecipientTransferHistoryOnSenderIsUpdatedTo_AccessDenied_WhenSenderIsNotConnectedToRecipient()
    {
        var senderOwnerClient = await LoginAsOwner(Identities.Frodo);
        var recipientOwnerClient = await LoginAsOwner(Identities.Sam);

        const DrivePermission drivePermissions = DrivePermission.Write;

        var targetDrive = TargetDrive.NewTargetDrive();
        await OutboxScenario.PrepareAsync(senderOwnerClient, recipientOwnerClient, targetDrive, drivePermissions);

        //
        // force a disconnection before sending the file
        //
        await recipientOwnerClient.Connections.DisconnectFrom(senderOwnerClient.Identity);

        var transitOptions = new TransitOptions
        {
            Recipients = [recipientOwnerClient.Identity.DomainName]
        };

        var (uploadResult, _, _) = await PeerTransferScenario.UploadEncryptedMetadataAsync(
            senderOwnerClient, targetDrive, transitOptions);

        Assert.That(uploadResult.RecipientStatus[recipientOwnerClient.Identity], Is.EqualTo(TransferStatus.Enqueued));

        await recipientOwnerClient.Sync.ProcessInboxAsync(uploadResult.File.TargetDrive);

        var recipientFileResponse =
            await recipientOwnerClient.V1.Drive.QueryByGlobalTransitId(uploadResult.GlobalTransitIdFileIdentifier);
        Assert.That(recipientFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(recipientFileResponse.Content.SearchResults, Is.Empty, "Recipient should not have the file");

        //
        // Validate the transfer history was updated correctly
        //
        var getHistoryResponse = await senderOwnerClient.V1.Drive.GetTransferHistory(uploadResult.File);
        Assert.That(getHistoryResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var theHistory = getHistoryResponse.Content;
        Assert.That(theHistory, Is.Not.Null);
        var recipientStatus = theHistory.GetHistoryItem(recipientOwnerClient.Identity);

        Assert.That(recipientStatus, Is.Not.Null, "There should be a status update for the recipient");
        Assert.That(recipientStatus.IsInOutbox, Is.False);
        Assert.That(recipientStatus.IsReadByRecipient, Is.False);
        Assert.That(recipientStatus.LatestTransferStatus,
            Is.EqualTo(LatestTransferStatus.RecipientIdentityReturnedAccessDenied));
        Assert.That(recipientStatus.LatestSuccessfullyDeliveredVersionTag, Is.Null);
    }

    [Test]
    public async Task RecipientTransferHistoryOnSenderIsUpdatedTo_AccessDenied_WhenSenderHasNoAccessToTargetDrive()
    {
        var senderOwnerClient = await LoginAsOwner(Identities.Frodo);
        var recipientOwnerClient = await LoginAsOwner(Identities.Sam);

        const DrivePermission drivePermissions = DrivePermission.Read;

        var targetDrive = TargetDrive.NewTargetDrive();
        await OutboxScenario.PrepareAsync(senderOwnerClient, recipientOwnerClient, targetDrive, drivePermissions);

        var transitOptions = new TransitOptions
        {
            Recipients = [recipientOwnerClient.Identity.DomainName]
        };

        var (uploadResult, _, _) = await PeerTransferScenario.UploadEncryptedMetadataAsync(
            senderOwnerClient, targetDrive, transitOptions);

        Assert.That(uploadResult.RecipientStatus[recipientOwnerClient.Identity], Is.EqualTo(TransferStatus.Enqueued));

        // validate recipient got the file
        await recipientOwnerClient.Sync.ProcessInboxAsync(uploadResult.File.TargetDrive);

        var recipientFileResponse =
            await recipientOwnerClient.V1.Drive.QueryByGlobalTransitId(uploadResult.GlobalTransitIdFileIdentifier);
        Assert.That(recipientFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(recipientFileResponse.Content.SearchResults, Is.Empty, "Recipient should not have the file");

        //
        // Validate the transfer history was updated correctly
        //
        var getHistoryResponse = await senderOwnerClient.V1.Drive.GetTransferHistory(uploadResult.File);
        Assert.That(getHistoryResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var theHistory = getHistoryResponse.Content;
        Assert.That(theHistory, Is.Not.Null);
        var recipientStatus = theHistory.GetHistoryItem(recipientOwnerClient.Identity);

        Assert.That(recipientStatus, Is.Not.Null, "There should be a status update for the recipient");
        Assert.That(recipientStatus.IsInOutbox, Is.False);
        Assert.That(recipientStatus.IsReadByRecipient, Is.False);
        Assert.That(recipientStatus.LatestTransferStatus,
            Is.EqualTo(LatestTransferStatus.RecipientIdentityReturnedAccessDenied));
        Assert.That(recipientStatus.LatestSuccessfullyDeliveredVersionTag, Is.Null);
    }

    [Test]
    public async Task RecipientTransferHistoryOnSenderIsUpdatedTo_WhenSourceFileDoesNotAllowDistribution()
    {
        var senderOwnerClient = await LoginAsOwner(Identities.Frodo);
        var recipientOwnerClient = await LoginAsOwner(Identities.Sam);

        const DrivePermission drivePermissions = DrivePermission.Read;

        var targetDrive = TargetDrive.NewTargetDrive();
        await OutboxScenario.PrepareAsync(senderOwnerClient, recipientOwnerClient, targetDrive, drivePermissions);

        var transitOptions = new TransitOptions
        {
            Recipients = [recipientOwnerClient.Identity.DomainName]
        };

        var (uploadResult, _, _) = await PeerTransferScenario.UploadEncryptedMetadataAsync(
            senderOwnerClient, targetDrive, transitOptions, allowDistribution: false);

        Assert.That(uploadResult.RecipientStatus[recipientOwnerClient.Identity], Is.EqualTo(TransferStatus.Enqueued));

        // The item fails and is rescheduled on purpose, so the outbox never empties. The drain makes a
        // bounded number of passes and returns with it still queued — which is what IsInOutbox reads.
        await senderOwnerClient.Sync.DrainOutboxAsync();

        // validate recipient got the file
        await recipientOwnerClient.Sync.ProcessInboxAsync(uploadResult.File.TargetDrive);

        var recipientFileResponse =
            await recipientOwnerClient.V1.Drive.QueryByGlobalTransitId(uploadResult.GlobalTransitIdFileIdentifier);
        Assert.That(recipientFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(recipientFileResponse.Content.SearchResults, Is.Empty, "Recipient should not have the file");

        //
        // Validate the transfer history was updated correctly
        //
        var getHistoryResponse = await senderOwnerClient.V1.Drive.GetTransferHistory(uploadResult.File);
        Assert.That(getHistoryResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var theHistory = getHistoryResponse.Content;
        Assert.That(theHistory, Is.Not.Null);
        var recipientStatus = theHistory.GetHistoryItem(recipientOwnerClient.Identity);

        Assert.That(recipientStatus, Is.Not.Null, "There should be a status update for the recipient");
        Assert.That(recipientStatus.IsInOutbox, Is.True, "file should remain in outbox");
        Assert.That(recipientStatus.IsReadByRecipient, Is.False);
        Assert.That(recipientStatus.LatestTransferStatus,
            Is.EqualTo(LatestTransferStatus.SourceFileDoesNotAllowDistribution));
        Assert.That(recipientStatus.LatestSuccessfullyDeliveredVersionTag, Is.Null);

        //Note: there should also be a job set to rerun this time; not sure how to test this - however.
    }
}
