#nullable enable
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Hosting.UnifiedV2.Drive.Shares;
using Odin.Services.Util;
using Refit;

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
        AssertDeleted(await direct.Drives.Reader.GetPayloadAsync(s.Drive.Alias, fileId, payload.Key), "straight from Frodo's server");
        AssertDeleted(await s.Sam.Drives.Peer.GetPayloadAsync(s.Frodo.Identity, s.Drive.Alias, fileId, payload.Key),
            "through Sam's own server");
    }

    [Test]
    public async Task WithdrawingAShareOfADeletedFileLeavesItsTombstoneAlone()
    {
        // Its modified is its place in the feed and the age clients purge it by
        var s = await ArrangeAsync();
        var fileId = await SharedFileTheOwnerDeletedAsync(s, SamplePayloadDefinitions.GetPayloadDefinition1());
        var before = (await s.HeaderAsync(fileId)).FileMetadata.Updated;

        var withdraw = await s.Shares.WithdrawShare(s.Drive.Alias, fileId, s.CircleId);
        Assert.That(withdraw.StatusCode, Is.EqualTo(HttpStatusCode.OK), withdraw.Error?.Content);

        var after = await s.HeaderAsync(fileId);
        Assert.That((after.FileState, after.FileMetadata.Updated), Is.EqualTo((FileState.Deleted, before)), "untouched");
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

    private static void AssertDeleted(IApiResponse response, string what) =>
        Assert.That((response.StatusCode, response.ErrorCode()), Is.EqualTo((HttpStatusCode.NotFound, OdinClientErrorCode.FileDeleted)),
            $"{what}: {response.Error?.Content}");
}
