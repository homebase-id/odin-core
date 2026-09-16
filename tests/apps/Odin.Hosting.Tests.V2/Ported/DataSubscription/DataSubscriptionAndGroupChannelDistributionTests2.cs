using System;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Ported.DataSubscription;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/OwnerApi/DataSubscription/DataSubscriptionAndGroupChannelDistributionTests2.cs
///
/// An encrypted post whose ACL names a single circle reaches only the followers in that circle, and is
/// removed from the member's feed when the owner deletes it.
/// </summary>
/// <remarks>
/// Checked port. <b>Entirely <c>[Ignore]("return to these after prototyping phase")</c>, carried
/// as-is</b> — this moved without ever having run.
/// <list type="bullet">
/// <item>Despite the name, nothing here is a group channel: both tests are copies of their namesakes in
/// <see cref="DataSubscriptionAndDistributionTests2"/>, on an ordinary channel drive with no group
/// attributes. Carried rather than deleted — what the fixture should become belongs to whoever
/// un-ignores it.</item>
/// <item>Same substitutions as its live twin: <c>CreateConnectedHobbits</c> →
/// <see cref="PeerFlow.ConnectAllAsync"/> (without the app registration nothing reads),
/// <c>Membership.CreateCircle(name, drive, permission)</c> → <c>Admin.CreateCircle</c> over
/// <c>TestUtils.CreatePermissionGrantRequest</c>, <c>WaitForEmptyOutbox</c> →
/// <c>Sync.DrainOutboxAsync()</c>, <c>ProcessInbox(FeedDrive)</c> →
/// <c>Sync.ProcessInboxAsync(FeedDrive)</c>, <c>TransitQuery.GetPayload</c> →
/// <see cref="DataSubscriptionScenario"/>'s payload assertions. Since none of it runs, none of that has
/// been exercised.</item>
/// <item>Dropped: the delete test's two <c>GetDrives(1, 100)</c> calls, whose results were assigned to
/// <c>x</c> and <c>x2</c> and never read — debugging leftovers that assert nothing.</item>
/// <item>Trailing unfollow / disconnect cleanup dropped — per-test reset covers it.</item>
/// </list>
/// </remarks>
[TestFixture]
public class DataSubscriptionAndGroupChannelDistributionTests2 : V2Fixture
{
    protected override string[] HostIdentities =>
        [Identities.Frodo, Identities.Sam, Identities.Pippin, Identities.Merry];

    [Test]
    [Ignore("return to these after prototyping phase")]
    public async Task EncryptedFile_UploadedByTheOwner_IsOnlyDistributedTo_ConnectedFollowers_WithAccessInFileAcl_Of_A_SingleCircle()
    {
        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);
        var merryOwnerClient = await LoginAsOwner(Identities.Merry);
        var pippinOwnerClient = await LoginAsOwner(Identities.Pippin);

        //
        // Frodo, Sam, Merry, and Pippin are connected
        //
        await PeerFlow.ConnectAllAsync([frodoOwnerClient, merryOwnerClient, pippinOwnerClient, samOwnerClient],
            TargetDrive.NewTargetDrive());

        //
        // Sam, Merry, and Pippin follow Frodo
        //
        await FollowAsync(samOwnerClient, frodoOwnerClient);
        await FollowAsync(merryOwnerClient, frodoOwnerClient);
        await FollowAsync(pippinOwnerClient, frodoOwnerClient);

        //create a channel drive
        var frodoSecureChannel = DataSubscriptionScenario.NewChannelDrive();

        await frodoOwnerClient.Admin.CreateDrive(frodoSecureChannel, "A Secured channel Drive", allowAnonymousReads: false,
            ownerOnly: false, allowSubscriptions: true);

        //
        // Frodo creates a circle named Mordor and puts Sam in it
        //
        var circleId = Guid.NewGuid();
        await frodoOwnerClient.Admin.CreateCircle(circleId, "Mordor",
            TestUtils.CreatePermissionGrantRequest(frodoSecureChannel, DrivePermission.Read));

        var grantResponse = await frodoOwnerClient.Connections.GrantCircle(circleId, samOwnerClient.Identity);
        Assert.That(grantResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        //
        // Frodo Uploads a video to his feed for the Mordor circle
        //
        const string headerContent = "I'm Mr. Underhill; I think";
        const string payloadContent = "this could be a photo of me";
        var (firstUploadResult, encryptedJsonContent64, encryptedPayloadContent64) =
            await DataSubscriptionScenario.UploadStandardEncryptedFileWithPayloadToChannelAsync(
                frodoOwnerClient, frodoSecureChannel, headerContent, payloadContent, circleId);

        // Process the outbox since we're sending an encrypted file
        await frodoOwnerClient.Sync.DrainOutboxAsync();

        //
        // The header is distributed to the feed drive of Sam
        // Sam can get the payload via transit query
        //
        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);
        await DataSubscriptionScenario.AssertFeedDriveHasFileAsync(samOwnerClient,
            DataSubscriptionScenario.FeedQueryByGlobalTransitId(firstUploadResult), encryptedJsonContent64, firstUploadResult);
        await DataSubscriptionScenario.AssertCanGetPayloadAsync(samOwnerClient, frodoOwnerClient, firstUploadResult,
            encryptedPayloadContent64);

