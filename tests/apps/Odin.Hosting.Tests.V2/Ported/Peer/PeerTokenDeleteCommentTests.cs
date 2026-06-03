using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// "Comprehensive" extras for the V2 peer transport: the V2 remote-notification token endpoint,
/// delete-over-peer, and proof that the peer endpoints honor the file-system-type header.
/// </summary>
[TestFixture]
public class PeerTokenDeleteCommentTests : V2Fixture
{
    private const int CommunityMessageFileType = 7020;

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task V2TokenEndpoint_ReturnsTokenForConnectedPeer()
    {
        var member = await LoginAsOwner(Identities.Frodo);
        var owner = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(member, owner, DrivePermission.Read, "community", allowAnonymousReads: false);

        var client = new PeerNotificationV2Client(member.Identity, member.Factory);
        var response = await client.GetTokenAsync(owner.Identity);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"V2 token endpoint failed: {response.StatusCode}");
        Assert.That(response.Content!.AuthenticationToken64, Is.Not.Null.And.Not.Empty);
        Assert.That(response.Content.SharedSecret, Is.Not.Null);
        Assert.That(response.Content.SharedSecret.Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task Member_DeletesFileOnOwnerOverPeer()
    {
        var member = await LoginAsOwner(Identities.Frodo);
        var owner = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(member, owner, DrivePermission.Write, "community",
            allowAnonymousReads: false);

        var metadata = SampleMetadataData.Create(fileType: CommunityMessageFileType, acl: AccessControlList.Connected,
            allowDistribution: true);
        metadata.AppData.Content = "to be deleted over peer";

        var send = await member.Drives.PeerWriter.SendUnencryptedFileOverPeer(owner.Identity, drive, metadata);
        Assert.That(send.IsSuccessStatusCode, Is.True, $"seed write-over-peer failed: {send.StatusCode}");
        var gtid = send.Content!.RemoteGlobalTransitIdFileIdentifier!.GlobalTransitId;

        await member.Sync.DrainOutboxAsync();
        await owner.Sync.ProcessInboxAsync(drive);

        Assert.That(await QueryByGtid(owner, drive, gtid), Is.Not.Null, "file should exist on owner before delete");

        var del = await member.Drives.PeerWriter.SendDeleteRequestOverPeer(owner.Identity, drive, gtid);
        Assert.That(del.IsSuccessStatusCode, Is.True, $"delete-over-peer failed: {del.StatusCode}");

        await member.Sync.DrainOutboxAsync();
        await owner.Sync.ProcessInboxAsync(drive);

        var after = await QueryByGtid(owner, drive, gtid);
        Assert.That(after == null || after.FileState == FileState.Deleted, Is.True,
            "owner's copy should be deleted after a delete-over-peer request");
    }

    [Test]
    public async Task PeerQuery_HonorsFileSystemTypeHeader()
    {
        var member = await LoginAsOwner(Identities.Frodo);
        var owner = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(member, owner, DrivePermission.Read, "community",
            allowAnonymousReads: false);

        // Upload a STANDARD file on the owner's drive.
        var metadata = SampleMetadataData.Create(fileType: CommunityMessageFileType, acl: AccessControlList.Connected);
        var upload = await owner.Drives.Writer.UploadNewMetadata(drive.Alias, metadata);
        Assert.That(upload.IsSuccessStatusCode, Is.True, $"owner upload failed: {upload.StatusCode}");
        var fileId = upload.Content!.FileId;

        QueryBatchRequest Req() => new()
        {
            QueryParams = new FileQueryParamsV1 { FileType = new[] { CommunityMessageFileType } },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true }
        };

        // Standard FST → the file is found.
        var standard = await member.Drives.Peer.QueryBatchAsync(owner.Identity, drive.Alias, Req(), FileSystemType.Standard);
        Assert.That(standard.IsSuccessStatusCode, Is.True, $"standard peer query failed: {standard.StatusCode}");
        Assert.That(standard.Content!.SearchResults.Any(r => r.FileId == fileId), Is.True);

        // Comment FST → the Standard file is NOT in the comment store (proves the FST header routes
        // over peer). Note: standalone comment files require a parent reference, so this asserts store
        // isolation rather than creating a comment.
        var comment = await member.Drives.Peer.QueryBatchAsync(owner.Identity, drive.Alias, Req(), FileSystemType.Comment);
        Assert.That(comment.IsSuccessStatusCode, Is.True, $"comment peer query failed: {comment.StatusCode}");
        Assert.That(comment.Content!.SearchResults.Any(r => r.FileId == fileId), Is.False,
            "a Standard file must not appear in a Comment-filesystem peer query");
    }

    private static async Task<SharedSecretEncryptedFileHeader> QueryByGtid(OwnerSession session, TargetDrive drive, Guid gtid)
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
