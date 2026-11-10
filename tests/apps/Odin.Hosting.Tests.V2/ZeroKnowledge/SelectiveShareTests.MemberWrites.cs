#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using static Odin.Hosting.Tests.V2.ZeroKnowledge.ZkTestFiles;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// A member adding and editing files in a shared folder (docs/selective-share-pr3-plan.md, section 7) with real locked
/// file keys: a new file's share is bound to the global transit id its writer picked (format 3), and an edit changes the
/// file's header, its payloads, or both, keeping the file key.
/// </summary>
public partial class SelectiveShareTests
{
    public enum MemberEdit
    {
        ContentOnly,
        ReplaceAPayload,
        AddAPayload,
        DeleteAPayload,
        ContentAndAPayload
    }

    [Test]
    public async Task AMembersNewFileIsReadByTheirCircleThroughAKeyBoundToTheGlobalTransitIdTheyPicked()
    {
        // Sam knows the global transit id before the file exists, so he locks its key for his circle in the same send
        var s = await ArrangeAsync();
        var circleKey = CircleKey();
        var fileKey = KeyHeader.NewRandom16();
        var gtid = Guid.NewGuid();
        var uniqueId = Guid.NewGuid();
        var payload = Payload(SamplePayloadDefinitions.GetPayloadDefinition1(), "Sam's payload");

        await SendDepositAsync(s.Sam, s.Sam, s.Frodo, s.WriteOnlyPublicKeyJwk, uniqueId, "Sam's file", payload, s.Drive,
            shares: [s.Share(Guid.Empty, DrivePermission.Read | DrivePermission.Write, lockedFileKey: LockFileKey(s, circleKey, fileKey, gtid))],
            fileKey: fileKey, globalTransitId: gtid);

        Assert.That((await s.HeaderByUniqueIdAsync(uniqueId)).FileMetadata.GlobalTransitId, Is.EqualTo(gtid), "Frodo's copy keeps the id Sam picked");
        await AssertMemberOpensAsync(s, gtid, circleKey, "Sam's file", [(payload.Key, "Sam's payload")]);
    }

    [Test]
    public async Task AMemberInTwoCirclesAddsAFileToAFolderSharedWithBothForBoth()
    {
        // Frodo's folder is shared with both of Sam's circles, Pippin is in the second only.  Sam sees both shares on the
        // folder, so his client shares the file he adds with both, each locked with that circle's key
        var s = await ArrangeAsync();
        var second = await s.CircleWithKeyAsync(DrivePermission.Read | DrivePermission.Write);
        await AppCircles.AddMemberAsync(s.App, second, Identities.Sam);
        await AppCircles.AddMemberAsync(s.App, second, Identities.Pippin);
        var circleKeys = new Dictionary<Guid, byte[]> { [s.CircleId] = CircleKey(), [second] = CircleKey() };
        var folderKey = KeyHeader.NewRandom16();
        var folderGtid = Guid.NewGuid();
        await s.UploadClientLockedAsync(fileKey: folderKey, globalTransitId: folderGtid,
            shares: circleKeys.Select(k => s.Share(Guid.Empty, DrivePermission.Read | DrivePermission.Write,
                lockedFileKey: LockFileKey(s, k.Value, folderKey, folderGtid, k.Key), circleId: k.Key)).ToList());

        var folder = await s.Sam.Drives.Peer.GetFileHeaderByGtidAsync(s.Frodo.Identity, s.Drive.Alias, folderGtid);
        Assert.That(folder.StatusCode, Is.EqualTo(HttpStatusCode.OK), folder.Error?.Content);
        var samsCircles = folder.Content!.Shares!.Select(r => r.CircleId).ToList();
        Assert.That(samsCircles, Is.EquivalentTo(circleKeys.Keys), "Sam sees the folder's shares for both his circles");

        var fileKey = KeyHeader.NewRandom16();
        var gtid = Guid.NewGuid();
        var uniqueId = Guid.NewGuid();
        await SendDepositAsync(s.Sam, s.Sam, s.Frodo, s.WriteOnlyPublicKeyJwk, uniqueId, "Sam's file", remoteDrive: s.Drive,
            shares: samsCircles.Select(c => s.Share(Guid.Empty, DrivePermission.Read | DrivePermission.Write,
                lockedFileKey: LockFileKey(s, circleKeys[c], fileKey, gtid, c), circleId: c)).ToList(),
            fileKey: fileKey, globalTransitId: gtid);

        var added = await s.HeaderByUniqueIdAsync(uniqueId);
        Assert.That((await s.SharesAsync(added.FileId)).Select(r => r.CircleId), Is.EquivalentTo(circleKeys.Keys), "shared with both");
        await AssertMemberOpensAsync(s, s.Sam, gtid, circleKeys, "Sam's file", []);
        await AssertMemberOpensAsync(s, await LoginAsOwner(Identities.Pippin), gtid, circleKeys, "Sam's file", []);
    }

