using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests._Universal.Feed;

/// <summary>
/// An unencrypted channel post is queued only for followers its ACL admits, as an encrypted post is (#1995).  It used
/// to be queued for every follower and refused in the outbox worker, which retried the refusal, so the outbox never
/// drained.
/// </summary>
public class UnencryptedFeedAclTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: [TestIdentities.Frodo, TestIdentities.Samwise, TestIdentities.Merry]);
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

    [TearDown]
    public void TearDown()
    {
        _scaffold.AssertLogEvents();
    }

    [Test]
    public async Task UnencryptedChannelPost_IsDistributedOnlyToFollowersTheAclAdmits()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merry = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        var channel = new TargetDrive
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        var createDrive = await frodo.DriveManager.CreateDrive(channel, "Unencrypted feed ACL channel", "", allowAnonymousReads: false,
            allowSubscriptions: true);
        Assert.That(createDrive.IsSuccessStatusCode, Is.True);

        // Sam is in Readers, Merry only in Others; both connected (and reviewed) and following Frodo
        var readersCircleId = Guid.NewGuid();
        await frodo.Network.CreateCircle(readersCircleId, "Readers", ReadGrant(channel));
        var othersCircleId = Guid.NewGuid();
        await frodo.Network.CreateCircle(othersCircleId, "Others", ReadGrant(channel));

        await Connect(frodo, sam, readersCircleId);
        await Connect(frodo, merry, othersCircleId);
        await sam.Follower.FollowIdentity(frodo.OdinId, FollowerNotificationType.AllNotifications);
        await merry.Follower.FollowIdentity(frodo.OdinId, FollowerNotificationType.AllNotifications);

        try
        {
            var readersOnly = await UploadPost(frodo, channel, new AccessControlList
            {
                RequiredSecurityGroup = SecurityGroupType.Connected,
                CircleIdList = [readersCircleId]
            });

            var everyone = await UploadPost(frodo, channel, AccessControlList.Authenticated);

            // Times out if an item for a refused follower is left retrying in the outbox
            await frodo.DriveRedux.WaitForFeedOutboxDistribution(channel);

            Assert.That(await FeedHasPost(sam, readersOnly), Is.True, "Sam is in the Readers circle");
            Assert.That(await FeedHasPost(merry, readersOnly), Is.False, "Merry follows Frodo but is not in the Readers circle");

            Assert.That(await FeedHasPost(sam, everyone), Is.True, "an Authenticated post reaches every follower");
            Assert.That(await FeedHasPost(merry, everyone), Is.True, "an Authenticated post reaches every follower");
        }
        finally
        {
            await sam.Follower.UnfollowIdentity(frodo.OdinId);
            await merry.Follower.UnfollowIdentity(frodo.OdinId);
            foreach (var reader in new[] { sam, merry })
            {
                await frodo.Connections.DisconnectFrom(reader.OdinId);
                await reader.Connections.DisconnectFrom(frodo.OdinId);
            }
        }
    }

    private static async Task<GlobalTransitIdFileIdentifier> UploadPost(OwnerApiClientRedux owner, TargetDrive channel,
        AccessControlList acl)
    {
        var metadata = SampleMetadataData.Create(fileType: 100, acl: acl, allowDistribution: true);
        var upload = await owner.DriveRedux.UploadNewMetadata(channel, metadata);
        Assert.That(upload.IsSuccessStatusCode, Is.True);
        return upload.Content.GlobalTransitIdFileIdentifier;
    }

    private static async Task Connect(OwnerApiClientRedux owner, OwnerApiClientRedux reader, Guid circleId)
    {
        var send = await reader.Connections.SendConnectionRequest(owner.OdinId);
        Assert.That(send.IsSuccessStatusCode, Is.True);

        var accept = await owner.Connections.AcceptConnectionRequest(reader.OdinId, [circleId]);
        Assert.That(accept.IsSuccessStatusCode, Is.True);

        var review = await owner.Network.MarkReviewed(reader.OdinId);
        Assert.That(review.IsSuccessStatusCode, Is.True);
    }

    private static PermissionSetGrantRequest ReadGrant(TargetDrive drive)
    {
        return new PermissionSetGrantRequest
        {
            Drives =
            [
                new DriveGrantRequest
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = drive,
                        Permission = DrivePermission.Read
                    }
                }
            ]
        };
    }

    private static async Task<bool> FeedHasPost(OwnerApiClientRedux follower, GlobalTransitIdFileIdentifier post)
    {
        await follower.DriveRedux.ProcessInbox(WellKnownAppDrives.FeedDrive);
        var response = await follower.DriveRedux.QueryByGlobalTransitId(new GlobalTransitIdFileIdentifier
        {
            GlobalTransitId = post.GlobalTransitId,
            TargetDrive = WellKnownAppDrives.FeedDrive
        });

        Assert.That(response.IsSuccessStatusCode, Is.True);
        return response.Content.SearchResults.Any();
    }
}
