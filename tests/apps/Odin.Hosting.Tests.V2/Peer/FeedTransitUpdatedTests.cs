using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Time;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Ported.DataSubscription;
using Odin.Services.Apps;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Peer;

/// <summary>
/// #2065 on the feed drive: a followed identity's new post arrives with no TransitUpdated, its edit moves it, and the
/// follower's own catch-up sync, which rewrites every item, keeps it
/// </summary>
[TestFixture]
public class FeedTransitUpdatedTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

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
}
