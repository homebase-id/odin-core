using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Time;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/_Universal/Peer/TransferHistory/TransferHistoryMultipleRecipientsTests.cs
///
/// Transfer history and transfer summary when one send fans out to several recipients and only some
/// of them succeed: a file that forbids distribution (every recipient fails and stays in the outbox),
/// and a send where half the recipients have severed the connection (those come back access-denied
/// while the rest deliver and send read receipts).
/// </summary>
/// <remarks>
/// Carries the <c>V1</c> prefix only because <see cref="TransferHistoryTests"/> in this folder already
/// owns the unprefixed name; see also <see cref="V1TransferHistoryTests"/>.
///
/// Port notes:
/// <list type="bullet">
///   <item><description>
///     The original's <c>Task.Delay(1000)</c> "to let the outbox process in the background" is
///     <c>Sync.DrainOutboxAsync</c> here. The comment next to it — that <c>WaitForEmptyOutbox</c> is
///     unusable because the items stay stuck on purpose — still applies to the drain: it makes a
///     bounded number of retry passes and returns with the failed items still queued, which is exactly
///     what <c>TotalInOutbox</c> is then asserted on.
///   </description></item>
///   <item><description>
///     Fixed defect: the original asserted neither the connection-request nor the accept response in
///     <c>ConnectRecipientsToSender</c> — both <c>ClassicAssert</c>s are commented out there, leaving
///     only a <c>Console.WriteLine</c>, so a setup failure surfaced as a confusing downstream
///     assertion. The handshake now runs through <see cref="PeerFlow.CreatePeerDriveAsync"/>, which
///     asserts both calls succeeded.
///   </description></item>
///   <item><description>
///     The original's on-failure <c>Console.WriteLine</c> history dump is dropped; the assertion
///     message keeps the counts NUnit cannot print for itself.
///   </description></item>
///   <item><description>
///     <c>SetupCallerWithOwner</c> is not used — the drive must exist on every participant and the
///     connections must be in place before the caller is built. Nothing from the original ran between
///     drive-create and caller-build.
///   </description></item>
/// </list>
/// </remarks>
[TestFixture]
public class V1TransferHistoryMultipleRecipientsTests : V2Fixture
{
    protected override string[] HostIdentities =>
    [
        Identities.Frodo, Identities.Sam, Identities.Merry, Identities.Pippin, Identities.TomBombadil,
        Identities.Collab
    ];

