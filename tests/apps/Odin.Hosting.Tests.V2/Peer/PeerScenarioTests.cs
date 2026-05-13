#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Peer;

/// <summary>
/// Peer flows beyond the canonical Frodo→Sam transfer (see <see cref="FrodoToSamPeerTransferTests"/>):
/// encrypted-payload decryption with the recipient's shared secret, soft-delete propagation,
/// read-receipt round-trip, and multi-recipient distribution. Each test exercises a different
/// production path on top of the same in-process peer routing seam.
/// </summary>
[TestFixture]
public class PeerScenarioTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Pippin];

    [Test]
    public async Task EncryptedFile_RecipientDecryptsWithItsOwnSharedSecret()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Write, "encrypted");

        var plaintext = "the precious is heavy today";
        var metadata = SampleMetadataData.Create(fileType: 200, acl: AccessControlList.Connected);
        metadata.AppData.Content = plaintext;
        metadata.AllowDistribution = true;

        var keyHeader = KeyHeader.NewRandom16();
        var (send, encryptedJson64, _, _) = await frodo.Drives.Writer.CreateEncryptedFile(
            drive.Alias,
            metadata,
            new TransitOptions { Recipients = new List<string> { sam.Identity } },
            keyHeader: keyHeader);

        Assert.That(send.IsSuccessStatusCode, Is.True, $"encrypted upload failed: {send.StatusCode}");
        var gtid = send.Content!.GlobalTransitId
            ?? throw new InvalidOperationException("encrypted peer upload must yield a GlobalTransitId");

        await frodo.Sync.DrainOutboxAsync();
        await sam.Sync.ProcessInboxAsync(drive);

        var samFile = await FindLocalByGtidAsync(sam, drive, gtid);
        var samHeader = await sam.Drives.Reader.GetFileHeaderAsync(drive.Alias, samFile);
        Assert.That(samHeader.IsSuccessStatusCode, Is.True);

        var header = samHeader.Content!;
        Assert.That(header.FileMetadata.IsEncrypted, Is.True,
            "encrypted file must arrive flagged as encrypted on the recipient side");
        Assert.That(header.FileMetadata.AppData.Content, Is.EqualTo(encryptedJson64),
            "ciphertext bytes round-trip unchanged across the wire");

        // Decrypt with Sam's own shared secret — proves the peer transit re-encrypted the key
        // header for the recipient rather than leaking the sender's local form.
        var samSecret = sam.SharedSecret;
        var recoveredKey = header.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref samSecret);
        var decrypted = recoveredKey.Decrypt(header.FileMetadata.AppData.Content.FromBase64()).ToStringFromUtf8Bytes();
        Assert.That(decrypted, Is.EqualTo(plaintext));
    }

    [Test]
    public async Task SoftDeleteWithRecipients_PropagatesDeleteToRecipient()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Write, "delete-prop");

        var (frodoFile, gtid) = await UploadAndDeliverAsync(frodo, sam, drive, fileType: 300);

        var samFile = await FindLocalByGtidAsync(sam, drive, gtid);

        var delete = await frodo.Drives.Writer.SoftDeleteFile(drive.Alias, frodoFile,
            recipients: new List<string> { sam.Identity });
        Assert.That(delete.IsSuccessStatusCode, Is.True, $"delete failed: {delete.StatusCode}");
        Assert.That(delete.Content!.LocalFileDeleted, Is.True, "local soft-delete should succeed");

        await frodo.Sync.DrainOutboxAsync();
        await sam.Sync.ProcessInboxAsync(drive);

        var samHeader = await sam.Drives.Reader.GetFileHeaderAsync(drive.Alias, samFile);
        Assert.That(samHeader.IsSuccessStatusCode, Is.True,
            "soft-deleted files keep returning a header (state, not absence)");
        Assert.That(samHeader.Content!.FileState, Is.EqualTo(FileState.Deleted),
            "Sam's local copy should be marked Deleted after inbox processing");
    }

    [Test]
    public async Task ReadReceipt_RoundTrip_SenderSeesReadTimestamp()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        // Bi-directional Write: Sam's read receipt back to Frodo lands at
        // PeerDriveIncomingTransferService.MarkFileAsReadAsync, which AssertCanWriteToDrive on
        // Frodo's local drive — Sam needs Write on Frodo's drive too, not just the other direction.
        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "frodo receipts");
        await sam.Admin.CreateDrive(drive, "sam receipts");
        await PeerFlow.ConnectBidirectionalAsync(frodo, sam, drive, DrivePermission.Write);

        var (frodoFile, gtid) = await UploadAndDeliverAsync(frodo, sam, drive, fileType: 400);

        // Pre-receipt: history shows Delivered but ReadByRecipientTimestamp is null.
        var preHistory = await frodo.Drives.Reader.GetTransferHistoryAsync(drive.Alias, frodoFile);
        Assert.That(preHistory.IsSuccessStatusCode, Is.True);
        var preItem = preHistory.Content!.GetHistoryItem(sam.Identity);
        Assert.That(preItem, Is.Not.Null);
        Assert.That(preItem.ReadByRecipientTimestamp, Is.Null,
            "before receipt is sent, ReadByRecipientTimestamp must be null");

        // Sam sends the read receipt for his local copy.
        var samFile = await FindLocalByGtidAsync(sam, drive, gtid);
        var receipt = await sam.Drives.Writer.SendReadReceipt(drive.Alias, new List<Guid> { samFile });
        Assert.That(receipt.IsSuccessStatusCode, Is.True, $"SendReadReceipt failed: {receipt.StatusCode}");

        await sam.Sync.DrainOutboxAsync();
        await frodo.Sync.ProcessInboxAsync(drive);

        var postHistory = await frodo.Drives.Reader.GetTransferHistoryAsync(drive.Alias, frodoFile);
        Assert.That(postHistory.IsSuccessStatusCode, Is.True);
        var postItem = postHistory.Content!.GetHistoryItem(sam.Identity);
        Assert.That(postItem, Is.Not.Null);
        Assert.That(postItem.ReadByRecipientTimestamp, Is.Not.Null,
            "after receipt round-trip, ReadByRecipientTimestamp should be populated on the sender side");
        Assert.That(postItem.ReadByRecipientTimestamp!.Value, Is.GreaterThan(0));
    }

    [Test]
    public async Task MultiRecipient_BothReceiversGetTheFile()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var pippin = await LoginAsOwner(Identities.Pippin);

        // Each recipient owns its own circle granting Write on the shared drive. Sam and Pippin are
        // independent peers from Frodo's perspective — connecting twice exercises that the in-process
        // peer routing isn't smuggling state via shared globals.
        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "Frodo multi-send");
        await sam.Admin.CreateDrive(drive, "Sam multi-recv");
        await pippin.Admin.CreateDrive(drive, "Pippin multi-recv");
        await PeerFlow.ConnectAsync(frodo, sam, drive, DrivePermission.Write);
        await PeerFlow.ConnectAsync(frodo, pippin, drive, DrivePermission.Write);

        var metadata = SampleMetadataData.Create(fileType: 500, acl: AccessControlList.Connected);
        metadata.AllowDistribution = true;
        metadata.AppData.Content = "fellowship-broadcast";

        var send = await frodo.Drives.Writer.UploadNewMetadata(
            drive.Alias,
            metadata,
            transitOptions: new TransitOptions
            {
                Recipients = new List<string> { sam.Identity, pippin.Identity }
            });
        Assert.That(send.IsSuccessStatusCode, Is.True, $"multi-recipient upload failed: {send.StatusCode}");
        var gtid = send.Content!.GlobalTransitId
            ?? throw new InvalidOperationException("multi-recipient upload must yield a GlobalTransitId");

        await frodo.Sync.DrainOutboxAsync();
        await sam.Sync.ProcessInboxAsync(drive);
        await pippin.Sync.ProcessInboxAsync(drive);

        await AssertRecipientGotFileAsync(sam, drive, gtid, metadata.AppData.Content);
        await AssertRecipientGotFileAsync(pippin, drive, gtid, metadata.AppData.Content);
    }

    // ---------------------------------------------------------------------------------------------

    private static async Task<(Guid senderFileId, Guid gtid)> UploadAndDeliverAsync(
        OwnerSession sender, OwnerSession recipient, TargetDrive drive, int fileType)
    {
        var metadata = SampleMetadataData.Create(fileType: fileType, acl: AccessControlList.Connected);
        metadata.AllowDistribution = true;

        var send = await sender.Drives.Writer.UploadNewMetadata(
            drive.Alias,
            metadata,
            transitOptions: new TransitOptions { Recipients = new List<string> { recipient.Identity } });
        Assert.That(send.IsSuccessStatusCode, Is.True, $"upload failed: {send.StatusCode}");
        var result = send.Content!;
        var gtid = result.GlobalTransitId
            ?? throw new InvalidOperationException("upload to a recipient must yield a GlobalTransitId");

        await sender.Sync.DrainOutboxAsync();
        await recipient.Sync.ProcessInboxAsync(drive);

        return (result.FileId, gtid);
    }

    private static async Task<Guid> FindLocalByGtidAsync(OwnerSession owner, TargetDrive drive, Guid gtid)
    {
        var q = await owner.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { GlobalTransitId = new[] { gtid } },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                MaxRecords = 10,
                IncludeMetadataHeader = true,
            },
        });
        Assert.That(q.IsSuccessStatusCode, Is.True);
        var hits = q.Content!.SearchResults.ToList();
        Assert.That(hits.Count, Is.EqualTo(1), $"expected exactly one local file for GTID {gtid} on {owner.Identity}");
        return hits[0].FileId;
    }

    private static async Task AssertRecipientGotFileAsync(
        OwnerSession recipient, TargetDrive drive, Guid gtid, string expectedContent)
    {
        var q = await recipient.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { GlobalTransitId = new[] { gtid } },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                MaxRecords = 10,
                IncludeMetadataHeader = true,
            },
        });
        Assert.That(q.IsSuccessStatusCode, Is.True, $"{recipient.Identity} query failed: {q.StatusCode}");
        var hits = q.Content!.SearchResults.ToList();
        Assert.That(hits.Count, Is.EqualTo(1), $"{recipient.Identity} should have one local file matching the GTID");
        Assert.That(hits[0].FileMetadata.AppData.Content, Is.EqualTo(expectedContent));
    }
}
