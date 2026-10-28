#nullable enable
using System;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;

using static Odin.Hosting.Tests.V2.ZeroKnowledge.ZkTestFiles;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// A drive's write-only keys, current and retired, and the sender's cached copy of them
/// (docs/drive-key-retirement-plan.md).  Deposits on the Shard Recovery drive, a zero-knowledge drive.
/// </summary>
[TestFixture]
public class DriveKeyRetirementTests : V2Fixture
{
    private const string Content = "sealed to one of the drive's keys";

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task ADepositSealedToTheCurrentOrARetiredKeyIsTakenAndToAnUnknownOneRefused()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        await ZkOwnerSteps.MakeZeroKnowledgeAsync(sam, OwnerLogin.DefaultPassword);
        var oldKey = (await ZkOwnerSteps.GetShardRecoveryDriveAsync(sam)).WriteOnlyPublicKeyJwk;
        var app = await AppSession.SetupAsync(sam, Drive, DrivePermission.ReadWrite);
        await DepositAsync(app, oldKey);

        var newKey = await ReplaceKeyAsync(sam);
        await DepositAsync(app, newKey);
        await DepositAsync(app, oldKey);

        var (fileKey, sealedToUnknownKey) = ZkBrowser.SealNewFileKey(ZkBrowser.MakeKeyPair().PublicKeyJwk);
        // The upload client throws on any 400 rather than returning it
        var e = Assert.ThrowsAsync<Exception>(() => app.Drives.Writer.CreateEncryptedFile(Drive.Alias, Metadata(Guid.NewGuid()),
            transitOptions: null!, keyHeader: fileKey, lockedKeyHeader: sealedToUnknownKey));
        Assert.That(e!.Message, Does.EndWith(nameof(OdinClientErrorCode.SealedToUnknownKey)));
    }

    [Test]
    public async Task ARetiredKeyStopsTakingSealsAfterItsTimeButStillOpensWhatItTook()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        await ZkOwnerSteps.MakeZeroKnowledgeAsync(sam, OwnerLogin.DefaultPassword);
        var oldKey = (await ZkOwnerSteps.GetShardRecoveryDriveAsync(sam)).WriteOnlyPublicKeyJwk;
        await ReplaceKeyAsync(sam);

        var scope = sam.Host.GetTenantScope(sam.Identity.DomainName);
        var driveManager = scope.Resolve<IDriveManager>();
        var acceptFor = scope.Resolve<OdinConfiguration>().Host.RetiredDriveKeyAcceptAge;
        var drive = await driveManager.GetDriveAsync(Drive.Alias, failIfInvalid: true);
        var (_, sealedToOldKey) = ZkBrowser.SealNewFileKey(oldKey);

        var now = UnixTimeUtc.Now();
        Assert.That(await driveManager.MatchSealedKeyAsync(drive, sealedToOldKey, now), Is.EqualTo(SealedTo.RetiredKey));

        var later = now.AddMilliseconds((long)acceptFor.TotalMilliseconds + 60_000);
        var e = Assert.ThrowsAsync<OdinClientException>(() => driveManager.MatchSealedKeyAsync(drive, sealedToOldKey, later));
        Assert.That(e!.ErrorCode, Is.EqualTo(OdinClientErrorCode.SealedToUnknownKey));

        Assert.That(await driveManager.HoldsKeyForAsync(drive, sealedToOldKey.EncryptedAesKey), Is.True,
            "its private half stays, so what was sealed to it opens");
        var (_, sealedToUnknownKey) = ZkBrowser.SealNewFileKey(ZkBrowser.MakeKeyPair().PublicKeyJwk);
        Assert.That(await driveManager.HoldsKeyForAsync(drive, sealedToUnknownKey.EncryptedAesKey), Is.False);
    }

    [Test]
    public async Task ASenderKeepsItsCopyOfAKeyUntilADepositSealedWithItIsAnsweredReplaced()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.ConnectAsync(frodo, sam);
        await ZkOwnerSteps.MakeZeroKnowledgeAsync(sam, OwnerLogin.DefaultPassword);
        var oldKey = (await ZkFriends.GetShardRecoveryPublicKeyAsync(frodo, sam)).PublicKeyJwk;

        var newKey = await ReplaceKeyAsync(sam);
        Assert.That((await ZkFriends.GetShardRecoveryPublicKeyAsync(frodo, sam)).PublicKeyJwk, Is.EqualTo(oldKey),
            "Frodo's server answers from its copy");

        // Sealed with Frodo's out-of-date copy: Sam's drive takes it and says "replaced", and Frodo's server forgets
        var uniqueId = Guid.NewGuid();
        await SendDepositAsync(frodo, sam, oldKey, uniqueId);
        var received = await sam.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, Drive.Alias);
        Assert.That(received.StatusCode, Is.EqualTo(HttpStatusCode.OK), received.Error?.Content);

        Assert.That((await ZkFriends.GetShardRecoveryPublicKeyAsync(frodo, sam)).PublicKeyJwk, Is.EqualTo(newKey),
            "fetched again after the answer");
    }

    /// <summary>
    /// Gives <paramref name="owner"/>'s Shard Recovery drive a new write-only key pair, as a rotation in the browser
    /// would, retiring the old one; returns the new public key.
    /// </summary>
    private static async Task<string> ReplaceKeyAsync(OwnerSession owner)
    {
        var pair = ZkBrowser.MakeKeyPair();
        var driveManager = owner.Host.GetTenantScope(owner.Identity.DomainName).Resolve<DriveManager>();
        var ownerContext = new OdinContext
        {
            Caller = new CallerContext((OdinId)owner.Identity.DomainName, new SensitiveByteArray(Guid.NewGuid().ToByteArray()),
                SecurityGroupType.Owner)
        };
        await driveManager.ReplaceWriteOnlyKeyPairAsync(Drive.Alias, DriveWriteOnlyKey.FromPublicKeyJwk(pair.PublicKeyJwk),
            "locked by the browser", ownerContext);
        return pair.PublicKeyJwk;
    }

    private static async Task DepositAsync(IV2Caller app, string publicKeyJwk)
    {
        var (fileKey, sealedKeyHeader) = ZkBrowser.SealNewFileKey(publicKeyJwk);
        var (upload, _, _, _) = await app.Drives.Writer.CreateEncryptedFile(Drive.Alias, Metadata(Guid.NewGuid()),
            transitOptions: null!, keyHeader: fileKey, lockedKeyHeader: sealedKeyHeader);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
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

    private static Odin.Services.Drives.FileSystem.Base.Upload.UploadFileMetadata Metadata(Guid uniqueId) =>
        ZkTestFiles.Metadata(uniqueId, Content);
}
