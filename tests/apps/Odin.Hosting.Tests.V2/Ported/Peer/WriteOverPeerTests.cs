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
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Cap 3 of the chat-kmp V2 peer transport: a member writes a file directly to a drive hosted by
/// another identity, with NO local copy (the community <c>uploadFileOverPeer</c> path). Based on the
/// battle-tested V1 <c>_Universal/Peer/DirectSend</c> tests.
///
/// Layout: <c>member</c> (Frodo) is granted Write on the <c>owner</c> (Sam) drive and sends a message
/// straight to it over peer. The file appears on the owner and NOT on the member.
/// </summary>
[TestFixture]
public class WriteOverPeerTests : V2Fixture
{
    private const int CommunityMessageFileType = 7020;

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task Member_WritesFileToOwnerDriveOverPeer_NoLocalCopy()
    {
        var member = await LoginAsOwner(Identities.Frodo);
        var owner = await LoginAsOwner(Identities.Sam);

        // member (sender) is granted Write on the owner's (recipient's) drive.
        var drive = await PeerFlow.CreatePeerDriveAsync(member, owner, DrivePermission.Write, "community",
            allowAnonymousReads: false);

        var metadata = SampleMetadataData.Create(fileType: CommunityMessageFileType, acl: AccessControlList.Connected,
            allowDistribution: true);
        metadata.AppData.Content = "hello from the member over peer";

        var send = await member.Drives.PeerWriter.SendUnencryptedFileOverPeer(owner.Identity, drive, metadata);
        Assert.That(send.IsSuccessStatusCode, Is.True, $"write-over-peer failed: {send.StatusCode}");

        var gtid = send.Content!.RemoteGlobalTransitIdFileIdentifier?.GlobalTransitId
                   ?? throw new InvalidOperationException("write-over-peer must yield a remote GlobalTransitId");

        await member.Sync.DrainOutboxAsync();
        await owner.Sync.ProcessInboxAsync(drive);

        // The owner now hosts the file.
        var ownerCopy = await QueryByGtid(owner, drive, gtid);
        Assert.That(ownerCopy, Is.Not.Null, "owner should have received the file written over peer");
        Assert.That(ownerCopy!.FileMetadata.AppData.Content, Is.EqualTo("hello from the member over peer"));

        // The member kept no local copy (the write was staged transiently and sent).
        var memberCopy = await QueryByGtid(member, drive, gtid);
        Assert.That(memberCopy, Is.Null, "member must not retain a local copy of a write-over-peer file");
    }

    [Test]
    public async Task Member_WritesFileWithPayloadToOwnerOverPeer()
    {
        var member = await LoginAsOwner(Identities.Frodo);
        var owner = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(member, owner, DrivePermission.Write, "community",
            allowAnonymousReads: false);

        var metadata = SampleMetadataData.Create(fileType: CommunityMessageFileType, acl: AccessControlList.Connected,
            allowDistribution: true);
        metadata.AppData.Content = "message with attachment";
        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var payloads = new List<TestPayloadDefinition> { payload };
        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };

        var send = await member.Drives.PeerWriter.SendUnencryptedFileOverPeer(owner.Identity, drive, metadata, payloads, manifest);
        Assert.That(send.IsSuccessStatusCode, Is.True, $"write-with-payload over peer failed: {send.StatusCode}");
        var gtid = send.Content!.RemoteGlobalTransitIdFileIdentifier!.GlobalTransitId;

        await member.Sync.DrainOutboxAsync();
        await owner.Sync.ProcessInboxAsync(drive);

        var ownerCopy = await QueryByGtid(owner, drive, gtid);
        Assert.That(ownerCopy, Is.Not.Null, "owner should have received the file");
        Assert.That(ownerCopy!.FileMetadata.Payloads.Count(), Is.EqualTo(1), "the payload should be present on the owner's copy");

        var payloadResponse = await owner.Drives.Reader.GetPayloadAsync(drive.Alias, ownerCopy.FileId, payload.Key);
        Assert.That(payloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var bytes = await payloadResponse.Content!.ReadAsByteArrayAsync();
        Assert.That(ByteArrayUtil.EquiByteArrayCompare(bytes, payload.Content), Is.True, "payload content mismatch on owner");
    }

    [Test]
    public async Task Member_UnauthorizedWrite_IsRejected()
    {
        var member = await LoginAsOwner(Identities.Frodo);
        var owner = await LoginAsOwner(Identities.Sam);
        // Member is granted only READ on the owner's drive — not Write.
        var drive = await PeerFlow.CreatePeerDriveAsync(member, owner, DrivePermission.Read, "community",
            allowAnonymousReads: false);

        var metadata = SampleMetadataData.Create(fileType: CommunityMessageFileType, acl: AccessControlList.Connected,
            allowDistribution: true);
        metadata.AppData.Content = "i should not be allowed to write this";

        var send = await member.Drives.PeerWriter.SendUnencryptedFileOverPeer(owner.Identity, drive, metadata);
        var gtid = send.Content?.RemoteGlobalTransitIdFileIdentifier?.GlobalTransitId ?? Guid.Empty;

        await member.Sync.DrainOutboxAsync();
        await owner.Sync.ProcessInboxAsync(drive);

        // Regardless of where the rejection surfaces (send status or remote perimeter), the file must
        // not land on the owner's drive.
        if (gtid != Guid.Empty)
        {
            var ownerCopy = await QueryByGtid(owner, drive, gtid);
            Assert.That(ownerCopy, Is.Null, "a write from a Read-only member must not land on the owner's drive");
        }
    }

    private static async Task<Odin.Services.Apps.SharedSecretEncryptedFileHeader> QueryByGtid(
        OwnerSession session, TargetDrive drive, Guid gtid)
    {
        var q = await session.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { GlobalTransitId = new[] { gtid } },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true }
        });
        Assert.That(q.IsSuccessStatusCode, Is.True, $"local query failed: {q.StatusCode}");
        return q.Content!.SearchResults.SingleOrDefault();
    }
}
