using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Time;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
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
///     Carried defect: the original does not assert the connection-request / accept responses in
///     <c>ConnectRecipientsToSender</c> — both <c>ClassicAssert</c>s are commented out, leaving only a
///     <c>Console.WriteLine</c>. Left unasserted here too (a port is a move); a setup failure surfaces
///     as a confusing downstream assertion rather than at its source.
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
        var sender = await LoginAsOwner(Identities.Merry);
        await DisableAutoAcceptIntroductionsAsync(sender);

        List<OwnerSession> connectedRecipients =
        [
            await LoginAsOwner(Identities.Pippin),
            await LoginAsOwner(Identities.Collab)
        ];

        //
        // Setup
        //
        var targetDrive = spec.TargetDrive;
        var senderCircleId = await PrepareSenderAsync(sender, targetDrive, DrivePermission.Write);
        await ConnectRecipientsToSenderAsync(sender, connectedRecipients, targetDrive, DrivePermission.Write, senderCircleId);

        var caller = await spec.Build(sender);

        //
        // Act: transfer the file
        //
        var transitOptions = new TransitOptions
        {
            Recipients = connectedRecipients.Select(r => r.Identity.DomainName).ToList()
        };

        var uploadResult = await TransferEncryptedMetadataAsync(sender, targetDrive, transitOptions, allowDistribution: false);

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
        var sender = await LoginAsOwner(Identities.Frodo);
        await DisableAutoAcceptIntroductionsAsync(sender);

        List<OwnerSession> connectedRecipients =
        [
            await LoginAsOwner(Identities.TomBombadil),
            await LoginAsOwner(Identities.Sam)
        ];

        List<OwnerSession> disconnectedRecipients =
        [
            await LoginAsOwner(Identities.Collab),
            await LoginAsOwner(Identities.Merry)
        ];

        var allRecipients = connectedRecipients.ToList();
        allRecipients.AddRange(disconnectedRecipients);

        //
        // Setup
        //
        var targetDrive = spec.TargetDrive;
        var senderCircleId = await PrepareSenderAsync(sender, targetDrive, DrivePermission.Write);
        await ConnectRecipientsToSenderAsync(sender, allRecipients, targetDrive, DrivePermission.Write, senderCircleId);
        await RecipientsToDisconnectFromSenderAsync(disconnectedRecipients, sender);

        var caller = await spec.Build(sender);

        //
        // Act: transfer the file then send read receipts
        //
        var transitOptions = new TransitOptions
        {
            Recipients = allRecipients.Select(r => r.Identity.DomainName).ToList()
        };

        var uploadResult = await TransferEncryptedMetadataAsync(sender, targetDrive, transitOptions);

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
        var sender = await LoginAsOwner(Identities.Frodo);
        await DisableAutoAcceptIntroductionsAsync(sender);

        List<OwnerSession> connectedRecipients =
        [
            await LoginAsOwner(Identities.TomBombadil),
            await LoginAsOwner(Identities.Sam)
        ];

        List<OwnerSession> disconnectedRecipients =
        [
            await LoginAsOwner(Identities.Collab),
            await LoginAsOwner(Identities.Merry),
            await LoginAsOwner(Identities.Pippin)
        ];

        var allRecipients = connectedRecipients.ToList();
        allRecipients.AddRange(disconnectedRecipients);

        //
        // Setup
        //
        var targetDrive = spec.TargetDrive;
        var senderCircleId = await PrepareSenderAsync(sender, targetDrive, DrivePermission.Write);
        await ConnectRecipientsToSenderAsync(sender, allRecipients, targetDrive, DrivePermission.Write, senderCircleId);
        await RecipientsToDisconnectFromSenderAsync(disconnectedRecipients, sender);

        var caller = await spec.Build(sender);

        //
        // Act transfer file, then send read receipt
        //
        var transitOptions = new TransitOptions
        {
            Recipients = allRecipients.Select(r => r.Identity.DomainName).ToList()
        };

        var uploadResult = await TransferEncryptedMetadataAsync(sender, targetDrive, transitOptions);

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
            var recipientStatus = theHistory.History.Results.SingleOrDefault(r => r.Recipient == recipient.Identity);
            Assert.That(recipientStatus, Is.Not.Null, $"There should be a status update for {recipient.Identity}");
            Assert.That(recipientStatus.IsReadByRecipient, Is.True, $"recipient: {recipient.Identity}");
            Assert.That(recipientStatus.LatestTransferStatus, Is.EqualTo(LatestTransferStatus.Delivered),
                $"recipient: {recipient.Identity}");
            Assert.That(recipientStatus.LatestSuccessfullyDeliveredVersionTag, Is.EqualTo(uploadResult.NewVersionTag),
                $"recipient: {recipient.Identity}");
        }

        foreach (var recipient in disconnectedRecipients)
        {
            var recipientStatus = theHistory.History.Results.SingleOrDefault(r => r.Recipient == recipient.Identity);
            Assert.That(recipientStatus, Is.Not.Null, $"There should be a status update for {recipient.Identity}");
            Assert.That(recipientStatus.IsReadByRecipient, Is.False, $"recipient: {recipient.Identity}");
            Assert.That(recipientStatus.IsInOutbox, Is.False, $"recipient: {recipient.Identity}");
            Assert.That(recipientStatus.LatestTransferStatus,
                Is.EqualTo(LatestTransferStatus.RecipientIdentityReturnedAccessDenied), $"recipient: {recipient.Identity}");
            Assert.That(recipientStatus.LatestSuccessfullyDeliveredVersionTag, Is.Null, $"recipient: {recipient.Identity}");
        }
    }

    // ---------------------------------------------------------------------------------------------

    private static Task DisableAutoAcceptIntroductionsAsync(OwnerSession owner) =>
        owner.Admin.UpdateTenantSettingsFlag(TenantConfigFlagNames.DisableAutoAcceptIntroductionsForTests, "true");

    private static async Task<UploadResult> TransferEncryptedMetadataAsync(
        OwnerSession sender,
        TargetDrive targetDrive,
        TransitOptions transitOptions,
        bool allowDistribution = true)
    {
        const string uploadedContent = "pie";

        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = allowDistribution,
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
        var uploadResult = uploadResponse.Content;
        Assert.That(uploadResult.RecipientStatus.Count, Is.EqualTo(transitOptions.Recipients.Count));

        if (allowDistribution) // we will only have a chance to get an empty outbox if the file is distributed
        {
            await sender.Sync.DrainOutboxAsync();
        }

        return uploadResult;
    }

    /// <summary>
    /// One connected recipient processes its inbox, finds its copy, marks it read, and drains its own
    /// outbox so the receipt reaches the sender's inbox.
    /// </summary>
    private static async Task SendReadReceiptAsync(
        OwnerSession sender, OwnerSession recipient, TargetDrive targetDrive, UploadResult uploadResult)
    {
        await recipient.Sync.ProcessInboxAsync(targetDrive);

        // get the file for the recipient
        var recipientFileResponse = await recipient.V1.Drive.QueryByGlobalTransitId(uploadResult.GlobalTransitIdFileIdentifier);
        Assert.That(recipientFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var recipientFile = recipientFileResponse.Content.SearchResults.SingleOrDefault();
        Assert.That(recipientFile, Is.Not.Null, $"recipient: {recipient.Identity}");

        //
        // Send the read receipt
        //
        var fileForReadReceipt = new ExternalFileIdentifier
        {
            FileId = recipientFile.FileId,
            TargetDrive = recipientFile.TargetDrive
        };

        var sendReadReceiptResponse = await recipient.V1.Drive.SendReadReceipt([fileForReadReceipt]);
        Assert.That(sendReadReceiptResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var sendReadReceiptResult = sendReadReceiptResponse.Content;
        Assert.That(sendReadReceiptResult, Is.Not.Null);
        var item = sendReadReceiptResult.Results.SingleOrDefault(d => d.File == fileForReadReceipt);
        Assert.That(item, Is.Not.Null, "no record for file");
        var statusItem = item.Status.SingleOrDefault(i => i.Recipient == sender.Identity);
        Assert.That(statusItem, Is.Not.Null);
        Assert.That(statusItem.Status, Is.EqualTo(SendReadReceiptResultStatus.Enqueued));

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

    private static async Task ConnectRecipientsToSenderAsync(
        OwnerSession sender,
        List<OwnerSession> recipients,
        TargetDrive targetDrive,
        DrivePermission drivePermission,
        Guid senderCircleId)
    {
        foreach (var recipient in recipients)
        {
            // setup the recipients
            await DisableAutoAcceptIntroductionsAsync(recipient);

            //
            // Recipient creates a target drive
            //
            await recipient.Admin.CreateDrive(targetDrive, "Target drive on recipient", allowAnonymousReads: false);

            var recipientCircleId = Guid.NewGuid();
            await recipient.Admin.CreateCircle(recipientCircleId, "Circle with drive access",
                DriveGrant(targetDrive, drivePermission));

            // get connected. The original asserts neither response -- both ClassicAsserts are
            // commented out there; see the fixture remarks.
            await recipient.Connections.SendConnectionRequest(sender.Identity, [recipientCircleId]);
            await sender.Connections.AcceptConnectionRequest(recipient.Identity, [senderCircleId]);
        }
    }

    private static async Task<Guid> PrepareSenderAsync(
        OwnerSession sender, TargetDrive targetDrive, DrivePermission drivePermissions)
    {
        //
        // Sender needs this same drive in order to send across files
        //
        await sender.Admin.CreateDrive(targetDrive, "Target drive on sender", allowAnonymousReads: false);

        //
        // Sender creates a circle with target drive access so recipients can send back a read-receipt
        //
        var senderCircleId = Guid.NewGuid();
        await sender.Admin.CreateCircle(senderCircleId,
            "Circle with drive access for the recipient to send back a read-receipt",
            DriveGrant(targetDrive, drivePermissions));

        return senderCircleId;
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
