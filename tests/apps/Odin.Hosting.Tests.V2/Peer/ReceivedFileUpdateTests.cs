#nullable enable
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Ported.DataSubscription;
using Odin.Hosting.Tests.V2.Ported.Peer;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Peer;

/// <summary>
/// A recipient changing its own copy of a file it received over transit, and what a peer's change of it moves.
/// </summary>
[TestFixture]
public class ReceivedFileUpdateTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    // #2101: the metadata-only update cleared TransitCreated, so a chat dated the message 1970
    [Test]
    public async Task OwnersMetadataUpdate_KeepsTransitCreated()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        var drive = await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Write, "received file update");

        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        metadata.AllowDistribution = true;
        var send = await frodo.Drives.Writer.UploadNewMetadata(drive.Alias, metadata,
            transitOptions: new TransitOptions { Recipients = [sam.Identity] });
        Assert.That(send.IsSuccessStatusCode, Is.True, $"upload failed: {send.StatusCode}");
        var globalTransitId = send.Content!.GlobalTransitId!.Value;

        await PeerFlow.DistributeAsync(frodo, sam, drive);

        var received = await FindAsync(sam, drive, globalTransitId);
        var transitCreated = received.FileMetadata.TransitCreated.milliseconds;
        Assert.That(transitCreated, Is.GreaterThan(0));

        var updated = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        updated.AppData.Content = "opened";
        updated.VersionTag = received.FileMetadata.VersionTag;
        var update = await sam.Drives.Writer.UpdateFileByFileId(drive.Alias, received.FileId, new FileUpdateInstructionSetV2
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            Manifest = new UploadManifest { PayloadDescriptors = [] }
        }, updated, []);
        Assert.That(update.IsSuccessStatusCode, Is.True, $"update failed: {update.StatusCode}");

        var after = await FindAsync(sam, drive, globalTransitId);
        Assert.That(after.FileMetadata.AppData.Content, Is.EqualTo("opened"));
        Assert.That(after.FileMetadata.TransitCreated.milliseconds, Is.EqualTo(transitCreated));
    }

    // #2065: TransitUpdated is when the file was last changed over transit by a peer.  A sender's update moves it, and
    // the owner's own update and overwrite keep it
    [Test]
    public async Task SendersUpdate_MovesTransitUpdated_AndOwnersWritesKeepIt()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var drive = TargetDrive.NewTargetDrive();
        await CollabChannelFlow.SetupAsync(frodo, drive, [sam], driveName: "Frodo's drive");

        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected, allowDistribution: true);
        metadata.AppData.Content = "first";
        var globalTransitId = await V2PeerUpdateByGtidTests.TransferAsync(sam, frodo, drive, metadata, []);
        var received = await FindAsync(frodo, drive, globalTransitId);
        Assert.That(received.FileMetadata.TransitUpdated.milliseconds, Is.EqualTo(0), "a first delivery is no update");

        var beforeTheUpdate = UnixTimeUtc.Now().milliseconds;
        var change = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected, allowDistribution: true);
        change.AppData.Content = "changed by sam";
        var sent = await V2PeerUpdateByGtidTests.UpdateAsync(sam, frodo.Identity, drive, globalTransitId, change,
            V2PeerUpdateByGtidTests.Instructions(drive, new UploadManifest { PayloadDescriptors = [] }));
        Assert.That(sent.IsSuccessStatusCode, Is.True, sent.Error?.Content);
        await sam.Sync.DrainOutboxAsync();
        await frodo.Sync.ProcessInboxAsync(drive, int.MaxValue);

        var changed = await FindAsync(frodo, drive, globalTransitId);
        Assert.That(changed.FileMetadata.AppData.Content, Is.EqualTo("changed by sam"));
        var transitUpdated = changed.FileMetadata.TransitUpdated.milliseconds;
        Assert.That(transitUpdated, Is.GreaterThanOrEqualTo(beforeTheUpdate), "the sender's update moves TransitUpdated");

        var own = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        own.AppData.Content = "changed by frodo";
        own.VersionTag = changed.FileMetadata.VersionTag;
        var update = await frodo.Drives.Writer.UpdateFileByFileId(drive.Alias, changed.FileId, new FileUpdateInstructionSetV2
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            Manifest = new UploadManifest { PayloadDescriptors = [] }
        }, own, []);
        Assert.That(update.IsSuccessStatusCode, Is.True, update.Error?.Content);

        var after = await FindAsync(frodo, drive, globalTransitId);
        Assert.That(after.FileMetadata.AppData.Content, Is.EqualTo("changed by frodo"));
        Assert.That(after.FileMetadata.TransitUpdated.milliseconds, Is.EqualTo(transitUpdated), "the owner's update keeps it");

        // The owner's overwrite of the whole file keeps it too
        var overwrite = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        overwrite.AppData.Content = "overwritten by frodo";
        overwrite.VersionTag = after.FileMetadata.VersionTag;
        var overwritten = await frodo.V1.Drive.UploadNewMetadata(drive, overwrite, new TransitOptions(), overwriteFileId: after.FileId);
        Assert.That(overwritten.IsSuccessStatusCode, Is.True, overwritten.Error?.Content);

        var afterOverwrite = await FindAsync(frodo, drive, globalTransitId);
        Assert.That(afterOverwrite.FileMetadata.AppData.Content, Is.EqualTo("overwritten by frodo"));
        Assert.That(afterOverwrite.FileMetadata.TransitUpdated.milliseconds, Is.EqualTo(transitUpdated),
            "the owner's overwrite keeps it");
    }

    // #2065 on the feed drive: a followed identity's new post arrives with no TransitUpdated, its edit moves it, and the
    // follower's own catch-up sync, which rewrites every item, keeps it
    [Test]
    public async Task FollowedIdentitysEdit_MovesTransitUpdated_AndTheFollowersSyncKeepsIt()
    {
        const int fileType = 20651;
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        // Anonymous reads, so the follower's sync can query the channel without a connection
        var channel = await DataSubscriptionScenario.CreateChannelDriveAsync(frodo, allowAnonymousReads: true);
        await DataSubscriptionScenario.FollowAsync(sam, frodo);
        var query = DataSubscriptionScenario.FeedQueryByFileType(fileType);

        var upload = await DataSubscriptionScenario.UploadStandardUnencryptedFileToChannelAsync(frodo, channel, "first", fileType);
        await frodo.Sync.DrainOutboxAsync();
        await sam.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var delivered = await DataSubscriptionScenario.AssertFeedDriveHasFileAsync(sam, query, "first", upload);
        Assert.That(delivered.FileMetadata.TransitUpdated.milliseconds, Is.EqualTo(0), "a new feed item is no change");

        var beforeTheEdit = UnixTimeUtc.Now().milliseconds;
        await DataSubscriptionScenario.OverwriteStandardFileAsync(frodo, upload.File, "edited", fileType, upload.NewVersionTag);
        await frodo.Sync.DrainOutboxAsync();
        await sam.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var edited = await DataSubscriptionScenario.AssertFeedDriveHasFileAsync(sam, query, "edited", upload);
        var transitUpdated = edited.FileMetadata.TransitUpdated.milliseconds;
        Assert.That(transitUpdated, Is.GreaterThanOrEqualTo(beforeTheEdit), "the followed identity's edit moves it");

        var sync = await sam.Followers.SynchronizeFeedHistoryAsync(frodo.Identity);
        Assert.That(sync.IsSuccessStatusCode, Is.True, $"sync-feed-history returned {sync.StatusCode}");

        var synced = await DataSubscriptionScenario.AssertFeedDriveHasFileAsync(sam, query, "edited", upload);
        Assert.That(synced.FileMetadata.Updated.milliseconds, Is.GreaterThan(edited.FileMetadata.Updated.milliseconds),
            "arrange: the sync should have rewritten the item");
        Assert.That(synced.FileMetadata.TransitUpdated.milliseconds, Is.EqualTo(transitUpdated), "the sync keeps it");
    }

    private static async Task<SharedSecretEncryptedFileHeader> FindAsync(OwnerSession owner, TargetDrive drive,
        System.Guid globalTransitId)
    {
        var query = await owner.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { GlobalTransitId = [globalTransitId] },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true },
        });
        Assert.That(query.IsSuccessStatusCode, Is.True, $"query failed: {query.StatusCode}");
        return query.Content!.SearchResults.Single();
    }
}
