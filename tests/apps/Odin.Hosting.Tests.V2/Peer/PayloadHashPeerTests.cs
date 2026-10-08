#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Peer;

/// <summary>
/// Payload hashes over peer (#1895, docs/payload-hashes.md rule 8): the stored bytes are identical on both
/// servers, so the hash travels unchanged, and the receiving server verifies storedHash and applies its own
/// drive's requirePayloadHashes.
/// </summary>
[TestFixture]
public class PayloadHashPeerTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task AnUnencryptedHashArrivesUnchanged()
    {
        var (frodo, sam, drive) = await ConnectAsync();

        var payload = Hashed(SamplePayloadDefinitions.GetPayloadDefinition1());
        var sent = await SendUnencrypted(frodo, sam, drive, payload);

        await frodo.Sync.DrainOutboxAsync();
        await sam.Sync.ProcessInboxAsync(drive);

        var received = await ReceivedDescriptor(sam, drive, sent.GlobalTransitId!.Value, payload.Key);
        AssertSameHash(received?.Hash, payload.Hash);
    }

    [Test]
    public async Task AnEncryptedHashArrivesUnchanged()
    {
        var (frodo, sam, drive) = await ConnectAsync();

        var (sent, payload) = await SendEncrypted(frodo, sam, drive);

        await frodo.Sync.DrainOutboxAsync();
        await sam.Sync.ProcessInboxAsync(drive);

        // Sam's server verified the ciphertext against storedHash on receipt; the opaque content hash came along as-is
        var received = await ReceivedDescriptor(sam, drive, sent.GlobalTransitId!.Value, payload.Key);
        AssertSameHash(received?.Hash, payload.Hash);
    }

    [Test]
    public async Task TheReceivingServerRejectsBytesThatDoNotMatchAndKeepsNothing()
    {
        var (frodo, sam, drive) = await ConnectAsync();

        var payload = Hashed(SamplePayloadDefinitions.GetPayloadDefinition1());
        var sent = await SendUnencrypted(frodo, sam, drive, payload);

        await CorruptStoredPayload(frodo, drive, sent, payload.Key);

        await frodo.Sync.DrainOutboxAsync();
        await sam.Sync.ProcessInboxAsync(drive);

        await DriveAsserts.AssertTransferStatus(frodo, FileOf(sent, drive), sam.Identity, LatestTransferStatus.RecipientIdentityReturnedBadRequest);
        Assert.That(await ReceivedDescriptor(sam, drive, sent.GlobalTransitId!.Value, payload.Key), Is.Null);
        AssertNoPayloadFiles(sam, drive);
    }

    [Test]
    public async Task TheReceivingServerRejectsCorruptedEncryptedBytesAndKeepsNothing()
    {
        // A write-only grant on a non-anonymous drive gives Frodo no storage key on Sam's drive, so Sam's server
        // routes the encrypted file through the inbox, which streams payloads straight to long-term storage
        var (frodo, sam, drive) = await ConnectAsync(allowAnonymousReads: false);

        var (sent, payload) = await SendEncrypted(frodo, sam, drive);
        await CorruptStoredPayload(frodo, drive, sent, payload.Key);

        await frodo.Sync.DrainOutboxAsync();
        await sam.Sync.ProcessInboxAsync(drive);

        await DriveAsserts.AssertTransferStatus(frodo, FileOf(sent, drive), sam.Identity, LatestTransferStatus.RecipientIdentityReturnedBadRequest);
        Assert.That(await ReceivedDescriptor(sam, drive, sent.GlobalTransitId!.Value, payload.Key), Is.Null);
        AssertNoPayloadFiles(sam, drive);
    }

    [Test]
    public async Task TheReceivingDriveCanRequireHashes()
    {
        var (frodo, sam, drive) = await ConnectAsync();
        await sam.Admin.SetRequirePayloadHashes(drive, true);

        var unhashed = await SendUnencrypted(frodo, sam, drive, SamplePayloadDefinitions.GetPayloadDefinition1());
        var hashedPayload = Hashed(SamplePayloadDefinitions.GetPayloadDefinition2());
        var hashed = await SendUnencrypted(frodo, sam, drive, hashedPayload);

        await frodo.Sync.DrainOutboxAsync();
        await sam.Sync.ProcessInboxAsync(drive);

        await DriveAsserts.AssertTransferStatus(frodo, FileOf(unhashed, drive), sam.Identity, LatestTransferStatus.RecipientIdentityReturnedBadRequest);
        await DriveAsserts.AssertTransferStatus(frodo, FileOf(hashed, drive), sam.Identity, LatestTransferStatus.Delivered);

        var received = await ReceivedDescriptor(sam, drive, hashed.GlobalTransitId!.Value, hashedPayload.Key);
        AssertSameHash(received?.Hash, hashedPayload.Hash);
    }

    //
    // Helpers
    //

    private async Task<(OwnerSession frodo, OwnerSession sam, TargetDrive drive)> ConnectAsync(bool allowAnonymousReads = true)
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Write, "payload hashes",
            allowAnonymousReads: allowAnonymousReads);
        return (frodo, sam, drive);
    }

    /// <summary>
    /// Corrupts the sender's stored copy after its server accepted it, so the bytes the recipient's server receives no
    /// longer match storedHash. The outbox only runs when drained, so nothing has been sent yet.
    /// </summary>
    private async Task CorruptStoredPayload(OwnerSession sender, TargetDrive drive, CreateFileResult sent, string key)
    {
        var descriptor = (await sender.Drives.Reader.GetFileHeaderAsync(sent.DriveId, sent.FileId)).Content!
            .FileMetadata.Payloads.Single(p => p.Key == key);
        var storedPath = Paths(sender).GetPayloadDirectoryAndFileName(drive.Alias, sent.FileId, key, descriptor.Uid);
        var bytes = await File.ReadAllBytesAsync(storedPath);
        bytes[0] ^= 0xFF;
        await File.WriteAllBytesAsync(storedPath, bytes);
    }

    /// <summary>
    /// Sends an encrypted file with one hashed payload: storedHash over the ciphertext, contentHash over the plaintext
    /// encrypted with the file key under the derived IV
    /// </summary>
    private static async Task<(CreateFileResult sent, TestPayloadDefinition payload)> SendEncrypted(OwnerSession frodo,
        OwnerSession sam, TargetDrive drive)
    {
        var keyHeader = KeyHeader.NewRandom16();
        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        payload.Iv = ByteArrayUtil.GetRndByteArray(16);
        var ciphertext = new KeyHeader { Iv = payload.Iv, AesKey = new SensitiveByteArray(keyHeader.AesKey.GetKey()) }
            .EncryptDataAes(payload.Content);
        payload.Hash = new PayloadHash
        {
            Algorithm = ContentHashAlgorithm.Blake3,
            StoredHash = IncrementalContentHash.Compute(ContentHashAlgorithm.Blake3, ciphertext),
            ContentHash = AesCbc.Encrypt(IncrementalContentHash.Compute(ContentHashAlgorithm.Blake3, payload.Content),
                keyHeader.AesKey.GetKey(), PayloadHash.ContentHashIv(payload.Iv))
        };

        var metadata = SampleMetadataData.CreateWithContent(fileType: 100, "encrypted app content", AccessControlList.Connected);
        metadata.AllowDistribution = true;
        var (response, _, _, _) = await frodo.Drives.Writer.CreateEncryptedFile(drive.Alias, metadata,
            new TransitOptions { Recipients = [sam.Identity] },
            new UploadManifest { PayloadDescriptors = new[] { payload }.ToPayloadDescriptorList().ToList() }, [payload],
            keyHeader: keyHeader);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"actual {response.StatusCode}");
        return (response.Content!, payload);
    }

    private static ExternalFileIdentifier FileOf(CreateFileResult result, TargetDrive drive) =>
        new() { FileId = result.FileId, TargetDrive = drive };

    private static TestPayloadDefinition Hashed(TestPayloadDefinition payload)
    {
        var hash = IncrementalContentHash.Compute(ContentHashAlgorithm.Blake3, payload.Content);
        payload.Hash = new PayloadHash { Algorithm = ContentHashAlgorithm.Blake3, StoredHash = hash, ContentHash = (byte[])hash.Clone() };
        return payload;
    }

    private static async Task<CreateFileResult> SendUnencrypted(OwnerSession frodo, OwnerSession sam, TargetDrive drive,
        TestPayloadDefinition payload)
    {
        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        metadata.AllowDistribution = true;

        var response = await frodo.Drives.Writer.CreateNewUnencryptedFile(drive.Alias, metadata,
            new UploadManifest { PayloadDescriptors = new[] { payload }.ToPayloadDescriptorList().ToList() }, [payload],
            new TransitOptions { Recipients = [sam.Identity] });
        Assert.That(response.IsSuccessStatusCode, Is.True, $"actual {response.StatusCode}");
        return response.Content!;
    }

    private static async Task<PayloadDescriptor?> ReceivedDescriptor(OwnerSession sam, TargetDrive drive,
        System.Guid globalTransitId, string key)
    {
        var query = await sam.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { GlobalTransitId = [globalTransitId] },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true }
        });
        Assert.That(query.IsSuccessStatusCode, Is.True, $"Sam query failed: {query.StatusCode}");
        return query.Content!.SearchResults.SingleOrDefault()?.FileMetadata.Payloads.SingleOrDefault(p => p.Key == key);
    }

    private void AssertNoPayloadFiles(OwnerSession owner, TargetDrive drive)
    {
        var paths = Paths(owner);
        var leftovers = new List<string>();
        foreach (var directory in new[] { paths.GetDrivePayloadPath(drive.Alias), paths.GetDriveUploadPath(drive.Alias) })
        {
            if (Directory.Exists(directory))
            {
                // Payload bytes only: the transfer's .metadata/.transferkeyheader staging files are written before any
                // payload arrives and are not removed when a peer transfer fails for any reason (pre-existing: #1897)
                leftovers.AddRange(Directory.GetFiles(directory, "*.payload", SearchOption.AllDirectories));
            }
        }

        Assert.That(leftovers, Is.Empty, "the rejected payload must not be left behind on the receiving server");
    }

    private Odin.Services.Drives.FileSystem.Base.TenantPathManager Paths(OwnerSession owner) =>
        Host.GetTenantScope(owner.Identity.DomainName).Resolve<TenantContext>().TenantPathManager;

    private static void AssertSameHash(PayloadHash? actual, PayloadHash expected)
    {
        Assert.That(actual, Is.Not.Null, "the received descriptor has no hash");
        Assert.That(actual!.Algorithm, Is.EqualTo(expected.Algorithm));
        Assert.That(actual.StoredHash, Is.EqualTo(expected.StoredHash));
        Assert.That(actual.ContentHash, Is.EqualTo(expected.ContentHash));
    }
}
