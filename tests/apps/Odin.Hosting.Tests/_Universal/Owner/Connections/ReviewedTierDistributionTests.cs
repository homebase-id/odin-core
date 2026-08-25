using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Configuration;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;

namespace Odin.Hosting.Tests._Universal.Owner.Connections;

/// <summary>
/// The two Cat 2 changes the tier tests do not reach: the identity-keyed evaluator that drives feed
/// distribution, and the connections-list permission keys moving onto the reviewed tier.
/// </summary>
/// <remarks>
/// Both are silent failures if they regress -- a post reaches someone it should not, or a permission
/// key is handed to someone who has not been reviewed -- so neither shows up as an error anywhere.
/// </remarks>
public class ReviewedTierDistributionTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>
        {
            TestIdentities.Frodo,
            TestIdentities.Samwise
        });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [Test]
    public async Task FeedDistributionSkipsAnUnreviewedFollower()
    {
        // IdentityHasPermissionAsync is keyed on the identity, not a caller context, so the assignment
        // change does not reach it -- it had to start testing IsReviewed() rather than IsConnected().
        // Left alone, an unreviewed connection would keep receiving posts ACL'd to the reviewed tier.
        const int fileType = 7734;

        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        await sam.Connections.SendConnectionRequest(frodo.OdinId, []);
        await frodo.Connections.AcceptConnectionRequest(sam.OdinId, []);

        var follow = await frodo.Follower.FollowIdentity(
            TestIdentities.Samwise.OdinId, FollowerNotificationType.AllNotifications, []);
        ClassicAssert.IsTrue(follow.IsSuccessStatusCode);

        // Sam no longer considers Frodo reviewed.
        var unreview = await sam.Network.UnreviewConnection(frodo.OdinId);
        ClassicAssert.IsTrue(unreview.IsSuccessStatusCode, $"un-review failed: {unreview.Error?.Content}");

        await Post(sam, fileType, "reviewed followers only");

        // Deliberately not WaitForEmptyOutbox: with no eligible recipient there is nothing to drain,
        // and waiting on it tests the outbox rather than the distribution decision.
        await _scaffold.CreateOwnerApiClient(frodo.Identity).Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        var feed = await QueryFeed(frodo, fileType);
        ClassicAssert.IsFalse(feed.Any(),
            "an unreviewed follower must not receive a post ACL'd to the reviewed tier");

        await Cleanup();
    }

    [Test]
    public async Task FeedDistributionReachesAReviewedFollower()
    {
        const int fileType = 7735;

        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        await sam.Connections.SendConnectionRequest(frodo.OdinId, []);
        await frodo.Connections.AcceptConnectionRequest(sam.OdinId, []);

        await frodo.Follower.FollowIdentity(
            TestIdentities.Samwise.OdinId, FollowerNotificationType.AllNotifications, []);

        await Post(sam, fileType, "reviewed followers only");

        await sam.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.PublicPostsChannelDrive);
        await _scaffold.CreateOwnerApiClient(frodo.Identity).Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        var feed = await QueryFeed(frodo, fileType);
        ClassicAssert.AreEqual(1, feed.Count(),
            "a reviewed follower should still receive it -- the tightening must not break distribution");

        await Cleanup();
    }

    [Test]
    public async Task TheConnectionsListKeysFollowTheReviewedTier()
    {
        // "Who can see my connections list" was implemented as permission keys on the Confirmed circle.
        // They are gated on the review now, so an unreviewed connection must not hold ReadConnections
        // even while the tenant setting is on.
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        // The scaffold stores a bare TenantSettings (every flag false) rather than TenantSettings.Default,
        // so the premise -- "the setting allows it" -- has to be established rather than assumed.
        var setFlag = await sam.Configuration.UpdateTenantSettingsFlag(
            TenantConfigFlagNames.ConnectedIdentitiesCanViewConnections, bool.TrueString);
        ClassicAssert.IsTrue(setFlag.IsSuccessStatusCode, $"could not enable the setting: {setFlag.Error?.Content}");

        await sam.Connections.SendConnectionRequest(frodo.OdinId, []);
        await frodo.Connections.AcceptConnectionRequest(sam.OdinId, []);

        var asReviewed = await frodo.PeerQuery.GetRemoteDotYouContext(
            new Controllers.TransitGetSecurityContextRequest { OdinId = sam.OdinId });

        ClassicAssert.IsTrue(asReviewed.IsSuccessStatusCode);
        ClassicAssert.IsTrue(HasKey(asReviewed.Content, PermissionKeys.ReadConnections),
            "a reviewed connection should hold ReadConnections while the tenant setting allows it");

        await sam.Network.UnreviewConnection(frodo.OdinId);

        var asUnreviewed = await frodo.PeerQuery.GetRemoteDotYouContext(
            new Controllers.TransitGetSecurityContextRequest { OdinId = sam.OdinId });

        ClassicAssert.IsTrue(asUnreviewed.IsSuccessStatusCode);
        ClassicAssert.IsFalse(HasKey(asUnreviewed.Content, PermissionKeys.ReadConnections),
            "an unreviewed connection must not hold ReadConnections, setting or no setting");

        await Cleanup();
    }

    private static bool HasKey(Services.Base.RedactedOdinContext context, int key)
    {
        return context.PermissionContext.PermissionGroups
            .Any(g => g.PermissionSet?.Keys?.Contains(key) ?? false);
    }

    private static async Task Post(OwnerApiClientRedux sam, int fileType, string content)
    {
        var file = SampleMetadataData.CreateWithContent(fileType, content, new AccessControlList
        {
            RequiredSecurityGroup = SecurityGroupType.Reviewed
        });

        file.AllowDistribution = true;

        var response = await sam.DriveRedux.UploadNewMetadata(
            SystemDriveConstants.PublicPostsChannelDrive, file);

        ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"post failed: {response.Error?.Content}");
    }

    private static async Task<IEnumerable<SharedSecretEncryptedFileHeader>> QueryFeed(
        OwnerApiClientRedux frodo, int fileType)
    {
        var response = await frodo.DriveRedux.QueryBatch(new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = SystemDriveConstants.FeedDrive,
                FileType = [fileType]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        ClassicAssert.IsTrue(response.IsSuccessStatusCode);
        return response.Content.SearchResults;
    }

    private async Task Cleanup()
    {
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        await frodo.Follower.UnfollowIdentity(sam.OdinId);
        await sam.Connections.DisconnectFrom(frodo.Identity.OdinId);
        await frodo.Connections.DisconnectFrom(sam.Identity.OdinId);
    }
}
