#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Management;
using Odin.Services.Peer.Encryption;

using static Odin.Hosting.Tests.V2.ZeroKnowledge.ZkTestFiles;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// A key holder's transform of deposits (docs/zero-knowledge-drives.md): a deposit's sealed key header is replaced by the
/// drive's own, locking the same file key under the same IV, and nothing else about the file changes.
/// </summary>
[TestFixture]
public class KeyHeaderTransformTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task TheOwnerTransformsADepositAndNothingElseAboutItChanges()
    {
        var (frodo, sam, drive, keys, _) = await ArrangeAsync();
        var received = await DepositAsync(frodo, sam, drive, keys);
        var uniqueId = UniqueIdOf(received);
        Assert.That(received.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Sealed));

        var response = await TransformAsync(frodo, drive, Transform(received, keys));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        var result = response.Content!.Results.Single();
        Assert.That(result.ErrorCode, Is.Null);
        Assert.That(result.KeyHeader.Type, Is.EqualTo(EncryptionType.ZkDrive));

        var after = await HeaderByUniqueIdAsync(frodo, uniqueId, drive);
        Assert.That(after.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.ZkDrive));
        Assert.That(after.SharedSecretEncryptedKeyHeader.EncryptedAesKey, Is.EqualTo(result.KeyHeader.EncryptedAesKey), "the one answered");
        Assert.That(after.FileMetadata.VersionTag, Is.EqualTo(received.FileMetadata.VersionTag), "the version tag doesn't move");
        Assert.That(after.FileMetadata.Updated, Is.EqualTo(received.FileMetadata.Updated), "nor modified, so no device resyncs");
        var opened = ZkBrowser.OpenFileKey(after.SharedSecretEncryptedKeyHeader, keys.DriveKey);
        Assert.That(opened.Decrypt(after.FileMetadata.AppData.Content.FromBase64()).ToStringFromUtf8Bytes(), Is.EqualTo("Sam's"),
            "the drive's key opens it to the same content");
    }

    [Test]
    public async Task EachFileIsJudgedOnItsOwnAndOneRefusalStopsNoOther()
    {
        var (frodo, sam, drive, keys, _) = await ArrangeAsync();
        var good = await DepositAsync(frodo, sam, drive, keys);
        var otherIv = await DepositAsync(frodo, sam, drive, keys);
        var aes = await DepositAsync(frodo, sam, drive, keys);
        var stale = await DepositAsync(frodo, sam, drive, keys);
        var deleted = await DepositAsync(frodo, sam, drive, keys);
        var delete = await frodo.Drives.Writer.SoftDeleteFile(drive.Alias, deleted.FileId);
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.OK), delete.Error?.Content);

        var ownKey = KeyHeader.NewRandom16();
        var ownId = Guid.NewGuid();
        var (upload, _, _, _) = await frodo.Drives.Writer.CreateEncryptedFile(drive.Alias, Metadata(ownId, "Frodo's"),
            transitOptions: null!, keyHeader: ownKey, lockedKeyHeader: ZkBrowser.LockFileKey(ownKey, keys.DriveKey));
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
        var own = await HeaderByUniqueIdAsync(frodo, ownId, drive);

        var withOtherIv = Transform(otherIv, keys);
        withOtherIv.KeyHeader.Iv = ByteArrayUtil.GetRndByteArray(16);
        var withAesType = Transform(aes, keys);
        withAesType.KeyHeader.Type = EncryptionType.Aes;
        var response = await TransformAsync(frodo, drive,
            Transform(good, keys),
            withOtherIv,
            withAesType,
            new TransformKeyHeaderV2 { FileId = stale.FileId, VersionTag = Guid.NewGuid(), KeyHeader = Transform(stale, keys).KeyHeader },
            Transform(deleted, keys),
            new TransformKeyHeaderV2 { FileId = own.FileId, VersionTag = own.FileMetadata.VersionTag, KeyHeader = own.SharedSecretEncryptedKeyHeader },
            new TransformKeyHeaderV2 { FileId = Guid.NewGuid(), VersionTag = Guid.NewGuid(), KeyHeader = Transform(good, keys).KeyHeader });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        Assert.That(response.Content!.Results.Select(r => r.ErrorCode), Is.EqualTo(new OdinClientErrorCode?[]
        {
            null,
            OdinClientErrorCode.InvalidKeyHeader, // another IV: the content was encrypted under the stored one
            OdinClientErrorCode.InvalidKeyHeader, // not locked with the drive's key
            OdinClientErrorCode.VersionTagMismatch,
            OdinClientErrorCode.CannotUpdateNonActiveFile,
            OdinClientErrorCode.NotSealed, // the drive's own already
            OdinClientErrorCode.FileNotFound
        }), "in request order");

        Assert.That((await HeaderByUniqueIdAsync(frodo, UniqueIdOf(good), drive)).SharedSecretEncryptedKeyHeader.Type,
            Is.EqualTo(EncryptionType.ZkDrive), "transformed though the rest were refused");
        foreach (var refused in new[] { otherIv, aes, stale })
        {
            Assert.That((await HeaderByUniqueIdAsync(frodo, UniqueIdOf(refused), drive)).SharedSecretEncryptedKeyHeader.Type,
                Is.EqualTo(EncryptionType.Sealed), "left as it was");
        }
    }

    [Test]
    public async Task ASendersChangeBeforeTheTransformStands()
    {
        // The sender changed the deposit after Frodo read it: the transform is of what Frodo read, so it is refused and
        // Frodo transforms the new one when he reads it
        var (frodo, sam, drive, keys, _) = await ArrangeAsync();
        var read = await DepositAsync(frodo, sam, drive, keys);
        var uniqueId = UniqueIdOf(read);
        await SendDepositAsync(sam, sam, frodo, keys.WriteOnlyPublicKeyJwk, uniqueId, "Sam's, changed", remoteDrive: drive,
            versionTag: read.FileMetadata.VersionTag);
        var changed = await HeaderByUniqueIdAsync(frodo, uniqueId, drive);
        Assert.That(changed.FileMetadata.VersionTag, Is.Not.EqualTo(read.FileMetadata.VersionTag), "Sam's change landed");

        var response = await TransformAsync(frodo, drive, Transform(read, keys));

        Assert.That(response.Content!.Results.Single().ErrorCode, Is.EqualTo(OdinClientErrorCode.VersionTagMismatch));
        var after = await HeaderByUniqueIdAsync(frodo, uniqueId, drive);
        Assert.That(after.SharedSecretEncryptedKeyHeader.EncryptedAesKey, Is.EqualTo(changed.SharedSecretEncryptedKeyHeader.EncryptedAesKey),
            "Sam's key header stands");
    }

    [Test]
    public async Task TheSenderMayStillDeleteATransformedDeposit()
    {
        // The reason the version tag doesn't move: the sender's right to change what they sent follows it
        // (docs/file-access.md, "Changing a file you sent")
        var (frodo, sam, drive, keys, _) = await ArrangeAsync();
        var received = await DepositAsync(frodo, sam, drive, keys);
        var transform = await TransformAsync(frodo, drive, Transform(received, keys));
        Assert.That(transform.Content!.Results.Single().ErrorCode, Is.Null);

        await sam.Drives.PeerWriter.SendDeleteRequestOverPeer(frodo.Identity, drive, received.FileMetadata.GlobalTransitId!.Value);
        await sam.Sync.DrainOutboxAsync();

        var after = await frodo.Drives.Reader.GetFileHeaderAsync(drive.Alias, received.FileId);
        Assert.That(after.Content?.FileState, Is.EqualTo(FileState.Deleted));
    }

    [Test]
    public async Task ASendersSealedChangeAfterTheTransformLandsUntilTheOwnerChangesIt()
    {
        // The transform keeps the version tag, so the sender's record of their last write still matches: their change is
        // of their own file, sealed again for a key holder to transform (ZkDriveFiles.AssertMayReplace)
        var (frodo, sam, drive, keys, _) = await ArrangeAsync();
        var received = await DepositAsync(frodo, sam, drive, keys);
        await TransformAsync(frodo, drive, Transform(received, keys));

        await SendDepositAsync(sam, sam, frodo, keys.WriteOnlyPublicKeyJwk, UniqueIdOf(received), "Sam's, changed", remoteDrive: drive,
            versionTag: received.FileMetadata.VersionTag);
        var changed = await HeaderByUniqueIdAsync(frodo, UniqueIdOf(received), drive);
        Assert.That(changed.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Sealed), "sealed again");
        Assert.That(ZkBrowser.OpenDeposit(changed, keys.WriteOnlyPrivateKey).ToStringFromUtf8Bytes(), Is.EqualTo("Sam's, changed"));

        // Frodo takes it over with the drive's key: a seal of Sam's no longer replaces his write
        var newKey = WithNewIv(ZkBrowser.OpenDepositFileKey(changed, keys.WriteOnlyPrivateKey));
        var takeOver = await UpdateAsync(frodo, changed.FileId, UniqueIdOf(changed), newKey, ZkBrowser.LockFileKey(newKey, keys.DriveKey),
            changed.FileMetadata.VersionTag, drive: drive);
        Assert.That(takeOver.StatusCode, Is.EqualTo(HttpStatusCode.OK), takeOver.Error?.Content);
        var frodos = await HeaderByUniqueIdAsync(frodo, UniqueIdOf(received), drive);

        await SendDepositAsync(sam, sam, frodo, keys.WriteOnlyPublicKeyJwk, UniqueIdOf(received), "Sam's, again", remoteDrive: drive,
            versionTag: frodos.FileMetadata.VersionTag);
        var after = await HeaderByUniqueIdAsync(frodo, UniqueIdOf(received), drive);
        Assert.That(after.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.ZkDrive));
        Assert.That(after.FileMetadata.VersionTag, Is.EqualTo(frodos.FileMetadata.VersionTag), "refused: Frodo changed it since Sam's last write");
    }

    [Test]
    public async Task OnlyACallerHoldingTheDrivesKeyTransforms()
    {
        var (frodo, sam, drive, keys, zkMasterKey) = await ArrangeAsync();
        var received = await DepositAsync(frodo, sam, drive, keys);
        var app = await AppSession.SetupAsync(frodo, drive, DrivePermission.ReadWrite);

        var withoutKey = await app.Drives.Writer.TransformKeyHeaders(drive.Alias,
            new TransformKeyHeadersRequestV2 { Files = [Transform(received, keys)] });
        Assert.That(withoutKey.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "an app device not handed the app's key");

        var (device, _) = await ZkAppSteps.SignInAsync(frodo, app.AppId, zkMasterKey);
        var withKey = await device.Drives.Writer.TransformKeyHeaders(drive.Alias,
            new TransformKeyHeadersRequestV2 { Files = [Transform(received, keys)] });
        Assert.That(withKey.StatusCode, Is.EqualTo(HttpStatusCode.OK), withKey.Error?.Content);
        Assert.That(withKey.Content!.Results.Single().ErrorCode, Is.Null, "a device holding the drive's key, as chat-kmp's does");
    }

    [Test]
    public async Task ADriveThatIsNotZeroKnowledgeHasNoDepositsToTransform()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "Frodo's shared", allowAnonymousReads: false);

        var response = await TransformAsync(frodo, drive,
            new TransformKeyHeaderV2 { FileId = Guid.NewGuid(), VersionTag = Guid.NewGuid(), KeyHeader = EncryptedKeyHeader.Empty() });

        DriveAsserts.AssertRejected(response, OdinClientErrorCode.ZkDriveKeyHeaderNeedsZeroKnowledgeDrive);
    }

    [Test]
    public async Task ShardsStaySealed()
    {
        // The server releases a friend's shard only as the sealed deposit it holds (ShamirBaseService)
        var frodo = await LoginAsOwner(Identities.Frodo);
        await ZkOwnerSteps.MakeZeroKnowledgeAsync(frodo, OwnerLogin.DefaultPassword);

        var response = await TransformAsync(frodo, WellKnownAppDrives.ShardRecoveryDrive,
            new TransformKeyHeaderV2 { FileId = Guid.NewGuid(), VersionTag = Guid.NewGuid(), KeyHeader = EncryptedKeyHeader.Empty() });

        DriveAsserts.AssertRejected(response, OdinClientErrorCode.InvalidDrive);
    }

    /// <summary>What a key holder sends for <paramref name="deposit"/>: its sealed key header opened, and the file key locked with the drive's key.</summary>
    private static TransformKeyHeaderV2 Transform(SharedSecretEncryptedFileHeader deposit, ZkBrowser.DriveKeys keys)
    {
        return new TransformKeyHeaderV2
        {
            FileId = deposit.FileId,
            VersionTag = deposit.FileMetadata.VersionTag,
            KeyHeader = ZkBrowser.LockFileKey(ZkBrowser.OpenDepositFileKey(deposit, keys.WriteOnlyPrivateKey), keys.DriveKey)
        };
    }

    private static Task<Refit.ApiResponse<TransformKeyHeadersResultV2>> TransformAsync(OwnerSession owner, TargetDrive drive,
        params TransformKeyHeaderV2[] files) =>
        owner.Drives.Writer.TransformKeyHeaders(drive.Alias, new TransformKeyHeadersRequestV2 { Files = files.ToList() });

    private static async Task<SharedSecretEncryptedFileHeader> DepositAsync(OwnerSession frodo, OwnerSession sam, TargetDrive drive,
        ZkBrowser.DriveKeys keys)
    {
        var uniqueId = Guid.NewGuid();
        await SendDepositAsync(sam, sam, frodo, keys.WriteOnlyPublicKeyJwk, uniqueId, "Sam's", remoteDrive: drive);
        return await HeaderByUniqueIdAsync(frodo, uniqueId, drive);
    }

    private static Guid UniqueIdOf(SharedSecretEncryptedFileHeader header) => header.FileMetadata.AppData.UniqueId!.Value;

    /// <summary>Frodo's Private drive, on which Sam may deposit, its keys, and Frodo's zero-knowledge master key.</summary>
    private async Task<(OwnerSession Frodo, OwnerSession Sam, TargetDrive Drive, ZkBrowser.DriveKeys Keys, byte[] ZkMasterKey)> ArrangeAsync()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var (zkMasterKey, _, _) = await ZkOwnerSteps.MakeZeroKnowledgeAsync(frodo, OwnerLogin.DefaultPassword);
        var (drive, keys) = await ZkOwnerSteps.CreateZeroKnowledgeDriveAsync(frodo, zkMasterKey, DriveSharingModel.Private,
            name: "Frodo's private");
        await PeerFlow.ConnectAsync(sam, frodo, drive, DrivePermission.Write);
        return (frodo, sam, drive, keys, zkMasterKey);
    }
}
