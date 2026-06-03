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
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Cap 1 + Cap 2 of the chat-kmp V2 peer transport: a member queries and reads files from a drive
/// hosted by another identity over peer. Models the community read path
/// (<c>queryBatchOverPeer</c> + <c>getContentFromHeaderOverPeer</c>). Based on the battle-tested V1
/// <c>_Universal/Peer</c> query flows.
///
/// Layout: <c>owner</c> (Sam) hosts the drive and uploads files locally; <c>member</c> (Frodo) is
/// connected and granted Read on the owner's drive, then reads over peer.
/// </summary>
[TestFixture]
public class PeerQueryAndContentTests : V2Fixture
{
    private const int CommunityMessageFileType = 7020;

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task Member_CanQueryBatchOwnerDriveOverPeer()
    {
        var (member, owner, drive) = await SetupMemberCanReadOwnerDriveAsync();

        // Owner posts two messages locally on the community drive.
        var f1 = await OwnerUploadsMessage(owner, drive, "hello");
        var f2 = await OwnerUploadsMessage(owner, drive, "world");

        var response = await member.Drives.Peer.QueryBatchAsync(owner.Identity, drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { FileType = new[] { CommunityMessageFileType } },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 100, IncludeMetadataHeader = true }
        });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"peer query-batch failed: {response.StatusCode}");
        var fileIds = response.Content!.SearchResults.Select(r => r.FileId).ToList();
        Assert.That(fileIds, Has.Member(f1.FileId));
        Assert.That(fileIds, Has.Member(f2.FileId));
    }

    [Test]
    public async Task Member_QueryBatchOverPeer_EmptyDrive_ReturnsNoResults()
    {
        var (member, owner, drive) = await SetupMemberCanReadOwnerDriveAsync();

        var response = await member.Drives.Peer.QueryBatchAsync(owner.Identity, drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { FileType = new[] { CommunityMessageFileType } },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 100, IncludeMetadataHeader = true }
        });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"peer query-batch failed: {response.StatusCode}");
        Assert.That(response.Content!.SearchResults.Count(), Is.EqualTo(0));
    }

    [Test]
    public async Task Member_CanGetHeaderAndPayloadAndThumbnailOverPeer()
    {
        var (member, owner, drive) = await SetupMemberCanReadOwnerDriveAsync();

        var metadata = SampleMetadataData.Create(fileType: CommunityMessageFileType, acl: AccessControlList.Connected);
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var manifest = new UploadManifest
        {
            PayloadDescriptors = new List<TestPayloadDefinition> { payload }.ToPayloadDescriptorList().ToList()
        };
        var uploaded = await owner.Drives.Writer.CreateNewUnencryptedFile(
            drive.Alias, metadata, manifest, new List<TestPayloadDefinition> { payload });
        Assert.That(uploaded.IsSuccessStatusCode, Is.True, $"owner upload failed: {uploaded.StatusCode}");
        var fileId = uploaded.Content!.FileId;

        // header
        var header = await member.Drives.Peer.GetFileHeaderAsync(owner.Identity, drive.Alias, fileId);
        Assert.That(header.IsSuccessStatusCode, Is.True, $"peer header failed: {header.StatusCode}");
        Assert.That(header.Content!.FileMetadata.Payloads.Count(), Is.EqualTo(1));

        // payload (unencrypted → bytes come back verbatim)
        var payloadResponse = await member.Drives.Peer.GetPayloadAsync(owner.Identity, drive.Alias, fileId, payload.Key);
        Assert.That(payloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), "peer payload should be 200");
        var payloadBytes = await payloadResponse.Content!.ReadAsByteArrayAsync();
        Assert.That(payloadBytes.ToBase64(), Is.EqualTo(payload.Content.ToBase64()), "payload content mismatch over peer");

        // thumbnail
        var thumb = payload.Thumbnails.Single();
        var thumbResponse = await member.Drives.Peer.GetThumbnailAsync(
            owner.Identity, drive.Alias, fileId, payload.Key, thumb.PixelWidth, thumb.PixelHeight);
        Assert.That(thumbResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), "peer thumbnail should be 200");
        var thumbBytes = await thumbResponse.Content!.ReadAsByteArrayAsync();
        Assert.That(thumbBytes.ToBase64(), Is.EqualTo(thumb.Content.ToBase64()), "thumbnail content mismatch over peer");
    }

    [Test]
    public async Task Member_ReadsRangedPayloadOverPeer()
    {
        var (member, owner, drive) = await SetupMemberCanReadOwnerDriveAsync();

        var metadata = SampleMetadataData.Create(fileType: CommunityMessageFileType, acl: AccessControlList.Connected);
        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var manifest = new UploadManifest
        {
            PayloadDescriptors = new List<TestPayloadDefinition> { payload }.ToPayloadDescriptorList().ToList()
        };
        var uploaded = await owner.Drives.Writer.CreateNewUnencryptedFile(
            drive.Alias, metadata, manifest, new List<TestPayloadDefinition> { payload });
        Assert.That(uploaded.IsSuccessStatusCode, Is.True, $"owner upload failed: {uploaded.StatusCode}");
        var fileId = uploaded.Content!.FileId;

        const int start = 1;
        const int length = 4;
        var response = await member.Drives.Peer.GetPayloadAsync(
            owner.Identity, drive.Alias, fileId, payload.Key, new FileChunk { Start = start, Length = length });
        Assert.That(response.IsSuccessStatusCode, Is.True, $"peer ranged payload failed: {response.StatusCode}");

        var bytes = await response.Content!.ReadAsByteArrayAsync();
        var expected = payload.Content.Skip(start).Take(length).ToArray();
        Assert.That(bytes.Length, Is.EqualTo(expected.Length), "ranged read returned the wrong number of bytes");
        Assert.That(ByteArrayUtil.EquiByteArrayCompare(bytes, expected), Is.True, "ranged payload slice mismatch over peer");
    }

    [Test]
    public async Task Member_GetHeaderOverPeer_MissingFile_ReturnsNotFound()
    {
        var (member, owner, drive) = await SetupMemberCanReadOwnerDriveAsync();

        var header = await member.Drives.Peer.GetFileHeaderAsync(owner.Identity, drive.Alias, Guid.NewGuid());
        Assert.That(header.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task NotConnectedMember_QueryBatchOverPeer_IsRejected()
    {
        // Both identities exist and each has the drive, but no connection / grant exists.
        var member = await LoginAsOwner(Identities.Frodo);
        var owner = await LoginAsOwner(Identities.Sam);

        var drive = TargetDrive.NewTargetDrive();
        await member.Admin.CreateDrive(drive, "member copy", allowAnonymousReads: false);
        await owner.Admin.CreateDrive(drive, "owner copy", allowAnonymousReads: false);

        var response = await member.Drives.Peer.QueryBatchAsync(owner.Identity, drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { FileType = new[] { CommunityMessageFileType } },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true }
        });

        Assert.That(response.IsSuccessStatusCode, Is.False,
            $"unconnected member should not be able to query the owner's drive; got {response.StatusCode}");
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

    private static async Task<Odin.Hosting.UnifiedV2.Drive.Write.CreateFileResult> OwnerUploadsMessage(
        OwnerSession owner, TargetDrive drive, string content)
    {
        var metadata = SampleMetadataData.Create(fileType: CommunityMessageFileType, acl: AccessControlList.Connected);
        metadata.AppData.Content = content;
        var response = await owner.Drives.Writer.UploadNewMetadata(drive.Alias, metadata);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"owner upload failed: {response.StatusCode}");
        return response.Content!;
    }
}
