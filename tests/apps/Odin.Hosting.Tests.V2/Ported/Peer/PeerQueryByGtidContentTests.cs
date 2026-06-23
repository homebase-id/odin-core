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
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// V2 "over peer" read of a file addressed by GlobalTransitId — the V2 equivalent of the V1
/// <c>/api/apps/v1/transit/query/{header,payload,thumb}_byglobaltransitid</c> endpoints. Sibling of
/// <see cref="PeerQueryAndContentTests"/> (which reads the same content by FileId).
///
/// Layout: <c>owner</c> (Sam) hosts the drive and uploads files locally; <c>member</c> (Frodo) is
/// connected and granted Read on the owner's drive, then reads over peer by GlobalTransitId.
/// </summary>
[TestFixture]
public class PeerQueryByGtidContentTests : V2Fixture
{
    private const int CommunityMessageFileType = 7020;

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task Member_CanGetHeaderAndPayloadAndThumbnailByGtidOverPeer()
    {
        var (member, owner, drive) = await SetupMemberCanReadOwnerDriveAsync();

        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var (_, gtid) = await OwnerUploadsFileAsync(owner, drive, payload);

        // header
        var header = await member.Drives.Peer.GetFileHeaderByGtidAsync(owner.Identity, drive.Alias, gtid);
        Assert.That(header.IsSuccessStatusCode, Is.True, $"peer header-by-gtid failed: {header.StatusCode}");
        Assert.That(header.Content!.FileMetadata.GlobalTransitId, Is.EqualTo(gtid));
        Assert.That(header.Content!.FileMetadata.Payloads.Count(), Is.EqualTo(1));

        // payload (unencrypted → bytes come back verbatim)
        var payloadResponse = await member.Drives.Peer.GetPayloadByGtidAsync(owner.Identity, drive.Alias, gtid, payload.Key);
        Assert.That(payloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), "peer payload-by-gtid should be 200");
        var payloadBytes = await payloadResponse.Content!.ReadAsByteArrayAsync();
        Assert.That(payloadBytes.ToBase64(), Is.EqualTo(payload.Content.ToBase64()), "payload content mismatch over peer");

        // thumbnail
        var thumb = payload.Thumbnails.Single();
        var thumbResponse = await member.Drives.Peer.GetThumbnailByGtidAsync(
            owner.Identity, drive.Alias, gtid, payload.Key, thumb.PixelWidth, thumb.PixelHeight);
        Assert.That(thumbResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), "peer thumbnail-by-gtid should be 200");
        var thumbBytes = await thumbResponse.Content!.ReadAsByteArrayAsync();
        Assert.That(thumbBytes.ToBase64(), Is.EqualTo(thumb.Content.ToBase64()), "thumbnail content mismatch over peer");
    }

    [Test]
    public async Task Member_ReadsRangedPayloadByGtidOverPeer()
    {
        var (member, owner, drive) = await SetupMemberCanReadOwnerDriveAsync();

        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var (_, gtid) = await OwnerUploadsFileAsync(owner, drive, payload);

        const int start = 1;
        const int length = 4;
        var response = await member.Drives.Peer.GetPayloadByGtidAsync(
            owner.Identity, drive.Alias, gtid, payload.Key, new FileChunk { Start = start, Length = length });
        Assert.That(response.IsSuccessStatusCode, Is.True, $"peer ranged payload-by-gtid failed: {response.StatusCode}");

        var bytes = await response.Content!.ReadAsByteArrayAsync();
        var expected = payload.Content.Skip(start).Take(length).ToArray();
        Assert.That(bytes.Length, Is.EqualTo(expected.Length), "ranged read returned the wrong number of bytes");
        Assert.That(ByteArrayUtil.EquiByteArrayCompare(bytes, expected), Is.True, "ranged payload slice mismatch over peer");
    }

    [Test]
    public async Task Member_GetHeaderByGtidOverPeer_MissingFile_ReturnsNotFound()
    {
        var (member, owner, drive) = await SetupMemberCanReadOwnerDriveAsync();

        var header = await member.Drives.Peer.GetFileHeaderByGtidAsync(owner.Identity, drive.Alias, Guid.NewGuid());
        Assert.That(header.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task NotConnectedMember_GetHeaderByGtidOverPeer_IsRejected()
    {
        // Both identities exist and each has the drive, but no connection / grant exists.
        var member = await LoginAsOwner(Identities.Frodo);
        var owner = await LoginAsOwner(Identities.Sam);

        var drive = TargetDrive.NewTargetDrive();
        await member.Admin.CreateDrive(drive, "member copy", allowAnonymousReads: false);
        await owner.Admin.CreateDrive(drive, "owner copy", allowAnonymousReads: false);

        var header = await member.Drives.Peer.GetFileHeaderByGtidAsync(owner.Identity, drive.Alias, Guid.NewGuid());
        Assert.That(header.IsSuccessStatusCode, Is.False,
            $"unconnected member should not be able to read the owner's drive; got {header.StatusCode}");
    }

    // -----------------------------------------------------------------------------------------

    private async Task<(OwnerSession member, OwnerSession owner, TargetDrive drive)> SetupMemberCanReadOwnerDriveAsync()
    {
        var member = await LoginAsOwner(Identities.Frodo);
        var owner = await LoginAsOwner(Identities.Sam);
        // sender = member: the member is granted Read on the owner's (recipient's) drive.
        var drive = await PeerFlow.CreatePeerDriveAsync(member, owner, DrivePermission.Read, "community",
            allowAnonymousReads: false);
        return (member, owner, drive);
    }

    // Owner uploads a file locally and we resolve its GlobalTransitId from the stored header
    // (guaranteed populated server-side, independent of distribution).
    private static async Task<(Guid fileId, Guid gtid)> OwnerUploadsFileAsync(
        OwnerSession owner, TargetDrive drive, TestPayloadDefinition payload)
    {
        var metadata = SampleMetadataData.Create(fileType: CommunityMessageFileType, acl: AccessControlList.Connected);
        var manifest = new UploadManifest
        {
            PayloadDescriptors = new List<TestPayloadDefinition> { payload }.ToPayloadDescriptorList().ToList()
        };
        var uploaded = await owner.Drives.Writer.CreateNewUnencryptedFile(
            drive.Alias, metadata, manifest, new List<TestPayloadDefinition> { payload });
        Assert.That(uploaded.IsSuccessStatusCode, Is.True, $"owner upload failed: {uploaded.StatusCode}");
        var fileId = uploaded.Content!.FileId;

        var header = await owner.Drives.Reader.GetFileHeaderAsync(drive.Alias, fileId);
        Assert.That(header.IsSuccessStatusCode, Is.True, $"owner header read failed: {header.StatusCode}");
        var gtid = header.Content!.FileMetadata.GlobalTransitId
                   ?? throw new InvalidOperationException("stored file is expected to have a GlobalTransitId");
        return (fileId, gtid);
    }
}
