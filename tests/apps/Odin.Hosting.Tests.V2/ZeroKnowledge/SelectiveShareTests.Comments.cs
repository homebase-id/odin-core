#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Transit;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Shares;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Reactions;
using Odin.Services.Peer.Outgoing.Drive.Reactions;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// Comments on a Selective Share drive beyond a member's first one (docs/selective-share-pr3-plan.md, section 10): the
/// owner's, an edit, a reaction to one, and what an expiry or an owner's edit of their file does to them.
/// </summary>
public partial class SelectiveShareTests
{
    [Test]
    public async Task AnOwnersCommentOnTheFilesKeyIsReadByItsMembers()
    {
        var s = await ArrangeAsync();
        var (fileId, _, fileKey) = await s.UploadClientLockedAsync();
        var lockedFileKey = LockedFileKey();
        var share = await s.Shares.WriteShares(s.Drive.Alias, new WriteSharesRequest
        {
            Shares = [s.Share(fileId, DrivePermission.Read, lockedFileKey: lockedFileKey)]
        });
        Assert.That(share.StatusCode, Is.EqualTo(HttpStatusCode.OK), share.Error?.Content);
        var file = await s.GtidOfAsync(fileId);

        var iv = await OwnerCommentAsync(s, file, fileKey, EncryptionType.FileKey);

        var key = (await SamsCommentsAsync(s, file)).Single().SharedSecretEncryptedKeyHeader;
        Assert.That((key.Type, key.EncryptedAesKey, key.Iv), Is.EqualTo((EncryptionType.Share, lockedFileKey, iv)),
            "Sam reads Frodo's comment with his share of the file");

        // One locked with the drive's key would reach no member: refused
        Assert.ThrowsAsync<Exception>(() => OwnerCommentAsync(s, file, fileKey, EncryptionType.ZkDrive),
            "a comment locked with the drive's key");
        Assert.That(await CommentsAsync(s.Frodo.Drives.Reader, s, file), Has.Count.EqualTo(1), "only the first stored");
    }

    [Test]
    public async Task ACommentIsReadWhenItsFileIsWhateverGroupTheCommentNames()
    {
        // An owner client may send its comment with the owner's group: the file's group and share decide, not the comment's
        var s = await ArrangeAsync();
        var fileId = await s.UploadAsync(SecurityGroupType.Owner, [s.Share(Guid.Empty, DrivePermission.Read)]);
        var file = await s.GtidOfAsync(fileId);
        await OwnerCommentAsync(s, file, null, null, AccessControlList.OwnerOnly);

        Assert.That((await SamsCommentsAsync(s, file)).Select(c => c.FileState), Is.EqualTo(new[] { FileState.Active }));
    }

    [Test]
    public async Task AMemberCantSendASharedFileAgainOnlyUpdateIt()
    {
        // Sending it again would reset its ACL and close it to every member; a member changes a file with an update
        var s = await ArrangeAsync();
        var uniqueId = Guid.NewGuid();
        await ZkTestFiles.SendDepositAsync(s.Sam, s.Sam, s.Frodo, s.WriteOnlyPublicKeyJwk, uniqueId, "Sam's file", null, s.Drive,
            null, [s.Share(Guid.Empty, DrivePermission.Read, lockedFileKey: LockedFileKey())]);
        var before = (await s.Frodo.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, s.Drive.Alias)).Content!;

        await ZkTestFiles.SendDepositAsync(s.Sam, s.Sam, s.Frodo, s.WriteOnlyPublicKeyJwk, uniqueId, "sent again", null, s.Drive);

