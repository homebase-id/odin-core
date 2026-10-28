#nullable enable
using System;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive;

using static Odin.Hosting.Tests.V2.ZeroKnowledge.ZkTestFiles;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// A deposit must be sealed to the drive's write-only key, and a sender's server keeps a copy of a peer drive's key
/// until a deposit sealed with it is refused (docs/drive-key-retirement-plan.md).  Deposits on the Shard Recovery
/// drive, a zero-knowledge drive.
/// </summary>
[TestFixture]
public class PeerDriveKeyCacheTests : V2Fixture
{
    private const string Content = "sealed to the drive's key";

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task ADepositSealedToAKeyTheDriveDoesNotHoldIsRefused()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        await ZkOwnerSteps.MakeZeroKnowledgeAsync(sam, OwnerLogin.DefaultPassword);
        var drivesKey = (await ZkOwnerSteps.GetShardRecoveryDriveAsync(sam)).WriteOnlyPublicKeyJwk;
        var app = await AppSession.SetupAsync(sam, Drive, Odin.Services.Drives.DrivePermission.ReadWrite);

        var (fileKey, sealedKeyHeader) = ZkBrowser.SealNewFileKey(drivesKey);
        var (upload, _, _, _) = await app.Drives.Writer.CreateEncryptedFile(Drive.Alias, Metadata(Guid.NewGuid()),
            transitOptions: null!, keyHeader: fileKey, lockedKeyHeader: sealedKeyHeader);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);

        var (otherFileKey, sealedToAnotherKey) = ZkBrowser.SealNewFileKey(ZkBrowser.MakeKeyPair().PublicKeyJwk);
        // The upload client throws on any 400 rather than returning it
        var e = Assert.ThrowsAsync<Exception>(() => app.Drives.Writer.CreateEncryptedFile(Drive.Alias, Metadata(Guid.NewGuid()),
            transitOptions: null!, keyHeader: otherFileKey, lockedKeyHeader: sealedToAnotherKey));
        Assert.That(e!.Message, Does.EndWith(nameof(OdinClientErrorCode.SealedToUnknownKey)));
    }

    [Test]
    public async Task ASenderKeepsItsCopyOfAKeyUntilADepositSealedWithItIsRefused()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.ConnectAsync(frodo, sam);
        var (samsZkMasterKey, _, _) = await ZkOwnerSteps.MakeZeroKnowledgeAsync(sam, OwnerLogin.DefaultPassword);
        var oldKey = (await ZkFriends.GetShardRecoveryPublicKeyAsync(frodo, sam)).PublicKeyJwk;

        // Converted again, as after a reset: a new write-only key, and the old one is gone
        var newKey = (await ZkOwnerSteps.ConvertShardRecoveryDriveAsync(sam, samsZkMasterKey)).WriteOnlyPublicKeyJwk;
        Assert.That(newKey, Is.Not.EqualTo(oldKey));
        Assert.That((await ZkFriends.GetShardRecoveryPublicKeyAsync(frodo, sam)).PublicKeyJwk, Is.EqualTo(oldKey),
            "Frodo's server answers from its copy");

        var uniqueId = Guid.NewGuid();
        await SendDepositAsync(frodo, sam, oldKey, uniqueId);
        var received = await sam.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, Drive.Alias);
        Assert.That(received.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "refused, not stored where nobody can open it");

        Assert.That((await ZkFriends.GetShardRecoveryPublicKeyAsync(frodo, sam)).PublicKeyJwk, Is.EqualTo(newKey),
            "fetched again after the refusal");
    }

    [Test]
    public async Task AnAnswerFromADriveThatIsNotZeroKnowledgeYetIsNotKept()
    {
        // Such a drive takes no deposits, and becomes zero knowledge at its owner's next login
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.ConnectAsync(frodo, sam);
        Assert.That((await ZkFriends.GetShardRecoveryPublicKeyAsync(frodo, sam)).IsZeroKnowledge, Is.False);

        await ZkOwnerSteps.MakeZeroKnowledgeAsync(sam, OwnerLogin.DefaultPassword);
        Assert.That((await ZkFriends.GetShardRecoveryPublicKeyAsync(frodo, sam)).IsZeroKnowledge, Is.True);
    }

    /// <summary>Frodo writes a deposit to his transient drive and sends it to Sam's Shard Recovery drive.</summary>
    private static async Task SendDepositAsync(OwnerSession frodo, OwnerSession sam, string publicKeyJwk, Guid uniqueId)
    {
        var (fileKey, sealedKeyHeader) = ZkBrowser.SealNewFileKey(publicKeyJwk);
        var metadata = Metadata(uniqueId);
        metadata.AllowDistribution = true;
        var (upload, _, _, _) = await frodo.Drives.Writer.CreateEncryptedFile(SystemDriveConstants.TransientTempDrive.Alias, metadata,
            new TransitOptions { IsTransient = true, Recipients = [sam.Identity.DomainName], RemoteTargetDrive = Drive },
            keyHeader: fileKey, lockedKeyHeader: sealedKeyHeader);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
        await frodo.Sync.DrainOutboxAsync();
    }

    private static UploadFileMetadata Metadata(Guid uniqueId) => ZkTestFiles.Metadata(uniqueId, Content);
}
