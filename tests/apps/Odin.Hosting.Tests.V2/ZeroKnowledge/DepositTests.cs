#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Security.ZeroKnowledge;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// Deposits on zero-knowledge drives (docs/zero-knowledge-recovery-plan.md, "Deposits"): a file whose
/// writer sealed its key header to the drive's write-only public key.  The server stores the sealed key
/// header as received, passes it on as is and returns it as stored; only the drive owner's browser opens
/// it.  On the drive it is a client-locked file, under the same rules as a zkDrive one
/// (docs/zero-knowledge-drive-api-plan.md, step 2).  Gated friend shards are the first deposits; these
/// tests use plain files.
/// </summary>
[TestFixture]
public class DepositTests : V2Fixture
{
    private const string Content = "only the drive's owner reads this";

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    private static readonly TargetDrive Drive = WellKnownAppDrives.ShardRecoveryDrive;

    [Test]
    public async Task TheOwnerCannotUploadADepositToTheirOwnZeroKnowledgeDrive()
    {
        // The owner's devices hold the drive's key and write zkDrive; a deposit comes only from someone else
        var frodo = await LoginAsOwner(Identities.Frodo);
        await ZkOwnerSteps.MakeZeroKnowledgeAsync(frodo, OwnerLogin.DefaultPassword);
        var publicKeyJwk = (await ZkOwnerSteps.GetShardRecoveryDriveAsync(frodo)).WriteOnlyPublicKeyJwk;
        var (fileKey, sealedKeyHeader) = ZkBrowser.SealNewFileKey(publicKeyJwk);

        // The upload client throws on any 400 rather than returning it.
        var e = Assert.ThrowsAsync<Exception>(() => frodo.Drives.Writer.CreateEncryptedFile(Drive.Alias, Metadata(Guid.NewGuid()),
            transitOptions: null!, keyHeader: fileKey, lockedKeyHeader: sealedKeyHeader));
        Assert.That(e!.Message, Does.EndWith(nameof(OdinClientErrorCode.DriveIsZeroKnowledge)));
    }

    [Test]
    public async Task ADepositIsRefusedOnADriveThatIsNotZeroKnowledge()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var (fileKey, sealedKeyHeader) = ZkBrowser.SealNewFileKey(ZkBrowser.MakeKeyPair().PublicKeyJwk);

