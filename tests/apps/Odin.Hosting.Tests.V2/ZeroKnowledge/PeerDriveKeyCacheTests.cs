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

using static Odin.Hosting.Tests.V2.ZeroKnowledge.ZkTestFiles;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// A deposit must be sealed to the drive's write-only key, and a sender's server keeps a copy of a peer drive's key
/// until a deposit sealed with it is refused (docs/drive-public-keys-plan.md).  Deposits on the Shard Recovery
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
        var app = await AppSession.SetupAsync(sam, Drive, DrivePermission.ReadWrite);

        var (fileKey, sealedKeyHeader) = ZkBrowser.SealNewFileKey(drivesKey);
        var (upload, _, _, _) = await app.Drives.Writer.CreateEncryptedFile(Drive.Alias, Metadata(Guid.NewGuid(), Content),
            transitOptions: null!, keyHeader: fileKey, lockedKeyHeader: sealedKeyHeader);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);

        var (otherFileKey, sealedToAnotherKey) = ZkBrowser.SealNewFileKey(ZkBrowser.MakeKeyPair().PublicKeyJwk);
        // The upload client throws on any 400 rather than returning it
        var e = Assert.ThrowsAsync<Exception>(() => app.Drives.Writer.CreateEncryptedFile(Drive.Alias, Metadata(Guid.NewGuid(), Content),
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
        await SendDepositAsync(frodo, frodo, sam, oldKey, uniqueId, Content);
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
        var before = await ZkFriends.GetShardRecoveryPublicKeyAsync(frodo, sam);
        Assert.That((before.IsZeroKnowledge, before.SharingModel), Is.EqualTo((false, DriveSharingModel.Collaborative)));

        await ZkOwnerSteps.MakeZeroKnowledgeAsync(sam, OwnerLogin.DefaultPassword);
        var after = await ZkFriends.GetShardRecoveryPublicKeyAsync(frodo, sam);
        Assert.That((after.IsZeroKnowledge, after.SharingModel), Is.EqualTo((true, DriveSharingModel.Private)),
            "the fresh answer says what the key is now bound to");
    }
}