        var after = (await s.Frodo.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, s.Drive.Alias)).Content!;
        Assert.That((after.FileMetadata.VersionTag, after.ServerMetadata.AccessControlList.RequiredSecurityGroup),
            Is.EqualTo((before.FileMetadata.VersionTag, SecurityGroupType.Connected)), "unchanged, still shared");
        await AssertSamSeesAsync(s, after.FileId, true, "after sending it again");
    }

    [Test]
    public async Task OnlyACommentsAuthorEditsItKeepingTheFilesKey()
    {
        var s = await ArrangeAsync();
        var (fileId, _, fileKey) = await s.UploadClientLockedAsync();
        var share = await s.Shares.WriteShares(s.Drive.Alias, new WriteSharesRequest
        {
            Shares = [s.Share(fileId, DrivePermission.Read | DrivePermission.Comment, lockedFileKey: LockedFileKey())]
        });
        Assert.That(share.StatusCode, Is.EqualTo(HttpStatusCode.OK), share.Error?.Content);
        var file = await s.GtidOfAsync(fileId);
        await SendCommentAsync(s, s.Sam, file, fileKey, "Sam's comment");
        var comment = (await CommentsAsync(s.Frodo.Drives.Reader, s, file)).Single();

        // Pippin, in the circle too, may comment on the file but not edit Sam's comment
        await AppCircles.AddMemberAsync(s.App, s.CircleId, Identities.Pippin);
        var pippin = await LoginAsOwner(Identities.Pippin);
        await EditCommentAsync(s, pippin, file, comment, fileKey, "Pippin's edit");
        var unchanged = (await CommentsAsync(s.Frodo.Drives.Reader, s, file)).Single();
        Assert.That(unchanged.FileMetadata.VersionTag, Is.EqualTo(comment.FileMetadata.VersionTag), "another member's edit");

        var newIv = await EditCommentAsync(s, s.Sam, file, comment, fileKey, "Sam's edit");
        var edited = (await CommentsAsync(s.Frodo.Drives.Reader, s, file)).Single();
        var key = edited.SharedSecretEncryptedKeyHeader;
        Assert.That((key.Type, key.Iv), Is.EqualTo((EncryptionType.FileKey, newIv)), "still the file's key, with the new IV");
        Assert.That(new KeyHeader { AesKey = fileKey.AesKey, Iv = newIv }.Decrypt(edited.FileMetadata.AppData.Content.FromBase64())
            .ToStringFromUtf8Bytes(), Is.EqualTo("Sam's edit"));
        Assert.That(edited.FileMetadata.AppData.GroupId, Is.EqualTo(file.GlobalTransitId), "still on its file");
    }

    [Test]
    public async Task AMemberReactsToACommentByTheFilesShare()
    {
        var s = await ArrangeAsync();
        var fileId = await s.UploadAsync(SecurityGroupType.Owner, [s.Share(Guid.Empty, DrivePermission.Read | DrivePermission.React)]);
        var file = await s.GtidOfAsync(fileId);
        await OwnerCommentAsync(s, file, null, null);
        var comment = (await CommentsAsync(s.Frodo.Drives.Reader, s, file)).Single();
        var commentFile = new GlobalTransitIdFileIdentifier { TargetDrive = s.Drive, GlobalTransitId = comment.FileMetadata.GlobalTransitId!.Value };

        await ReactAsync(s, commentFile, add: true);

        var reacted = (await CommentsAsync(s.Frodo.Drives.Reader, s, file)).Single();
        Assert.That(ReactionsOf(reacted), Is.EqualTo(new[] { (Thumbsup, 1) }), "the comment's summary");
        Assert.That(ReactionsOf(await s.HeaderAsync(fileId)), Is.Empty, "not the file's");
    }

    [Test]
    public async Task AnExpiredShareHidesTheFilesComments()
    {
        var s = await ArrangeAsync();
        var fileId = await s.UploadAsync(SecurityGroupType.Connected);
        var file = await s.GtidOfAsync(fileId);
        await OwnerCommentAsync(s, file, null, null);

        // Shared last, so the time before it expires is only Sam's one query
        var expiresAt = UnixTimeUtc.Now().AddSeconds(3);
        await s.ShareAsync(fileId, DrivePermission.Read, expiresAt);
        Assert.That((await SamsCommentsAsync(s, file)).Select(c => c.FileState), Is.EqualTo(new[] { FileState.Active }), "before it expires");

        await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, expiresAt.milliseconds - UnixTimeUtc.Now().milliseconds) + 200));
        Assert.That((await SamsCommentsAsync(s, file)).Select(c => c.FileState), Is.EqualTo(new[] { FileState.Deleted }),
            "once it expired: the tombstone, as for the file");
    }

    [Test]
    public async Task AnOwnersEditKeepsASharedFileSharedWhateverGroupItSends()
    {
        // The server decides a stored file's group on these drives: an owner client sending Owner with an edit (Filesync
        // does) doesn't close a shared file to its members; withdrawing the shares does (decided 2026-11-02)
        var s = await ArrangeAsync();
        var fileId = await s.UploadAsync(SecurityGroupType.Owner, [s.Share(Guid.Empty, DrivePermission.Read)]);
        var file = await s.GtidOfAsync(fileId);
        await OwnerCommentAsync(s, file, null, null);

        var header = await s.OwnerEditAsync(fileId, SecurityGroupType.Owner);

        var after = await s.HeaderAsync(fileId);
        Assert.That(after.FileMetadata.VersionTag, Is.Not.EqualTo(header.FileMetadata.VersionTag), "edited");
        Assert.That(after.ServerMetadata.AccessControlList.RequiredSecurityGroup, Is.EqualTo(SecurityGroupType.Connected), "still shared");
        Assert.That((await s.SharesAsync(fileId)).Single().Permissions, Is.EqualTo(DrivePermission.Read), "its share untouched");
        await AssertSamSeesAsync(s, fileId, true, "after Frodo's edit");
        Assert.That((await SamsCommentsAsync(s, file)).Select(c => c.FileState), Is.EqualTo(new[] { FileState.Active }), "and its comments");
    }

    /// <summary>
    /// Frodo comments on his own file from his console: on an encrypted file (<paramref name="fileKey"/> given), with the
    /// file's key and a new IV, under a key header of <paramref name="type"/>; on an unencrypted one, as it is.  Returns
    /// the IV.
    /// </summary>
    private static async Task<byte[]?> OwnerCommentAsync(Arranged s, GlobalTransitIdFileIdentifier file, KeyHeader? fileKey,
        EncryptionType? type, AccessControlList? acl = null)
    {
        var metadata = SampleMetadataData.CreateWithContent(fileType: 101, "Frodo's comment", acl ?? AccessControlList.Connected);
        metadata.ReferencedFile = file;
        var writer = new DriveWriterV2Client(s.Frodo.Identity, s.Frodo.Factory, FileSystemType.Comment);
        if (fileKey == null)
        {
            var plain = await writer.CreateNewUnencryptedFile(s.Drive.Alias, metadata, new UploadManifest { PayloadDescriptors = [] }, []);
            Assert.That(plain.StatusCode, Is.EqualTo(HttpStatusCode.OK), plain.Error?.Content);
            return null;
        }

        var key = new KeyHeader { AesKey = new SensitiveByteArray(fileKey.AesKey.GetKey()), Iv = ByteArrayUtil.GetRndByteArray(16) };
        var locked = type == EncryptionType.ZkDrive
            ? ZkBrowser.LockFileKey(key, ByteArrayUtil.GetRndByteArray(16))
            : new EncryptedKeyHeader { EncryptionVersion = 1, Type = EncryptionType.FileKey, Iv = key.Iv };
        var (upload, _, _, _) = await writer.CreateEncryptedFile(s.Drive.Alias, metadata, null!, keyHeader: key, lockedKeyHeader: locked);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
        return key.Iv;
    }

    /// <summary>
    /// A member edits a comment on Frodo's file through their own server (V2 peer update), with the file's key and a new
    /// IV under a file key header.  Returns the new IV.
    /// </summary>
    private static async Task<byte[]> EditCommentAsync(Arranged s, OwnerSession member, GlobalTransitIdFileIdentifier file,
        SharedSecretEncryptedFileHeader comment, KeyHeader fileKey, string content)
    {
        var newIv = ByteArrayUtil.GetRndByteArray(16);
        var metadata = SampleMetadataData.CreateWithContent(fileType: 101, "", AccessControlList.Connected);
        metadata.AppData.Content = new KeyHeader { AesKey = fileKey.AesKey, Iv = newIv }.EncryptDataAes(content.ToUtf8ByteArray()).ToBase64();
        metadata.IsEncrypted = true;
        metadata.AllowDistribution = true;
        metadata.ReferencedFile = file;
        metadata.VersionTag = comment.FileMetadata.VersionTag;

        var writer = new DriveWriterV2Client(member.Identity, member.Factory, FileSystemType.Comment);
        var update = await writer.UpdatePeerFileByGlobalTransitId(s.Frodo.Identity, s.Drive.Alias, comment.FileMetadata.GlobalTransitId!.Value,
            new FileUpdateInstructionSetV2
            {
                TransferIv = ByteArrayUtil.GetRndByteArray(16),
                Manifest = new UploadManifest { PayloadDescriptors = [] }
            }, metadata, [], new EncryptedKeyHeader { EncryptionVersion = 1, Type = EncryptionType.FileKey, Iv = newIv });
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), update.Error?.Content);
        await member.Sync.DrainOutboxAsync();
        return newIv;
    }
}