    public static IEnumerable<object[]> HistoryCases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Secured()), HttpStatusCode.OK];
        yield return
        [
            CallerSpec.App(DriveSpec.Secured(), DrivePermission.ReadWrite, PermissionKeys.All.ToArray()),
            HttpStatusCode.OK
        ];
    }

    [Test, TestCaseSource(nameof(HistoryCases))]
    public async Task CanReadTransferSummaryFromFileMixResultsWhenSourceFileDoesNotAllowDistribution(
        CallerSpec spec, HttpStatusCode expected)
    {
        var owners = await LoginAllAsync(Identities.Merry, Identities.Pippin, Identities.Collab);
        var sender = owners[0];
        await sender.Admin.DisableAutoAcceptIntroductions();
        List<OwnerSession> connectedRecipients = [owners[1], owners[2]];

        //
        // Setup
        //
        var targetDrive = spec.TargetDrive;
        await ConnectRecipientsToSenderAsync(sender, connectedRecipients, targetDrive, DrivePermission.Write);

        var caller = await spec.Build(sender);

        //
        // Act: transfer the file
        //
        var transitOptions = new TransitOptions
        {
            Recipients = connectedRecipients.Select(r => r.Identity.DomainName).ToList()
        };

        var (uploadResult, _, _) = await PeerTransferScenario.UploadEncryptedMetadataAsync(
            sender, targetDrive, transitOptions, allowDistribution: false);

        // The items fail and are rescheduled on purpose, so the outbox never empties. The drain makes a
        // bounded number of passes and returns with them still queued.
        await sender.Sync.DrainOutboxAsync();

        //
        // Assert: the sender has the transfer history updated
        //
        var uploadedFileResponse1 = await caller.V1.Drive.GetFileHeader(uploadResult.File);
        Assert.That(uploadedFileResponse1.StatusCode, Is.EqualTo(expected));
        var uploadedFile1 = uploadedFileResponse1.Content;

        var summary = uploadedFile1.ServerMetadata.TransferHistory.Summary;
        Assert.That(summary, Is.Not.Null, "missing transfer summary");
        Assert.That(summary.TotalDelivered, Is.EqualTo(0));
        Assert.That(summary.TotalReadByRecipient, Is.EqualTo(0));

        Assert.That(summary.TotalFailed, Is.EqualTo(connectedRecipients.Count),
            $"(delivered:{summary.TotalDelivered}, in outbox: {summary.TotalInOutbox})");

        Assert.That(summary.TotalInOutbox, Is.EqualTo(connectedRecipients.Count));
    }

    [Test, TestCaseSource(nameof(HistoryCases))]
    public async Task CanReadTransferSummaryFromFileMixResultsWhenRecipientReturnsAccessDenied(
        CallerSpec spec, HttpStatusCode expected)
    {
        var owners = await LoginAllAsync(Identities.Frodo, Identities.TomBombadil, Identities.Sam,
            Identities.Collab, Identities.Merry);
        var sender = owners[0];
        await sender.Admin.DisableAutoAcceptIntroductions();
        List<OwnerSession> connectedRecipients = [owners[1], owners[2]];
        List<OwnerSession> disconnectedRecipients = [owners[3], owners[4]];

        var allRecipients = connectedRecipients.ToList();
        allRecipients.AddRange(disconnectedRecipients);

        //
        // Setup
        //
        var targetDrive = spec.TargetDrive;
        await ConnectRecipientsToSenderAsync(sender, allRecipients, targetDrive, DrivePermission.Write);
        await RecipientsToDisconnectFromSenderAsync(disconnectedRecipients, sender);

        var caller = await spec.Build(sender);

        //
        // Act: transfer the file then send read receipts
        //
        var transitOptions = new TransitOptions
        {
            Recipients = allRecipients.Select(r => r.Identity.DomainName).ToList()
        };

        var (uploadResult, _, _) =
            await PeerTransferScenario.UploadEncryptedMetadataAsync(sender, targetDrive, transitOptions);

        foreach (var recipient in connectedRecipients)
        {
            await SendReadReceiptAsync(sender, recipient, targetDrive, uploadResult);
        }

        await sender.Sync.ProcessInboxAsync(targetDrive); // process all read receipts

        //
        // Assert: the sender has the transfer history updated
        //
        var uploadedFileResponse1 = await caller.V1.Drive.GetFileHeader(uploadResult.File);
        Assert.That(uploadedFileResponse1.StatusCode, Is.EqualTo(expected));
        var uploadedFile1 = uploadedFileResponse1.Content;

        var summary = uploadedFile1.ServerMetadata.TransferHistory.Summary;
        Assert.That(summary, Is.Not.Null, "missing transfer summary");
        Assert.That(summary.TotalDelivered, Is.EqualTo(connectedRecipients.Count));
        Assert.That(summary.TotalReadByRecipient, Is.EqualTo(connectedRecipients.Count));
        Assert.That(summary.TotalFailed, Is.EqualTo(disconnectedRecipients.Count));
        Assert.That(summary.TotalInOutbox, Is.EqualTo(0));
    }

    [Test, TestCaseSource(nameof(HistoryCases))]
    public async Task CanReadTransferHistoryForFileMixResults(CallerSpec spec, HttpStatusCode expected)
    {
        var owners = await LoginAllAsync(Identities.Frodo, Identities.TomBombadil, Identities.Sam,
            Identities.Collab, Identities.Merry, Identities.Pippin);
        var sender = owners[0];
        await sender.Admin.DisableAutoAcceptIntroductions();
        List<OwnerSession> connectedRecipients = [owners[1], owners[2]];
        List<OwnerSession> disconnectedRecipients = [owners[3], owners[4], owners[5]];

        var allRecipients = connectedRecipients.ToList();
        allRecipients.AddRange(disconnectedRecipients);

        //
        // Setup
        //
        var targetDrive = spec.TargetDrive;
        await ConnectRecipientsToSenderAsync(sender, allRecipients, targetDrive, DrivePermission.Write);
        await RecipientsToDisconnectFromSenderAsync(disconnectedRecipients, sender);

        var caller = await spec.Build(sender);

        //
        // Act transfer file, then send read receipt
        //
        var transitOptions = new TransitOptions
        {
            Recipients = allRecipients.Select(r => r.Identity.DomainName).ToList()
        };

        var (uploadResult, _, _) =
            await PeerTransferScenario.UploadEncryptedMetadataAsync(sender, targetDrive, transitOptions);

        foreach (var recipient in connectedRecipients)
        {
            await SendReadReceiptAsync(sender, recipient, targetDrive, uploadResult);
        }

        await sender.Sync.ProcessInboxAsync(targetDrive); // process all read receipts

        //
        // Assert: the sender has the transfer history updated
        //
        var historyResponse = await caller.V1.Drive.GetTransferHistory(uploadResult.File);
        Assert.That(historyResponse.StatusCode, Is.EqualTo(expected));

        var theHistory = historyResponse.Content;
        Assert.That(theHistory.OriginalRecipientCount, Is.EqualTo(transitOptions.Recipients.Count));
        Assert.That(theHistory.History.Results.Count, Is.EqualTo(transitOptions.Recipients.Count));

        foreach (var recipient in connectedRecipients)
        {
            var recipientStatus = theHistory.GetHistoryItem(recipient.Identity);
            Assert.That(recipientStatus, Is.Not.Null, $"There should be a status update for {recipient.Identity}");
            Assert.That(recipientStatus.IsReadByRecipient, Is.True, $"recipient: {recipient.Identity}");
            Assert.That(recipientStatus.LatestTransferStatus, Is.EqualTo(LatestTransferStatus.Delivered),
                $"recipient: {recipient.Identity}");
            Assert.That(recipientStatus.LatestSuccessfullyDeliveredVersionTag, Is.EqualTo(uploadResult.NewVersionTag),
                $"recipient: {recipient.Identity}");
        }

        foreach (var recipient in disconnectedRecipients)
        {
            var recipientStatus = theHistory.GetHistoryItem(recipient.Identity);
            Assert.That(recipientStatus, Is.Not.Null, $"There should be a status update for {recipient.Identity}");
            Assert.That(recipientStatus.IsReadByRecipient, Is.False, $"recipient: {recipient.Identity}");
            Assert.That(recipientStatus.IsInOutbox, Is.False, $"recipient: {recipient.Identity}");
            Assert.That(recipientStatus.LatestTransferStatus,
                Is.EqualTo(LatestTransferStatus.RecipientIdentityReturnedAccessDenied), $"recipient: {recipient.Identity}");
            Assert.That(recipientStatus.LatestSuccessfullyDeliveredVersionTag, Is.Null, $"recipient: {recipient.Identity}");
        }
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Logs in several owners at once. These are distinct tenants with distinct sessions, so there is
    /// no shared scope to serialize on — the <c>ScopedConnectionFactory</c> parallelism hazard is
    /// within one lifetime scope, and each login gets its own.
    /// </summary>
    private Task<OwnerSession[]> LoginAllAsync(params string[] identities) =>
        Task.WhenAll(identities.Select(LoginAsOwner));

    /// <summary>
    /// One connected recipient processes its inbox, finds its copy, marks it read, and drains its own
    /// outbox so the receipt reaches the sender's inbox.
    /// </summary>
    private static async Task SendReadReceiptAsync(
        OwnerSession sender, OwnerSession recipient, TargetDrive targetDrive, UploadResult uploadResult)
    {
        await recipient.Sync.ProcessInboxAsync(targetDrive);

        // get the file for the recipient
        var recipientFile = await PeerTransferScenario.GetRecipientCopyAsync(recipient, uploadResult);

        //
        // Send the read receipt
        //
        var fileForReadReceipt = PeerTransferScenario.AsExternalFile(recipientFile);
        await PeerTransferScenario.SendReadReceiptAsync(sender, recipient, fileForReadReceipt);

        // validate the local file's local app data was updated
        var getFileHeaderResponse = await recipient.V1.Drive.GetFileHeader(fileForReadReceipt);
        Assert.That(getFileHeaderResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var readTime = getFileHeaderResponse.Content.FileMetadata.LocalAppData.ReadTime;
        Assert.That(readTime, Is.Not.Null);
        // greater than a minute ago
        Assert.That(readTime.GetValueOrDefault().milliseconds,
            Is.GreaterThan(UnixTimeUtc.Now().AddSeconds(-60).milliseconds));

        await recipient.Sync.DrainOutboxAsync();
    }

    private static async Task RecipientsToDisconnectFromSenderAsync(List<OwnerSession> recipients, OwnerSession sender)
    {
        foreach (var recipient in recipients)
        {
            await recipient.Connections.DisconnectFrom(sender.Identity);
        }
    }

    /// <summary>
    /// Every recipient gets the sender's drive and a mutual grant on it: the sender needs Write on the
    /// recipient's copy to deliver the file, and the recipient needs Write on the sender's copy to send
    /// a read receipt back. The recipient is the one that sends the connection request, as in the
    /// original — hence the argument order into <see cref="PeerFlow.CreatePeerDriveAsync"/>.
    /// </summary>
    private static async Task ConnectRecipientsToSenderAsync(
        OwnerSession sender,
        List<OwnerSession> recipients,
        TargetDrive targetDrive,
        DrivePermission drivePermission)
    {
        foreach (var recipient in recipients)
        {
            // setup the recipients
            await recipient.Admin.DisableAutoAcceptIntroductions();

            await PeerFlow.CreatePeerDriveAsync(recipient, sender, drivePermission,
                label: "target drive",
                recipientPermissionOnSenderDrive: drivePermission,
                allowAnonymousReads: false,
                drive: targetDrive);
        }
    }
}
