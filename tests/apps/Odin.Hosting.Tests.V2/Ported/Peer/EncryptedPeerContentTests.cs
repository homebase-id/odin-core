using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Exercises the security-critical part of the peer transport: the shared-secret re-encryption path.
/// When a member reads an ENCRYPTED file from an owner-hosted drive over peer, the server must re-wrap
/// the file's key header from the ICR-shared-secret into the MEMBER's own shared secret
/// (<c>PeerDriveQueryService.TransformSharedSecret</c>/<c>ReEncrypt</c>). These tests prove the member
/// (and, for writes, the owner) can decrypt with their OWN shared secret. Decryption pattern reused
/// from <c>Ported/DriveRead/GetFileByUniqueIdTests</c> and <c>Peer/PeerScenarioTests</c>.
/// </summary>
[TestFixture]
public class EncryptedPeerContentTests : V2Fixture
{
    private const int CommunityMessageFileType = 7020;

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task Member_ReadsEncryptedHeaderAndPayloadOverPeer()
    {
        var member = await LoginAsOwner(Identities.Frodo);
        var owner = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(member, owner, DrivePermission.Read, "community",
            allowAnonymousReads: false);

        var keyHeader = KeyHeader.NewRandom16();
        const string plaintext = "secret community message";
        var metadata = SampleMetadataData.Create(fileType: CommunityMessageFileType, acl: AccessControlList.Connected);
        metadata.AppData.Content = plaintext;
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        payload.Iv = ByteArrayUtil.GetRndByteArray(16); // encrypted payloads require a per-file IV
        var payloads = new List<TestPayloadDefinition> { payload };
        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };

        var (upload, _, _, _) = await owner.Drives.Writer.CreateEncryptedFile(
            drive.Alias, metadata, new TransitOptions(), manifest, payloads, notificationOptions: null, keyHeader);
        Assert.That(upload.IsSuccessStatusCode, Is.True, $"owner encrypted upload failed: {upload.StatusCode}");
        var fileId = upload.Content!.FileId;

        // Header over peer → decrypt content with the MEMBER's own shared secret.
        var headerResp = await member.Drives.Peer.GetFileHeaderAsync(owner.Identity, drive.Alias, fileId);
        Assert.That(headerResp.IsSuccessStatusCode, Is.True, $"peer header failed: {headerResp.StatusCode}");
        var header = headerResp.Content!;
        Assert.That(header.FileMetadata.IsEncrypted, Is.True);

        var memberSecret = member.Drives.Reader.GetSharedSecret();
        var headerKeyHeader = header.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref memberSecret);
        var decryptedContent = headerKeyHeader.Decrypt(header.FileMetadata.AppData.Content.FromBase64()).ToStringFromUtf8Bytes();
        Assert.That(decryptedContent, Is.EqualTo(plaintext),
            "header content must decrypt with the member's own shared secret (proves ICR→owner re-encryption)");

        // Payload over peer → decrypt with the MEMBER's own shared secret.
        var payloadResp = await member.Drives.Peer.GetPayloadAsync(owner.Identity, drive.Alias, fileId, payload.Key);
        Assert.That(payloadResp.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"peer payload failed: {payloadResp.StatusCode}");
        Assert.That(payloadResp.Headers!.TryGetValues(HttpHeaderConstants.PayloadEncrypted, out var encVals), Is.True);
        Assert.That(bool.Parse(encVals!.Single()), Is.True, "payload should be reported encrypted");
        Assert.That(payloadResp.Headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedKeyHeader64, out var ekhVals), Is.True);

        var payloadEkh = EncryptedKeyHeader.FromBase64(ekhVals!.Single());
        var memberSecret2 = member.Drives.Reader.GetSharedSecret();
        var payloadKeyHeader = payloadEkh.DecryptAesToKeyHeader(ref memberSecret2);
        var decryptedPayload = payloadKeyHeader.Decrypt(await payloadResp.Content!.ReadAsByteArrayAsync());
        Assert.That(ByteArrayUtil.EquiByteArrayCompare(decryptedPayload, payload.Content), Is.True,
            "payload must decrypt with the member's own shared secret over peer");
    }

    [Test]
    public async Task Member_WritesEncryptedFileToOwnerOverPeer()
    {
        var member = await LoginAsOwner(Identities.Frodo);
        var owner = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(member, owner, DrivePermission.Write, "community",
            allowAnonymousReads: false);

        var keyHeader = KeyHeader.NewRandom16();
        const string plaintext = "encrypted message written over peer";
        var metadata = SampleMetadataData.Create(fileType: CommunityMessageFileType, acl: AccessControlList.Connected,
            allowDistribution: true);
        metadata.AppData.Content = plaintext;

        var send = await member.Drives.PeerWriter.SendEncryptedFileOverPeer(owner.Identity, drive, metadata, keyHeader);
        Assert.That(send.IsSuccessStatusCode, Is.True, $"encrypted write-over-peer failed: {send.StatusCode}");
        var gtid = send.Content!.RemoteGlobalTransitIdFileIdentifier?.GlobalTransitId
                   ?? throw new InvalidOperationException("write-over-peer must yield a remote GlobalTransitId");

        await member.Sync.DrainOutboxAsync();
        await owner.Sync.ProcessInboxAsync(drive);

        var q = await owner.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { GlobalTransitId = new[] { gtid } },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true }
        });
        Assert.That(q.IsSuccessStatusCode, Is.True, $"owner local query failed: {q.StatusCode}");
        var header = q.Content!.SearchResults.SingleOrDefault();
        Assert.That(header, Is.Not.Null, "owner should have received the encrypted file over peer");
        Assert.That(header!.FileMetadata.IsEncrypted, Is.True);

        // Owner decrypts with the OWNER's own shared secret (inbox re-encrypted the key header for the owner).
        var ownerSecret = owner.Drives.Reader.GetSharedSecret();
        var kh = header.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ownerSecret);
        var decrypted = kh.Decrypt(header.FileMetadata.AppData.Content.FromBase64()).ToStringFromUtf8Bytes();
        Assert.That(decrypted, Is.EqualTo(plaintext));
    }
}