        // The upload client throws on any 400 rather than returning it.
        var e = Assert.ThrowsAsync<Exception>(() => frodo.Drives.Writer.CreateEncryptedFile(Drive.Alias, Metadata(Guid.NewGuid()),
            transitOptions: null!, keyHeader: fileKey, lockedKeyHeader: sealedKeyHeader));
        Assert.That(e!.Message, Does.EndWith(nameof(OdinClientErrorCode.DepositNeedsZeroKnowledgeDrive)));
    }

    [Test]
    public async Task ADepositCarriesNoPayloads()
    {
        // On the transient drive, where a deposit is written to be sent
        var frodo = await LoginAsOwner(Identities.Frodo);
        var (fileKey, sealedKeyHeader) = ZkBrowser.SealNewFileKey(ZkBrowser.MakeKeyPair().PublicKeyJwk);

        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        payload.Iv = ByteArrayUtil.GetRndByteArray(16);
        var manifest = new UploadManifest { PayloadDescriptors = [payload.ToPayloadDescriptor()] };

        var e = Assert.ThrowsAsync<Exception>(() => frodo.Drives.Writer.CreateEncryptedFile(SystemDriveConstants.TransientTempDrive.Alias,
            Metadata(Guid.NewGuid()), transitOptions: null!, manifest, [payload], keyHeader: fileKey, lockedKeyHeader: sealedKeyHeader));
        Assert.That(e!.Message, Does.EndWith(nameof(OdinClientErrorCode.InvalidPayload)));
    }

    [TestCase(true, TestName = "ADepositSentToAPeersZeroKnowledgeDriveArrivesSealed")]
    [TestCase(false, TestName = "ADepositSentToAPeersDriveThatIsNotZeroKnowledgeIsRefusedThere")]
    public async Task ADepositIsSentAsIs(bool recipientIsZeroKnowledge)
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.ConnectAsync(frodo, sam);

        var samsKey = recipientIsZeroKnowledge ? (await ZkOwnerSteps.MakeZeroKnowledgeAsync(sam, OwnerLogin.DefaultPassword)).WriteOnlyPrivateKey : null;
        var samsPublicKey = await ZkFriends.GetShardRecoveryPublicKeyAsync(frodo, sam);
        Assert.That(samsPublicKey.IsZeroKnowledge, Is.EqualTo(recipientIsZeroKnowledge));

        // Written to Frodo's transient drive only to be sent: his server can't open it either
        var uniqueId = Guid.NewGuid();
        var sealedKeyHeader = await SendDepositAsync(frodo, sam, samsPublicKey.PublicKeyJwk, uniqueId);

        var received = await sam.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, Drive.Alias);
        if (!recipientIsZeroKnowledge)
        {
            Assert.That(received.StatusCode, Is.Not.EqualTo(HttpStatusCode.OK), "a drive that isn't zero knowledge takes no deposit");
            return;
        }

        Assert.That(received.StatusCode, Is.EqualTo(HttpStatusCode.OK), received.Error?.Content);
        AssertOpens(received.Content!, samsKey!, sealedKeyHeader);
        Assert.That(received.Content!.FileMetadata.SenderOdinId, Is.EqualTo(frodo.Identity.DomainName));

        // A query needs no storage key for it: its key header goes back as stored
        var batch = await sam.Drives.Reader.GetBatchAsync(Drive.Alias, QueryFor(uniqueId));
        Assert.That(batch.StatusCode, Is.EqualTo(HttpStatusCode.OK), batch.Error?.Content);
        AssertOpens(batch.Content!.SearchResults.Single(), samsKey!, sealedKeyHeader);
    }

    [Test]
    public async Task AReceivedDepositIsHiddenFromAnAppWithReadOnTheDrive()
    {
        // A client-locked file reaches only a caller holding the drive's key: an app holds none for a
        // zero-knowledge drive, and an old chat-kmp stalls its sync on a key header it can't open
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.ConnectAsync(frodo, sam);
        await ZkOwnerSteps.MakeZeroKnowledgeAsync(sam, OwnerLogin.DefaultPassword);
        var app = await AppSession.SetupAsync(sam, Drive, DrivePermission.Read);

        var samsPublicKey = await ZkFriends.GetShardRecoveryPublicKeyAsync(frodo, sam);
        var uniqueId = Guid.NewGuid();
        await SendDepositAsync(frodo, sam, samsPublicKey.PublicKeyJwk, uniqueId);

        var owners = await sam.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, Drive.Alias);
        Assert.That(owners.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the owner sees it");

        var batch = await app.Drives.Reader.GetBatchAsync(Drive.Alias, QueryFor(uniqueId));
        Assert.That(batch.StatusCode, Is.EqualTo(HttpStatusCode.OK), batch.Error?.Content);
        Assert.That(batch.Content!.SearchResults, Is.Empty, "left out of the app's page");

        var byUniqueId = await app.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, Drive.Alias);
        Assert.That(byUniqueId.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), $"by unique id: {byUniqueId.Error?.Content}");
        var byFileId = await app.Drives.Reader.GetFileHeaderAsync(Drive.Alias, owners.Content!.FileId);
        Assert.That(byFileId.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), $"by file id: {byFileId.Error?.Content}");
    }

    /// <summary>Frodo writes a deposit to his transient drive and sends it to Sam's Shard Recovery drive.</summary>
    private static async Task<EncryptedKeyHeader> SendDepositAsync(
        OwnerSession frodo, OwnerSession sam, string publicKeyJwk, Guid uniqueId)
    {
        var (fileKey, sealedKeyHeader) = ZkBrowser.SealNewFileKey(publicKeyJwk);
        var metadata = Metadata(uniqueId);
        metadata.AllowDistribution = true;
        var (upload, _, _, _) = await frodo.Drives.Writer.CreateEncryptedFile(SystemDriveConstants.TransientTempDrive.Alias, metadata,
            new TransitOptions
            {
                IsTransient = true,
                Recipients = [sam.Identity.DomainName],
                RemoteTargetDrive = Drive
            }, keyHeader: fileKey, lockedKeyHeader: sealedKeyHeader);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
        await frodo.Sync.DrainOutboxAsync();
        return sealedKeyHeader;
    }

    private static QueryBatchRequest QueryFor(Guid uniqueId) => new()
    {
        QueryParams = new FileQueryParamsV1 { ClientUniqueIdAtLeastOne = [uniqueId] },
        ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true }
    };

    private static UploadFileMetadata Metadata(Guid uniqueId)
    {
        var metadata = SampleMetadataData.CreateWithContent(fileType: 100, Content, AccessControlList.OwnerOnly);
        metadata.AppData.UniqueId = uniqueId;
        return metadata;
    }

    /// <summary>The key header came back as it was sent, and the drive's write-only private key opens the content.</summary>
    private static void AssertOpens(SharedSecretEncryptedFileHeader header, byte[] writeOnlyPrivateKey, EncryptedKeyHeader sent)
    {
        Assert.That(header.SharedSecretEncryptedKeyHeader.EncryptedAesKey, Is.EqualTo(sent.EncryptedAesKey), "stored and returned as it came");
        Assert.That(ZkBrowser.OpenDeposit(header, writeOnlyPrivateKey).ToStringFromUtf8Bytes(), Is.EqualTo(Content));
    }
}
