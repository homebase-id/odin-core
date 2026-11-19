#nullable enable
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Hosting.UnifiedV2.Drive.Shares;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// A shared file the owner deleted: a member reading its payload is told it is deleted, through their own server and
/// straight from the owner's alike, and withdrawing a share of it leaves its tombstone alone.
/// </summary>
public partial class SelectiveShareTests
{
    [Test]
    public async Task AMembersReadOfADeletedSharedFilesPayloadSaysItIsDeleted()
    {
        var s = await ArrangeAsync();
        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var fileId = await SharedFileTheOwnerDeletedAsync(s, payload);

        var direct = await PeerTokenCaller.ForAsync(s.Sam, s.Frodo);
        DriveAsserts.AssertFileDeleted(await direct.Drives.Reader.GetPayloadAsync(s.Drive.Alias, fileId, payload.Key), "straight from Frodo's server");
        DriveAsserts.AssertFileDeleted(await s.Sam.Drives.Peer.GetPayloadAsync(s.Frodo.Identity, s.Drive.Alias, fileId, payload.Key),
            "through Sam's own server");
    }

    [Test]
    public async Task WithdrawingAShareOfADeletedFileLeavesItsTombstoneAloneButStillWithdrawsIt()
    {
        // Its modified is its place in the feed and the age clients purge it by
        var s = await ArrangeAsync();
        var fileId = await SharedFileTheOwnerDeletedAsync(s, SamplePayloadDefinitions.GetPayloadDefinition1());
        var before = (await s.HeaderAsync(fileId)).FileMetadata.Updated;
        var direct = await PeerTokenCaller.ForAsync(s.Sam, s.Frodo);
        var whileShared = await direct.Drives.Reader.GetFileHeaderAsync(s.Drive.Alias, fileId);
        Assert.That((whileShared.StatusCode, whileShared.Content?.FileState), Is.EqualTo((HttpStatusCode.OK, (FileState?)FileState.Deleted)),
            $"while shared, Sam reads its tombstone: {whileShared.Error?.Content}");

        var withdraw = await s.Shares.WithdrawShare(s.Drive.Alias, fileId, s.CircleId);
        Assert.That(withdraw.StatusCode, Is.EqualTo(HttpStatusCode.OK), withdraw.Error?.Content);

        var after = await s.HeaderAsync(fileId);
        Assert.That((after.FileState, after.FileMetadata.Updated), Is.EqualTo((FileState.Deleted, before)), "the tombstone untouched");

        // The share still goes: no bits, and Sam reads the file no more
        Assert.That((await s.SharesAsync(fileId)).Single().Permissions, Is.EqualTo(Odin.Services.Drives.DrivePermission.None), "withdrawn");
        Assert.That((await direct.Drives.Reader.GetFileHeaderAsync(s.Drive.Alias, fileId)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
            "once withdrawn, Sam reads it no more");
    }

    /// <summary>A client-locked file with <paramref name="payload"/>, shared with Sam's circle for reading, then deleted by Frodo</summary>
    private static async Task<System.Guid> SharedFileTheOwnerDeletedAsync(Arranged s, TestPayloadDefinition payload)
    {
        var (fileId, _, _) = await s.UploadClientLockedAsync(payload);
        var share = await s.Shares.WriteShares(s.Drive.Alias, new WriteSharesRequest
        {
            Shares = [s.Share(fileId, Odin.Services.Drives.DrivePermission.Read, lockedFileKey: LockedFileKey())]
        });
        Assert.That(share.StatusCode, Is.EqualTo(HttpStatusCode.OK), share.Error?.Content);

        var delete = await s.Frodo.Drives.Writer.SoftDeleteFile(s.Drive.Alias, fileId);
        Assert.That(delete.IsSuccessStatusCode, Is.True, $"delete: {delete.StatusCode} {delete.Error?.Content}");
        return fileId;
    }
}
