using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using static Odin.Hosting.Tests.V2.DriveWrite.SoftDeleteBatchTests;

namespace Odin.Hosting.Tests.V2.Peer;

/// <summary>
/// <c>POST files/soft-delete-batch</c> with recipients: each deleted file's recipients are told to delete their copies; a
/// refused file tells nobody.
/// </summary>
[TestFixture]
public class SoftDeleteBatchPeerTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Merry];

    [Test]
    public async Task OnlyTheDeletedFilesRecipientsAreTold()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, DrivePermission.Write, "soft-delete-batch");
        var deleted = await UploadAndDistribute(sender, recipient, drive, fileType: 501);
        var stale = await UploadAndDistribute(sender, recipient, drive, fileType: 502);

        var response = await sender.Drives.Writer.SoftDeleteBatch(drive.Alias, Batch(
            File(deleted.FileId, deleted.VersionTag, [recipient.Identity]),
            File(stale.FileId, Guid.NewGuid(), [recipient.Identity])));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        var results = response.Content!.Results;
        Assert.That(results.Select(r => (r.FileId, r.ErrorCode, Status(r.RecipientStatus))), Is.EqualTo(new[]
        {
            (deleted.FileId, (OdinClientErrorCode?)null, $"{recipient.Identity}=Enqueued"),
            (stale.FileId, OdinClientErrorCode.VersionTagMismatch, "")
        }));

        await sender.Sync.DrainOutboxAsync();
        await recipient.Sync.ProcessInboxAsync(drive);
        Assert.That((await RecipientStateAsync(recipient, drive, deleted.Gtid), await RecipientStateAsync(recipient, drive, stale.Gtid)),
            Is.EqualTo((FileState.Deleted, FileState.Active)));
    }

    [Test]
    public async Task ARecipientNoLongerConnectedIsReportedAndTheFileIsStillDeleted()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var connected = await LoginAsOwner(Identities.Sam);
        var gone = await LoginAsOwner(Identities.Merry);
        var drive = TargetDrive.NewTargetDrive();
        await sender.Admin.CreateDrive(drive, $"{sender.Identity} soft-delete-batch");
        await connected.Admin.CreateDrive(drive, $"{connected.Identity} soft-delete-batch");
        await gone.Admin.CreateDrive(drive, $"{gone.Identity} soft-delete-batch");
        await PeerFlow.ConnectAsync(sender, connected, drive, DrivePermission.Write);
        await PeerFlow.ConnectAsync(sender, gone, drive, DrivePermission.Write);
        var file = await UploadAndDistribute(sender, connected, drive, fileType: 503);
        await PeerFlow.DisconnectAsync(sender, gone);

        var response = await sender.Drives.Writer.SoftDeleteBatch(drive.Alias,
            Batch(File(file.FileId, recipients: [connected.Identity, gone.Identity])));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        var result = response.Content!.Results.Single();
        Assert.That((result.ErrorCode, Status(result.RecipientStatus)), Is.EqualTo(((OdinClientErrorCode?)null,
            string.Join(",", new[] { $"{connected.Identity}=Enqueued", $"{gone.Identity}=EnqueueFailed" }.Order()))));

        await sender.Sync.DrainOutboxAsync();
        await connected.Sync.ProcessInboxAsync(drive);
        Assert.That(await RecipientStateAsync(connected, drive, file.Gtid), Is.EqualTo(FileState.Deleted));
    }

    [Test]
    public async Task AFileDeletedForMeCanThenBeDeletedForEveryone()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, DrivePermission.Write, "for me then everyone");
        var file = await UploadAndDistribute(sender, recipient, drive, fileType: 504);

        var forMe = await sender.Drives.Writer.SoftDeleteBatch(drive.Alias, Batch(File(file.FileId)));
        var forEveryone = await sender.Drives.Writer.SoftDeleteBatch(drive.Alias,
            Batch(File(file.FileId, file.VersionTag, [recipient.Identity]))); // the tag from before the first delete

        Assert.That((forMe.StatusCode, forEveryone.StatusCode), Is.EqualTo((HttpStatusCode.OK, HttpStatusCode.OK)),
            forEveryone.Error?.Content);
        var result = forEveryone.Content!.Results.Single();
        Assert.That((result.ErrorCode, Status(result.RecipientStatus), result.Updated),
            Is.EqualTo(((OdinClientErrorCode?)null, $"{recipient.Identity}=Enqueued", forMe.Content!.Results.Single().Updated)));

        await sender.Sync.DrainOutboxAsync();
        await recipient.Sync.ProcessInboxAsync(drive);
        Assert.That(await RecipientStateAsync(recipient, drive, file.Gtid), Is.EqualTo(FileState.Deleted));
    }

    private static string Status(Dictionary<string, DeleteLinkedFileStatus> status) =>
        string.Join(",", status.Select(kv => $"{kv.Key}={kv.Value}").Order());

    private static async Task<(Guid FileId, Guid VersionTag, Guid Gtid)> UploadAndDistribute(
        OwnerSession sender, OwnerSession recipient, TargetDrive drive, int fileType)
    {
        var metadata = SampleMetadataData.Create(fileType: fileType, acl: AccessControlList.Connected, allowDistribution: true);
        var transit = new TransitOptions { Recipients = [recipient.Identity], Priority = OutboxPriority.High };

        var response = await sender.Drives.Writer.UploadNewMetadata(drive.Alias, metadata, transit);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"upload failed: {response.StatusCode}");
        var upload = response.Content!;
        var gtid = upload.GlobalTransitId ?? throw new InvalidOperationException("distribution upload must yield a GlobalTransitId");

        await sender.Sync.DrainOutboxAsync();
        await recipient.Sync.ProcessInboxAsync(drive);
        Assert.That(await RecipientStateAsync(recipient, drive, gtid), Is.EqualTo(FileState.Active));
        return (upload.FileId, upload.NewVersionTag, gtid);
    }

    private static async Task<FileState> RecipientStateAsync(OwnerSession recipient, TargetDrive drive, Guid gtid)
    {
        var query = await recipient.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { GlobalTransitId = [gtid], FileState = [FileState.Active, FileState.Deleted] },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, ResultShape = FileResultShape.Minimal }
        });
        Assert.That(query.IsSuccessStatusCode, Is.True, $"recipient query failed: {query.StatusCode}");
        var refs = query.Content!.FileRefs.ToList();
        Assert.That(refs, Has.Count.EqualTo(1), $"recipient should have one copy of GTID {gtid}");
        return refs[0].FileState;
    }
}
