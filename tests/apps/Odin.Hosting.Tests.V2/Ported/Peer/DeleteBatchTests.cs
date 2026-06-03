using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Port of <c>_V2/Tests/Drive/WriteFileTests/DeleteFileIdBatchPeerTestsV2</c>. Covers the
/// peer-fan-out path of V2 <c>delete-batch/by-file-id</c>: distributing a file, soft-deleting via
/// the V2 batch endpoint with one or more <see cref="DeleteFileRequestV2.Recipients"/>, and
/// verifying the recipient's local copy flips to <see cref="FileState.Deleted"/>. The original
/// suite also covers App/Guest callers, disconnected-recipient error paths, and a series of
/// <c>InboxDrainOnQuery</c> background-drain scenarios — those depend on production behaviours
/// (auto-drain via background services, V1 Disconnect helper) that the in-process framework
/// deliberately doesn't run. They stay on the V1 framework; see SUPERSEDED banner on the original.
/// </summary>
[TestFixture]
public class DeleteBatchTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Merry];

    [Test]
    public async Task DeleteFileIdBatch_WithSingleRecipient_PropagatesDeleteToRecipient()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, DrivePermission.Write, "delete-batch single");

        var u1 = await UploadAndDistribute(sender, recipient, drive, fileType: 401);
        var u2 = await UploadAndDistribute(sender, recipient, drive, fileType: 402);

        await AssertRecipientFileState(recipient, drive, u1.gtid, FileState.Active);
        await AssertRecipientFileState(recipient, drive, u2.gtid, FileState.Active);

        var deleteResponse = await sender.Drives.Writer.DeleteFileList(drive.Alias, new List<DeleteFileRequestV2>
        {
            new() { FileId = u1.fileId, Recipients = [recipient.Identity] },
            new() { FileId = u2.fileId, Recipients = [recipient.Identity] }
        });
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var batch = deleteResponse.Content!;
        Assert.That(batch.Results.Count, Is.EqualTo(2));
        foreach (var result in batch.Results)
        {
            Assert.That(result.LocalFileDeleted, Is.True);
            Assert.That(result.RecipientStatus[recipient.Identity], Is.EqualTo(DeleteLinkedFileStatus.Enqueued));
        }

        await sender.Sync.DrainOutboxAsync();
        await recipient.Sync.ProcessInboxAsync(drive);

        await AssertRecipientFileState(recipient, drive, u1.gtid, FileState.Deleted);
        await AssertRecipientFileState(recipient, drive, u2.gtid, FileState.Deleted);
    }

    [Test]
    public async Task DeleteFileIdBatch_WithMultipleConnectedRecipients_PropagatesToAll()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipientA = await LoginAsOwner(Identities.Sam);
        var recipientB = await LoginAsOwner(Identities.Merry);

        var drive = TargetDrive.NewTargetDrive();
        await sender.Admin.CreateDrive(drive, $"{sender.Identity} multi-recipient");
        await recipientA.Admin.CreateDrive(drive, $"{recipientA.Identity} multi-recipient");
        await recipientB.Admin.CreateDrive(drive, $"{recipientB.Identity} multi-recipient");
        await PeerFlow.ConnectAsync(sender, recipientA, drive, DrivePermission.Write);
        await PeerFlow.ConnectAsync(sender, recipientB, drive, DrivePermission.Write);

        var upload = await UploadAndDistributeToMany(sender, [recipientA, recipientB], drive, fileType: 420);

        await AssertRecipientFileState(recipientA, drive, upload.gtid, FileState.Active);
        await AssertRecipientFileState(recipientB, drive, upload.gtid, FileState.Active);

        var deleteResponse = await sender.Drives.Writer.DeleteFileList(drive.Alias, new List<DeleteFileRequestV2>
        {
            new()
            {
                FileId = upload.fileId,
                Recipients = [recipientA.Identity, recipientB.Identity]
            }
        });
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var single = deleteResponse.Content!.Results.Single();
        Assert.That(single.LocalFileDeleted, Is.True);
        Assert.That(single.RecipientStatus.Count, Is.EqualTo(2));
        Assert.That(single.RecipientStatus[recipientA.Identity], Is.EqualTo(DeleteLinkedFileStatus.Enqueued));
        Assert.That(single.RecipientStatus[recipientB.Identity], Is.EqualTo(DeleteLinkedFileStatus.Enqueued));

        await sender.Sync.DrainOutboxAsync();
        await recipientA.Sync.ProcessInboxAsync(drive);
        await recipientB.Sync.ProcessInboxAsync(drive);

        await AssertRecipientFileState(recipientA, drive, upload.gtid, FileState.Deleted);
        await AssertRecipientFileState(recipientB, drive, upload.gtid, FileState.Deleted);
    }

    [Test]
    public async Task DeleteFileIdBatch_MixedRecipients_OnlyFansOutForEntriesThatRequestIt()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, DrivePermission.Write, "mixed-recipients");

        var distributed = await UploadAndDistribute(sender, recipient, drive, fileType: 431);
        var alsoDistributed = await UploadAndDistribute(sender, recipient, drive, fileType: 432);

        await AssertRecipientFileState(recipient, drive, distributed.gtid, FileState.Active);
        await AssertRecipientFileState(recipient, drive, alsoDistributed.gtid, FileState.Active);

        var deleteResponse = await sender.Drives.Writer.DeleteFileList(drive.Alias, new List<DeleteFileRequestV2>
        {
            new() { FileId = distributed.fileId,     Recipients = [recipient.Identity] },
            new() { FileId = alsoDistributed.fileId, Recipients = [] }
        });
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var results = deleteResponse.Content!.Results;
        Assert.That(results.Count, Is.EqualTo(2));
        var fanOut = results.Single(r => r.FileId == distributed.fileId);
        var noFanOut = results.Single(r => r.FileId == alsoDistributed.fileId);

        Assert.That(fanOut.LocalFileDeleted, Is.True);
        Assert.That(fanOut.RecipientStatus[recipient.Identity], Is.EqualTo(DeleteLinkedFileStatus.Enqueued));

        Assert.That(noFanOut.LocalFileDeleted, Is.True);
        Assert.That(noFanOut.RecipientStatus.Any(), Is.False, "entry without recipients must not enqueue any peer delete");

        await sender.Sync.DrainOutboxAsync();
        await recipient.Sync.ProcessInboxAsync(drive);

        await AssertRecipientFileState(recipient, drive, distributed.gtid, FileState.Deleted);
        await AssertRecipientFileState(recipient, drive, alsoDistributed.gtid, FileState.Active);
    }

    [Test]
    public async Task DeleteFileIdBatch_FileWithPayloadsAndThumbnails_RecipientPayloadsRemoved()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, DrivePermission.Write, "delete-with-payloads");

        var metadata = SampleMetadataData.Create(fileType: 450, acl: AccessControlList.Connected, allowDistribution: true);
        var payloads = new List<TestPayloadDefinition>
        {
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1(),
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2()
        };
        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };
        var transit = new TransitOptions
        {
            Recipients = new List<string> { recipient.Identity },
            Priority = OutboxPriority.High
        };

        var uploadResponse = await sender.Drives.Writer.CreateNewUnencryptedFile(drive.Alias, metadata, manifest, payloads, transit);
        Assert.That(uploadResponse.IsSuccessStatusCode, Is.True);
        var upload = uploadResponse.Content!;
        var gtid = upload.GlobalTransitId
            ?? throw new InvalidOperationException("upload to a recipient must yield a GlobalTransitId");

        await sender.Sync.DrainOutboxAsync();
        await recipient.Sync.ProcessInboxAsync(drive);

        // Resolve the recipient's copy (different FileId, same GTID).
        var recipientFileId = await GetRecipientFileId(recipient, drive, gtid);

        // Sanity: recipient can fetch each payload before the delete.
        foreach (var p in payloads)
        {
            var resp = await recipient.Drives.Reader.GetPayloadAsync(drive.Alias, recipientFileId, p.Key);
            Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"recipient should have payload {p.Key}");
        }

        var deleteResponse = await sender.Drives.Writer.DeleteFileList(drive.Alias, new List<DeleteFileRequestV2>
        {
            new() { FileId = upload.FileId, Recipients = [recipient.Identity] }
        });
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(deleteResponse.Content!.Results.Single().RecipientStatus[recipient.Identity],
            Is.EqualTo(DeleteLinkedFileStatus.Enqueued));

        await sender.Sync.DrainOutboxAsync();
        await recipient.Sync.ProcessInboxAsync(drive);

        await AssertRecipientFileState(recipient, drive, gtid, FileState.Deleted);

        foreach (var p in payloads)
        {
            var resp = await recipient.Drives.Reader.GetPayloadAsync(drive.Alias, recipientFileId, p.Key);
            Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
                $"recipient payload {p.Key} should be gone after peer delete");

            foreach (var thumb in p.Thumbnails)
            {
                var thumbResp = await recipient.Drives.Reader.GetThumbnailAsync(
                    drive.Alias, recipientFileId, p.Key, thumb.PixelWidth, thumb.PixelHeight);
                Assert.That(thumbResp.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
                    $"recipient thumbnail {p.Key}/{thumb.PixelWidth}x{thumb.PixelHeight} should be gone");
            }
        }
    }

    [Test]
    public async Task DeleteFileIdBatch_FileAlreadySoftDeleted_DoesNotThrowAndReturnsResult()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, DrivePermission.Write, "already-deleted");

        var upload = await UploadAndDistribute(sender, recipient, drive, fileType: 460);
        await AssertRecipientFileState(recipient, drive, upload.gtid, FileState.Active);

        // First call: no recipients — local file is soft-deleted but peer isn't notified.
        var first = await sender.Drives.Writer.DeleteFileList(drive.Alias, new List<DeleteFileRequestV2>
        {
            new() { FileId = upload.fileId, Recipients = [] }
        });
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(first.Content!.Results.Single().LocalFileDeleted, Is.True);

        // Second call against the same fileId, this time with the recipient. Must not throw.
        var second = await sender.Drives.Writer.DeleteFileList(drive.Alias, new List<DeleteFileRequestV2>
        {
            new() { FileId = upload.fileId, Recipients = [recipient.Identity] }
        });
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            $"second batch on already-soft-deleted file should not error; got {second.StatusCode}");

        var result = second.Content!.Results.Single();
        Assert.That(result.LocalFileDeleted || result.LocalFileNotFound, Is.True,
            "result must report at least one of LocalFileDeleted or LocalFileNotFound");
        if (result.LocalFileDeleted)
        {
            Assert.That(result.RecipientStatus.ContainsKey(recipient.Identity), Is.True,
                "if header still found, peer fan-out should still be enqueued");
        }
    }

    // -----------------------------------------------------------------------------------------
    // helpers
    // -----------------------------------------------------------------------------------------

    private static async Task<(Guid fileId, Guid gtid)> UploadAndDistribute(
        OwnerSession sender, OwnerSession recipient, TargetDrive drive, int fileType)
    {
        var metadata = SampleMetadataData.Create(fileType: fileType, acl: AccessControlList.Connected, allowDistribution: true);
        var transit = new TransitOptions
        {
            Recipients = new List<string> { recipient.Identity },
            Priority = OutboxPriority.High
        };

        var response = await sender.Drives.Writer.UploadNewMetadata(drive.Alias, metadata, transit);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"upload failed: {response.StatusCode}");
        var gtid = response.Content!.GlobalTransitId
            ?? throw new InvalidOperationException("distribution upload must yield a GlobalTransitId");

        await sender.Sync.DrainOutboxAsync();
        await recipient.Sync.ProcessInboxAsync(drive);
        return (response.Content.FileId, gtid);
    }

    private static async Task<(Guid fileId, Guid gtid)> UploadAndDistributeToMany(
        OwnerSession sender, IReadOnlyList<OwnerSession> recipients, TargetDrive drive, int fileType)
    {
        var metadata = SampleMetadataData.Create(fileType: fileType, acl: AccessControlList.Connected, allowDistribution: true);
        var transit = new TransitOptions
        {
            Recipients = recipients.Select(r => (string)r.Identity).ToList(),
            Priority = OutboxPriority.High
        };

        var response = await sender.Drives.Writer.UploadNewMetadata(drive.Alias, metadata, transit);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"upload failed: {response.StatusCode}");
        var gtid = response.Content!.GlobalTransitId
            ?? throw new InvalidOperationException("distribution upload must yield a GlobalTransitId");

        await sender.Sync.DrainOutboxAsync();
        foreach (var r in recipients)
        {
            await r.Sync.ProcessInboxAsync(drive);
        }
        return (response.Content.FileId, gtid);
    }

    private static async Task AssertRecipientFileState(OwnerSession recipient, TargetDrive drive, Guid gtid, FileState expected)
    {
        var hit = await QueryByGtidSingle(recipient, drive, gtid);
        Assert.That(hit!.FileState, Is.EqualTo(expected),
            $"recipient's file state for GTID {gtid} should be {expected} but was {hit.FileState}");
    }

    private static async Task<Guid> GetRecipientFileId(OwnerSession recipient, TargetDrive drive, Guid gtid)
    {
        var hit = await QueryByGtidSingle(recipient, drive, gtid);
        return hit!.FileId;
    }

    private static async Task<dynamic> QueryByGtidSingle(OwnerSession recipient, TargetDrive drive, Guid gtid)
    {
        var query = await recipient.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { GlobalTransitId = new[] { gtid } },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                MaxRecords = 10,
                IncludeMetadataHeader = true
            }
        });
        Assert.That(query.IsSuccessStatusCode, Is.True);
        var hit = query.Content!.SearchResults.SingleOrDefault();
        Assert.That(hit, Is.Not.Null, $"recipient should have a copy of GTID {gtid}");
        return hit;
    }
}
