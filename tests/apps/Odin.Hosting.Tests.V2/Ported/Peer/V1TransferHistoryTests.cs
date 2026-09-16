using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Storage;
using Odin.Core.Time;
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
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/_Universal/Peer/TransferHistory/TransferHistoryTests.cs
///
/// Frodo sends one encrypted file to Sam, Sam sends a read receipt back, and the sender's transfer
/// history / transfer summary is then read through every surface that exposes it: the file header,
/// QueryBatch, QueryModified, the dedicated V1 history endpoint and the V2 history endpoint.
/// </summary>
/// <remarks>
/// Carries the <c>V1</c> prefix only because <see cref="TransferHistoryTests"/> in this folder
/// already owns the unprefixed name (it ports the <c>_V2/</c> fixture of the same name).
///
/// Port notes:
/// <list type="bullet">
///   <item><description>
///     <c>SetupCallerWithOwner</c> is not used: the drive has to exist on <em>both</em> identities and
///     the two have to be connected before the caller is built, so the fixture creates the drive and
///     runs <see cref="PeerFlow.ConnectAsync"/> itself and then calls <c>spec.Build</c>. Nothing from
///     the original ran between drive-create and caller-build, so the ordering is unchanged.
///   </description></item>
///   <item><description>
///     The original's hand-rolled <c>PrepareScenario</c> also asserted that the recipient's ICR carries
///     the expected circle grant. That was setup validation, not a claim about transfer history;
///     <see cref="PeerFlow.ConnectAsync"/> replaces it and asserts the two connection calls succeeded.
///   </description></item>
///   <item><description>
///     Every V1 <c>WaitForEmptyOutbox</c> / <c>ProcessInbox</c> became
///     <c>Sync.DrainOutboxAsync</c> / <c>Sync.ProcessInboxAsync</c>. The V1 calls are passive polls that
///     depend on the outbox background service, which this host registers but never starts.
///   </description></item>
///   <item><description>
///     The original repeated the send-read-receipt block verbatim in five of its six tests; it is one
///     helper here. <see cref="V2TransferHistoryReturnsReadByRecipientTimestamp"/> had the one shorter
///     copy (it only checked the response was successful), so that case now also asserts the per-recipient
///     <c>Enqueued</c> status the other five already asserted on the same call.
///   </description></item>
/// </list>
/// </remarks>
[TestFixture]
public class V1TransferHistoryTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    public static IEnumerable<object[]> HistoryCases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Secured()), HttpStatusCode.OK];
        yield return
        [
            CallerSpec.App(DriveSpec.Secured(), DrivePermission.ReadWrite, PermissionKeys.All.ToArray()),
            HttpStatusCode.OK
        ];
    }

    /// <summary>The V2 history endpoint case; the original ran it for the owner only.</summary>
    public static IEnumerable<object[]> OwnerHistoryCases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Secured()), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(HistoryCases))]
    public async Task ResendingFileKeepsOriginalRecipientCount(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, sender, recipient, targetDrive) = await PrepareScenarioAsync(spec);

        var transitOptions = new TransitOptions
        {
            Recipients = [recipient.Identity]
        };

        var (uploadResult, recipientFile, originalKeyHeader, originalUploadFileMetadata) =
            await TransferEncryptedMetadataAsync(sender, recipient, targetDrive, transitOptions);

        await SendReadReceiptAsync(sender, recipient, recipientFile, targetDrive);

        // Validate the original recipient is set on the first upload
        var uploadedFileResponse1 = await sender.V1.Drive.GetFileHeader(uploadResult.File);
        Assert.That(uploadedFileResponse1.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var uploadedFile1 = uploadedFileResponse1.Content;

        Assert.That(uploadedFile1.ServerMetadata.OriginalRecipientCount, Is.EqualTo(transitOptions.Recipients.Count));

        //
        // now resend the file
        //
        var newKeyHeader = new KeyHeader
        {
            Iv = ByteArrayUtil.GetRndByteArray(16),
            AesKey = new SensitiveByteArray(originalKeyHeader.AesKey.GetKey())
        };

        originalUploadFileMetadata.VersionTag = uploadResult.NewVersionTag;
        originalUploadFileMetadata.IsEncrypted = true;

        var (updateFileResponse, _) = await sender.V1.Drive.UpdateExistingEncryptedMetadata(uploadResult.File,
            newKeyHeader,
            originalUploadFileMetadata);
        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        //
        // Assert: recipient count is still set
        //
        var updatedFileResponse = await caller.V1.Drive.GetFileHeader(uploadResult.File);
        Assert.That(updatedFileResponse.StatusCode, Is.EqualTo(expected));
        var updatedFile = updatedFileResponse.Content;
        Assert.That(updatedFile.ServerMetadata.OriginalRecipientCount, Is.EqualTo(transitOptions.Recipients.Count));
    }

    [Test, TestCaseSource(nameof(OwnerHistoryCases))]
    public async Task V2TransferHistoryReturnsReadByRecipientTimestamp(CallerSpec spec, HttpStatusCode expected)
    {
        var (_, sender, recipient, targetDrive) = await PrepareScenarioAsync(spec);

        var transitOptions = new TransitOptions
        {
            Recipients = [recipient.Identity]
        };

        var (uploadResult, recipientFile, _, _) =
            await TransferEncryptedMetadataAsync(sender, recipient, targetDrive, transitOptions);

        // Before read receipt: V2 should return null
        var beforeHistory = await sender.Drives.Reader.GetTransferHistoryAsync(targetDrive.Alias, uploadResult.File.FileId);
        Assert.That(beforeHistory.StatusCode, Is.EqualTo(expected));
        var beforeItem = beforeHistory.Content.GetHistoryItem(recipient.Identity);
        Assert.That(beforeItem, Is.Not.Null);
        Assert.That(beforeItem.ReadByRecipientTimestamp, Is.Null, "V2 should return null before read receipt");

        var beforeTs = UnixTimeUtc.Now().milliseconds;
        await SendReadReceiptAsync(sender, recipient, recipientFile, targetDrive);
        var afterTs = UnixTimeUtc.Now().milliseconds;

        // After read receipt: V2 should return a positive timestamp
        var afterHistory = await sender.Drives.Reader.GetTransferHistoryAsync(targetDrive.Alias, uploadResult.File.FileId);
        Assert.That(afterHistory.StatusCode, Is.EqualTo(expected));
        var afterItem = afterHistory.Content.GetHistoryItem(recipient.Identity);
        Assert.That(afterItem, Is.Not.Null);
        Assert.That(afterItem.ReadByRecipientTimestamp, Is.Not.Null, "V2 should return a timestamp after read receipt");
        Assert.That(afterItem.ReadByRecipientTimestamp.Value, Is.GreaterThan(0));
        Assert.That(afterItem.ReadByRecipientTimestamp.Value, Is.GreaterThanOrEqualTo(beforeTs));
        Assert.That(afterItem.ReadByRecipientTimestamp.Value, Is.LessThanOrEqualTo(afterTs));
    }

    [Test, TestCaseSource(nameof(HistoryCases))]
    public async Task CanReadTransferSummaryFromFile(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, sender, recipient, targetDrive) = await PrepareScenarioAsync(spec);

        var transitOptions = new TransitOptions
        {
            Recipients = [recipient.Identity]
        };

        var (uploadResult, recipientFile, _, _) =
            await TransferEncryptedMetadataAsync(sender, recipient, targetDrive, transitOptions);

        await SendReadReceiptAsync(sender, recipient, recipientFile, targetDrive);

        //
        // Assert: the sender has the transfer history updated
        //
        var uploadedFileResponse1 = await caller.V1.Drive.GetFileHeader(uploadResult.File);
        Assert.That(uploadedFileResponse1.StatusCode, Is.EqualTo(expected));
        var uploadedFile1 = uploadedFileResponse1.Content;

        Assert.That(uploadedFile1.ServerMetadata.OriginalRecipientCount, Is.EqualTo(transitOptions.Recipients.Count));

        var summary = uploadedFile1.ServerMetadata.TransferHistory.Summary;
        Assert.That(summary, Is.Not.Null, "missing transfer summary");
        Assert.That(summary.TotalDelivered, Is.EqualTo(1));
        Assert.That(summary.TotalReadByRecipient, Is.EqualTo(1));
        Assert.That(summary.TotalFailed, Is.EqualTo(0));
        Assert.That(summary.TotalInOutbox, Is.EqualTo(0));
    }

    [Test, TestCaseSource(nameof(HistoryCases))]
    public async Task CanReadTransferSummaryFromQueryBatch(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, sender, recipient, targetDrive) = await PrepareScenarioAsync(spec);

        var transitOptions = new TransitOptions
        {
            Recipients = [recipient.Identity]
        };

        var (_, recipientFile, _, _) =
            await TransferEncryptedMetadataAsync(sender, recipient, targetDrive, transitOptions);

        await SendReadReceiptAsync(sender, recipient, recipientFile, targetDrive);

        //
        // Assert: the sender has the transfer history updated
        //
        var q = new QueryBatchRequest
        {
            QueryParams = new()
            {
                TargetDrive = targetDrive,
                FileState = [FileState.Active]
            },
            ResultOptionsRequest = new()
            {
                MaxRecords = 10,
                IncludeMetadataHeader = true,
                IncludeTransferHistory = true
            }
        };

        var uploadedFileResponse1 = await caller.V1.Drive.QueryBatch(q);
        Assert.That(uploadedFileResponse1.StatusCode, Is.EqualTo(expected));
        var uploadedFile1 = uploadedFileResponse1.Content.SearchResults.FirstOrDefault();
        Assert.That(uploadedFile1, Is.Not.Null);

        var summary = uploadedFile1.ServerMetadata.TransferHistory.Summary;
        Assert.That(summary, Is.Not.Null, "missing transfer summary");
        Assert.That(summary.TotalDelivered, Is.EqualTo(1));
        Assert.That(summary.TotalReadByRecipient, Is.EqualTo(1));
        Assert.That(summary.TotalFailed, Is.EqualTo(0));
        Assert.That(summary.TotalInOutbox, Is.EqualTo(0));
    }

    [Test, TestCaseSource(nameof(HistoryCases))]
    public async Task CanReadTransferSummaryFromQueryModified(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, sender, recipient, targetDrive) = await PrepareScenarioAsync(spec);

        var transitOptions = new TransitOptions
        {
            Recipients = [recipient.Identity]
        };

        var (_, recipientFile, _, _) =
            await TransferEncryptedMetadataAsync(sender, recipient, targetDrive, transitOptions);

        await SendReadReceiptAsync(sender, recipient, recipientFile, targetDrive);

        //
        // Assert: the sender has the transfer history updated
        //
        var q = new QueryModifiedRequest
        {
            QueryParams = new()
            {
                TargetDrive = targetDrive,
                FileState = [FileState.Active]
            },
            ResultOptions = new()
            {
                MaxRecords = 10,
                MaxDate = UnixTimeUtc.Now().AddDays(1).milliseconds,
                IncludeTransferHistory = true
            }
        };

        await Task.Delay(5);

        var uploadedFileResponse1 = await caller.V1.Drive.QueryModified(q);
        Assert.That(uploadedFileResponse1.StatusCode, Is.EqualTo(expected));
        var uploadedFile1 = uploadedFileResponse1.Content.SearchResults.FirstOrDefault();
        Assert.That(uploadedFile1, Is.Not.Null);

        var summary = uploadedFile1.ServerMetadata.TransferHistory.Summary;
        Assert.That(summary, Is.Not.Null, "missing transfer summary");
        Assert.That(summary.TotalDelivered, Is.EqualTo(1));
        Assert.That(summary.TotalReadByRecipient, Is.EqualTo(1));
        Assert.That(summary.TotalFailed, Is.EqualTo(0));
        Assert.That(summary.TotalInOutbox, Is.EqualTo(0));
    }

    [Test, TestCaseSource(nameof(HistoryCases))]
    public async Task CanReadTransferHistoryForFile(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, sender, recipient, targetDrive) = await PrepareScenarioAsync(spec);

        var transitOptions = new TransitOptions
        {
            Recipients = [recipient.Identity]
        };

        var (uploadResult, recipientFile, _, _) =
            await TransferEncryptedMetadataAsync(sender, recipient, targetDrive, transitOptions);

        await SendReadReceiptAsync(sender, recipient, recipientFile, targetDrive);

        //
        // Assert: the sender has the transfer history updated
        //
        var historyResponse = await caller.V1.Drive.GetTransferHistory(uploadResult.File);
        Assert.That(historyResponse.StatusCode, Is.EqualTo(expected));

        foreach (var recipientDomain in transitOptions.Recipients)
        {
            var theHistory = historyResponse.Content;
            Assert.That(theHistory.OriginalRecipientCount, Is.EqualTo(transitOptions.Recipients.Count));
            Assert.That(theHistory.History.Results.Count, Is.EqualTo(1));
            var recipientStatus = theHistory.History.Results.SingleOrDefault(r => r.Recipient == recipientDomain);
            Assert.That(recipientStatus, Is.Not.Null, "There should be a status update for the recipient");
            Assert.That(recipientStatus.IsReadByRecipient, Is.True);
            Assert.That(recipientStatus.LatestTransferStatus, Is.EqualTo(LatestTransferStatus.Delivered));
            Assert.That(recipientStatus.LatestSuccessfullyDeliveredVersionTag, Is.EqualTo(uploadResult.NewVersionTag));
        }
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Replaces the original's <c>PrepareScenario</c>: the same drive on both identities, connected
    /// with Write in both directions. The reverse grant is what lets Sam's read receipt land — it
    /// hits <c>AssertCanWriteToDrive</c> on Frodo's drive.
    /// </summary>
    private async Task<(IV2Caller Caller, OwnerSession Sender, OwnerSession Recipient, TargetDrive Drive)>
        PrepareScenarioAsync(CallerSpec spec)
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var targetDrive = spec.TargetDrive;
        await recipient.Admin.CreateDrive(targetDrive, "Target drive on recipient", allowAnonymousReads: false);
        await sender.Admin.CreateDrive(targetDrive, "Target drive on sender", allowAnonymousReads: false);

        await PeerFlow.ConnectAsync(sender, recipient, targetDrive, DrivePermission.Write, bidirectional: true);

        var caller = await spec.Build(sender);
        return (caller, sender, recipient, targetDrive);
    }

    private static async Task<(
            UploadResult uploadResult,
            SharedSecretEncryptedFileHeader recipientFile,
            KeyHeader originalKeyHeader,
            UploadFileMetadata uploadFileMetadata)>
        TransferEncryptedMetadataAsync(
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

        var keyHeader = KeyHeader.NewRandom16();
        var (uploadResponse, _) = await sender.V1.Drive.UploadNewEncryptedMetadata(
            fileMetadata,
            storageOptions,
            transitOptions,
            keyHeader);

        Assert.That(uploadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var uploadResult = uploadResponse.Content;
        Assert.That(uploadResult.RecipientStatus.Count, Is.EqualTo(1));
        Assert.That(uploadResult.RecipientStatus[transitOptions.Recipients.Single()], Is.EqualTo(TransferStatus.Enqueued));

        await sender.Sync.DrainOutboxAsync();

        // validate recipient got the file
        await recipient.Sync.ProcessInboxAsync(storageOptions.Drive);
        var recipientFileResponse = await recipient.V1.Drive.QueryByGlobalTransitId(uploadResult.GlobalTransitIdFileIdentifier);
        Assert.That(recipientFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var file = recipientFileResponse.Content.SearchResults.SingleOrDefault();
        Assert.That(file, Is.Not.Null);

        return (uploadResult, file, keyHeader, fileMetadata);
    }

    /// <summary>
    /// The recipient marks the file read and the receipt is carried all the way back onto the
    /// sender's transfer history: enqueue, drain the recipient's outbox, process the sender's inbox.
    /// </summary>
    private static async Task SendReadReceiptAsync(
        OwnerSession sender,
        OwnerSession recipient,
        SharedSecretEncryptedFileHeader recipientFile,
        TargetDrive targetDrive)
    {
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

        await recipient.Sync.DrainOutboxAsync();
        await sender.Sync.ProcessInboxAsync(targetDrive); // process all read receipts
    }
}