    [Test]
    public async Task AnOwnersUploadSharesANewFileInOneStepThroughAKeyBoundToTheGlobalTransitIdItPicked()
    {
        var s = await ArrangeAsync();
        var circleKey = CircleKey();
        var fileKey = KeyHeader.NewRandom16();
        var gtid = Guid.NewGuid();

        var (fileId, _, _) = await s.UploadClientLockedAsync(fileKey: fileKey, globalTransitId: gtid,
            shares: [s.Share(Guid.Empty, DrivePermission.Read, lockedFileKey: LockFileKey(s, circleKey, fileKey, gtid))]);

        Assert.That((await s.HeaderAsync(fileId)).FileMetadata.GlobalTransitId, Is.EqualTo(gtid));
        await AssertMemberOpensAsync(s, gtid, circleKey, "shared", []);
    }

    [Test]
    public async Task AGlobalTransitIdAlreadyOnTheDriveIsRefused()
    {
        var s = await ArrangeAsync();
        var gtid = Guid.NewGuid();
        var first = await s.Frodo.Drives.Writer.CreateNewUnencryptedFile(s.Drive.Alias, s.Metadata(SecurityGroupType.Owner),
            new UploadManifest { PayloadDescriptors = [] }, [], globalTransitId: gtid);
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK), first.Error?.Content);

        var again = await s.Frodo.Drives.Writer.CreateNewUnencryptedFile(s.Drive.Alias, s.Metadata(SecurityGroupType.Owner),
            new UploadManifest { PayloadDescriptors = [] }, [], globalTransitId: gtid);
        DriveAsserts.AssertRejected(again, OdinClientErrorCode.ExistingFileWithGlobalTransitId);
    }

    /// <summary>
    /// A client names a new file's global transit id only where nothing of it reaches a feed, which finds its copies by
    /// global transit id alone: not on another kind of drive, not on a send from the transient drive that isn't a deposit,
    /// and never with another id for the recipient's copy.
    /// </summary>
    [TestCase("an ordinary drive", TestName = "AGlobalTransitIdIsNotNamedOnAnOrdinaryDrive")]
    [TestCase("an ordinary send", TestName = "AGlobalTransitIdIsNotNamedOnASendThatIsNoDeposit")]
    [TestCase("another id for the copy", TestName = "AGlobalTransitIdIsNotNamedWithAnotherForTheRecipientsCopy")]
    public async Task AGlobalTransitIdIsNamedOnlyWhereNoFeedFindsIt(string where)
    {
        var s = await ArrangeAsync();
        var ordinary = TargetDrive.NewTargetDrive();
        await s.Frodo.Admin.EnsureDrive(ordinary, "an ordinary drive", allowAnonymousReads: false);
        var metadata = s.Metadata(SecurityGroupType.Owner);
        metadata.AllowDistribution = true;
        var send = new TransitOptions
        {
            IsTransient = true, Recipients = [s.Frodo.Identity.DomainName], RemoteTargetDrive = s.Drive,
            OverrideRemoteGlobalTransitId = where == "another id for the copy" ? Guid.NewGuid() : null
        };

        if (where == "an ordinary drive")
        {
            DriveAsserts.AssertRejected(await s.Frodo.Drives.Writer.CreateNewUnencryptedFile(ordinary.Alias, metadata,
                new UploadManifest { PayloadDescriptors = [] }, [], globalTransitId: Guid.NewGuid()), OdinClientErrorCode.ArgumentError);
            return;
        }

        // The encrypted upload's client throws on a 400, naming its code
        var sealedKeyHeader = where == "an ordinary send" ? null : ZkBrowser.SealNewFileKey(s.WriteOnlyPublicKeyJwk).Sealed;
        var refused = Assert.ThrowsAsync<Exception>(() => s.Sam.Drives.Writer.CreateEncryptedFile(SystemDriveConstants.TransientTempDrive.Alias,
            metadata, send, lockedKeyHeader: sealedKeyHeader, globalTransitId: Guid.NewGuid()));
        Assert.That(refused!.Message, Does.EndWith(nameof(OdinClientErrorCode.ArgumentError)));
    }

    /// <summary>
    /// The file's header and any of its payloads change in a member's edit, keeping the file key: a share key header with
    /// only the new IV (decision C), each new payload under its own new IV.
    /// </summary>
    [TestCase(MemberEdit.ContentOnly)]
    [TestCase(MemberEdit.ReplaceAPayload)]
    [TestCase(MemberEdit.AddAPayload)]
    [TestCase(MemberEdit.DeleteAPayload)]
    [TestCase(MemberEdit.ContentAndAPayload)]
    public async Task AMemberWithWriteEditsASharedFilesHeaderAndPayloads(MemberEdit edit)
    {
        var s = await ArrangeAsync();
        var circleKey = CircleKey();
        var fileKey = KeyHeader.NewRandom16();
        var gtid = Guid.NewGuid();
        var first = Payload(SamplePayloadDefinitions.GetPayloadDefinition1(), "the first payload");
        var (fileId, locked, _) = await s.UploadClientLockedAsync(first, fileKey, globalTransitId: gtid,
            shares: [s.Share(Guid.Empty, DrivePermission.Read | DrivePermission.Write, lockedFileKey: LockFileKey(s, circleKey, fileKey, gtid))]);
        var before = await s.HeaderAsync(fileId);

        var content = edit is MemberEdit.ContentOnly or MemberEdit.ContentAndAPayload ? "edited by Sam" : "shared";
        var replaced = EncryptedPayload(SamplePayloadDefinitions.GetPayloadDefinition1(), fileKey, "the first payload, edited by Sam");
        var added = EncryptedPayload(SamplePayloadDefinitions.GetPayloadDefinition2(), fileKey, "a second payload, from Sam");
        (List<TestPayloadDefinition> Sent, List<string> Deleted, List<(string Key, string Content)> Expected) change = edit switch
        {
            MemberEdit.ReplaceAPayload or MemberEdit.ContentAndAPayload => ([replaced], [], [(first.Key, "the first payload, edited by Sam")]),
            MemberEdit.AddAPayload => ([added], [], [(first.Key, "the first payload"), (added.Key, "a second payload, from Sam")]),
            MemberEdit.DeleteAPayload => ([], [first.Key], []),
            _ => ([], [], [(first.Key, "the first payload")])
        };
        var (sent, deleted, expected) = change;

        var newIv = await MemberEditsAsync(s, gtid, before.FileMetadata.VersionTag, fileKey, content, sent, deleted, KeepFileKey);

        var after = await s.HeaderAsync(fileId);
        var key = after.SharedSecretEncryptedKeyHeader;
        Assert.That((key.Type, key.EncryptedAesKey, key.Iv), Is.EqualTo((EncryptionType.ZkDrive, locked.EncryptedAesKey, newIv)),
            "the file's own key header, with Sam's new IV");
        Assert.That(after.FileMetadata.VersionTag, Is.Not.EqualTo(before.FileMetadata.VersionTag), "an edit is a new version");
        Assert.That(after.ServerMetadata.AccessControlList.RequiredSecurityGroup, Is.EqualTo(SecurityGroupType.Connected), "it keeps its ACL");
        Assert.That(after.FileMetadata.Payloads.Select(p => p.Key), Is.EquivalentTo(expected.Select(e => e.Item1)), "its payloads");
        await AssertMemberOpensAsync(s, gtid, circleKey, content, expected);
    }

    [Test]
    public async Task AMemberEditsTheirOwnNewFileWithANewSealUnderTheSameKey()
    {
        // Not yet locked by an owner device, the file keeps its sealed key header: the writer seals the same file key with
        // a new IV, and the shares stay valid, being of the same key
        var s = await ArrangeAsync();
        var circleKey = CircleKey();
        var fileKey = KeyHeader.NewRandom16();
        var gtid = Guid.NewGuid();
        var uniqueId = Guid.NewGuid();
        var first = Payload(SamplePayloadDefinitions.GetPayloadDefinition1(), "Sam's payload");
        await SendDepositAsync(s.Sam, s.Sam, s.Frodo, s.WriteOnlyPublicKeyJwk, uniqueId, "Sam's file", first, s.Drive,
            shares: [s.Share(Guid.Empty, DrivePermission.Read | DrivePermission.Write, lockedFileKey: LockFileKey(s, circleKey, fileKey, gtid))],
            fileKey: fileKey, globalTransitId: gtid);
        var before = await s.HeaderByUniqueIdAsync(uniqueId);

        var replaced = EncryptedPayload(SamplePayloadDefinitions.GetPayloadDefinition1(), fileKey, "Sam's payload, edited");
        var newIv = await MemberEditsAsync(s, gtid, before.FileMetadata.VersionTag, fileKey, "Sam's file, edited", [replaced], [],
            SealAgain(s, fileKey), uniqueId);

        var after = await s.HeaderByUniqueIdAsync(uniqueId);
        Assert.That((after.SharedSecretEncryptedKeyHeader.Type, after.SharedSecretEncryptedKeyHeader.Iv), Is.EqualTo((EncryptionType.Sealed, newIv)),
            "his new seal");
        Assert.That(after.FileMetadata.VersionTag, Is.Not.EqualTo(before.FileMetadata.VersionTag), "an edit is a new version");
        await AssertMemberOpensAsync(s, gtid, circleKey, "Sam's file, edited", [(first.Key, "Sam's payload, edited")]);
    }

    /// <summary>
    /// Sam edits Frodo's file through his own server (V2 peer update, by global transit id), naming the version tag he
    /// has: the content under a new IV, <paramref name="payloads"/> appended or overwritten, <paramref name="deleted"/>
    /// removed, and the key header <paramref name="keyHeaderFor"/> makes of the new IV.  The app data is replaced, so it
    /// names the file's <paramref name="uniqueId"/> again when it has one.  Sent by <paramref name="editor"/>, one of Sam's
    /// apps, or his own client.  Returns the new IV.
    /// </summary>
    private static async Task<byte[]> MemberEditsAsync(Arranged s, Guid gtid, Guid versionTag, KeyHeader fileKey, string content,
        List<TestPayloadDefinition> payloads, List<string> deleted, Func<byte[], EncryptedKeyHeader> keyHeaderFor, Guid? uniqueId = null,
        IV2Caller? editor = null)
    {
        var (newIv, _) = await QueueMemberEditAsync(s, gtid, versionTag, fileKey, content, payloads, deleted, keyHeaderFor, uniqueId,
            editor);
        await s.Sam.Sync.DrainOutboxAsync();
        return newIv;
    }

    /// <summary>
    /// <see cref="MemberEditsAsync"/>, left in Sam's outbox: returns the new IV and the version tag the update gave back
    /// </summary>
    private static async Task<(byte[] NewIv, Guid SentVersionTag)> QueueMemberEditAsync(Arranged s, Guid gtid, Guid versionTag,
        KeyHeader fileKey, string content, List<TestPayloadDefinition> payloads, List<string> deleted,
        Func<byte[], EncryptedKeyHeader> keyHeaderFor, Guid? uniqueId = null, IV2Caller? editor = null)
    {
        var newKey = WithNewIv(fileKey);
        var metadata = s.Metadata(SecurityGroupType.Connected, versionTag);
        metadata.AppData.UniqueId = uniqueId;
        metadata.AppData.Content = newKey.EncryptDataAes(content.ToUtf8ByteArray()).ToBase64();
        metadata.IsEncrypted = true;
        metadata.AllowDistribution = true;

        var update = await (editor ?? s.Sam).Drives.Writer.UpdatePeerFileByGlobalTransitId(s.Frodo.Identity, s.Drive.Alias, gtid,
            new FileUpdateInstructionSetV2
            {
                TransferIv = ByteArrayUtil.GetRndByteArray(16),
                Manifest = new UploadManifest
                {
                    PayloadDescriptors = payloads.Select(p => p.ToPayloadDescriptor(PayloadUpdateOperationType.AppendOrOverwrite))
                        .Concat(deleted.Select(k => new UploadManifestPayloadDescriptor
                        {
                            PayloadKey = k, PayloadUpdateOperationType = PayloadUpdateOperationType.DeletePayload
                        }))
                        .ToList()
                }
            }, metadata, payloads, keyHeaderFor(newKey.Iv));
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), update.Error?.Content);
        return (newKey.Iv, update.Content!.NewVersionTag);
    }

    private static Task AssertMemberOpensAsync(Arranged s, Guid gtid, byte[] circleKey, string content,
        List<(string Key, string Content)> payloads) =>
        AssertMemberOpensAsync(s, s.Sam, gtid, new Dictionary<Guid, byte[]> { [s.CircleId] = circleKey }, content, payloads);

    /// <summary>
    /// <paramref name="member"/> reads the file over peer: the locked file key of the share they read it through, opened
    /// with that circle's key and the file's global transit id, opens its content and every payload.
    /// </summary>
    private static async Task AssertMemberOpensAsync(Arranged s, OwnerSession member, Guid gtid, IReadOnlyDictionary<Guid, byte[]> circleKeys,
        string content, List<(string Key, string Content)> payloads)
    {
        var read = await member.Drives.Peer.GetFileHeaderByGtidAsync(s.Frodo.Identity, s.Drive.Alias, gtid);
        Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.OK), read.Error?.Content);
        var header = read.Content!;
        var key = header.SharedSecretEncryptedKeyHeader;
        Assert.That(key.Type, Is.EqualTo(EncryptionType.Share), "their share's key");
        var circle = header.Shares![0].CircleId;

        var fileKey = ShareKeys.Open(circleKeys[circle], key.EncryptedAesKey, ShareKeys.FileKeyAad(s.Drive.Alias,
            header.FileMetadata.GlobalTransitId!.Value, circle), ShareKeys.FormatByGlobalTransitId);
        var opened = new KeyHeader { AesKey = new SensitiveByteArray(fileKey), Iv = key.Iv }.Decrypt(header.FileMetadata.AppData.Content.FromBase64());
        Assert.That(opened.ToStringFromUtf8Bytes(), Is.EqualTo(content), "the content");

        foreach (var (payloadKey, payloadContent) in payloads)
        {
            var descriptor = header.FileMetadata.Payloads.Single(p => p.Key == payloadKey);
            var bytes = await member.Drives.Peer.GetPayloadByGtidAsync(s.Frodo.Identity, s.Drive.Alias, gtid, payloadKey);
            Assert.That(bytes.StatusCode, Is.EqualTo(HttpStatusCode.OK), bytes.Error?.Content);
            var plain = new KeyHeader { AesKey = new SensitiveByteArray(fileKey), Iv = descriptor.Iv }
                .Decrypt(await bytes.Content!.ReadAsByteArrayAsync());
            Assert.That(plain.ToStringFromUtf8Bytes(), Is.EqualTo(payloadContent), $"payload {payloadKey}");
        }
    }

    // A member's edit keeping the file's key: a share key header with only the new IV (decision C)
    private static EncryptedKeyHeader KeepFileKey(byte[] newIv) => new() { EncryptionVersion = 1, Type = EncryptionType.Share, Iv = newIv };

    /// <summary>A deposit's file key sealed to the drive's write-only key again, with the edit's new IV</summary>
    private static Func<byte[], EncryptedKeyHeader> SealAgain(Arranged s, KeyHeader fileKey) =>
        iv => EncryptedKeyHeader.SealedTo(new KeyHeader { AesKey = fileKey.AesKey, Iv = iv }, s.WriteOnlyPublicKeyJwk);

    // A circle key as a member unseals it: 16 random bytes, which the server never sees
    private static byte[] CircleKey() => ByteArrayUtil.GetRndByteArray(16);

    // A new file's key locked for the arranged circle, or the one named, bound to the global transit id the writer picked
    // (format 3)
    private static byte[] LockFileKey(Arranged s, byte[] circleKey, KeyHeader fileKey, Guid gtid, Guid? circleId = null) =>
        ShareKeys.Lock(circleKey, fileKey.AesKey.GetKey(), ShareKeys.FileKeyAad(s.Drive.Alias, gtid, circleId ?? s.CircleId),
            RandomNumberGenerator.GetBytes(12), ShareKeys.FormatByGlobalTransitId);

    // A payload for an upload, which encrypts it with the file key and the payload's own IV
    private static TestPayloadDefinition Payload(TestPayloadDefinition payload, string content)
    {
        payload.Content = content.ToUtf8ByteArray();
        return payload;
    }

    // A payload for an update, which sends it as it is: encrypted here, as a client does, under the payload's own new IV
    private static TestPayloadDefinition EncryptedPayload(TestPayloadDefinition payload, KeyHeader fileKey, string content)
    {
        var payloadKey = WithNewIv(fileKey);
        payload.Iv = payloadKey.Iv;
        payload.Content = payloadKey.EncryptDataAes(content.ToUtf8ByteArray());
        return payload;
    }
}