        //
        // The header is NOT distributed to the feed drive of Merry and Pippin
        // Merry and Pippin Cannot get the payload via transit query
        //
        await pippinOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);
        await DataSubscriptionScenario.AssertFeedDriveDoesNotHaveHeaderAsync(pippinOwnerClient, firstUploadResult);
        await DataSubscriptionScenario.AssertCanNotGetPayloadAsync(pippinOwnerClient, frodoOwnerClient, firstUploadResult);

        await merryOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);
        await DataSubscriptionScenario.AssertFeedDriveDoesNotHaveHeaderAsync(merryOwnerClient, firstUploadResult);
        await DataSubscriptionScenario.AssertCanNotGetPayloadAsync(merryOwnerClient, frodoOwnerClient, firstUploadResult);
    }

    [Test]
    [Ignore("return to these after prototyping phase")]
    public async Task
        EncryptedFile_UploadedByTheOwner_IsOnlyDistributedTo_ConnectedFollowers_WithAccessInFileAcl_Of_A_SingleCircle_And_Deleted_When_Owner_Deletes_File()
    {
        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);
        var merryOwnerClient = await LoginAsOwner(Identities.Merry);
        var pippinOwnerClient = await LoginAsOwner(Identities.Pippin);

        //
        // Frodo, Sam, Merry, and Pippin are connected
        //
        await PeerFlow.ConnectAllAsync([frodoOwnerClient, merryOwnerClient, pippinOwnerClient, samOwnerClient],
            TargetDrive.NewTargetDrive());

        //
        // Sam follows Frodo
        //
        await FollowAsync(samOwnerClient, frodoOwnerClient);

        //create a channel drive
        var frodoSecureChannel = DataSubscriptionScenario.NewChannelDrive();

        await frodoOwnerClient.Admin.CreateDrive(frodoSecureChannel, "A Secured channel Drive", allowAnonymousReads: false,
            ownerOnly: false, allowSubscriptions: true);

        //
        // Frodo creates a circle named Mordor and puts Sam in it
        //
        var circleId = Guid.NewGuid();
        await frodoOwnerClient.Admin.CreateCircle(circleId, "Mordor",
            TestUtils.CreatePermissionGrantRequest(frodoSecureChannel, DrivePermission.All));

        var grantResponse = await frodoOwnerClient.Connections.GrantCircle(circleId, samOwnerClient.Identity);
        Assert.That(grantResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        //
        // Frodo Uploads a video to his feed for the Mordor circle
        //
        const string headerContent = "I'm Mr. Underhill; I think";
        const string payloadContent = "this could be a photo of me";
        var (uploadResult, encryptedJsonContent64, encryptedPayloadContent64) =
            await DataSubscriptionScenario.UploadStandardEncryptedFileWithPayloadToChannelAsync(
                frodoOwnerClient, frodoSecureChannel, headerContent, payloadContent, circleId);

        // Process the outbox since we're sending an encrypted file
        await frodoOwnerClient.Sync.DrainOutboxAsync();

        //
        // The header is distributed to the feed drive of Sam
        // Sam can get the payload via transit query
        //
        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);
        await DataSubscriptionScenario.AssertFeedDriveHasFileAsync(samOwnerClient,
            DataSubscriptionScenario.FeedQueryByGlobalTransitId(uploadResult), encryptedJsonContent64, uploadResult);
        await DataSubscriptionScenario.AssertCanGetPayloadAsync(samOwnerClient, frodoOwnerClient, uploadResult,
            encryptedPayloadContent64);

        //
        // The owner deletes the file
        //
        var deleteResponse = await frodoOwnerClient.V1.Drive.SoftDeleteFile(uploadResult.File);
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // The original drained the target drive, the transient temp drive and the feed drive "just in
        // case"; the drain is per-tenant here, so one call covers all three.
        await frodoOwnerClient.Sync.DrainOutboxAsync();

        //
        // Sam's feed drive no longer has the header
        // Sam can not get the payload via transit query
        //
        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);
        await DataSubscriptionScenario.AssertFeedDriveHasDeletedFileAsync(samOwnerClient, uploadResult);
        await DataSubscriptionScenario.AssertPayloadIs404Async(samOwnerClient, frodoOwnerClient, uploadResult);
    }

    private static async Task FollowAsync(OwnerSession follower, OwnerSession followee)
    {
        var response = await follower.V1.Follower.FollowIdentity(followee.Identity,
            FollowerNotificationType.AllNotifications);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }
}
