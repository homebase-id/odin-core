using System;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Ported.DataSubscription;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/OwnerApi/DataSubscription/DataSubscriptionAndDistributionTests2.cs
///
/// An encrypted post whose ACL names a single circle reaches only the followers in that circle: the
/// member gets the header in their feed and can pull the payload over peer query, the two non-members
/// get neither — and when the owner deletes the post, the member's feed copy is marked deleted and the
/// payload answers 404.
/// </summary>
/// <remarks>
/// Checked port. No caller matrix in the original and none here.
/// <list type="bullet">
/// <item><c>_scaffold.Scenarios.CreateConnectedHobbits</c> becomes
/// <see cref="PeerFlow.ConnectAllAsync"/>. The V1 helper also registered an app per identity and
/// carried its token on a <c>TestAppContext</c>; nothing here reads a token — every call is made as an
/// owner — so that half is dropped.</item>
/// <item><c>Membership.CreateCircle(name, drive, permission)</c> becomes
/// <c>Admin.CreateCircle</c> over <c>TestUtils.CreatePermissionGrantRequest</c>, which builds the same
/// grant (one drive, empty permission set). The V1 client also read the definition back and asserted
/// the grant round-tripped; that is circle-definition coverage owned by the circle fixtures, not by
/// this one.</item>
/// <item>Every <c>WaitForEmptyOutbox</c> becomes <c>Sync.DrainOutboxAsync()</c> and every
/// <c>ProcessInbox(FeedDrive)</c> becomes <c>Sync.ProcessInboxAsync(FeedDrive)</c>. The delete test
/// drained three times "just in case" (target drive, transient temp drive, feed drive); the drain is
/// per-tenant here, so one call replaces the three and the comment is kept.</item>
/// <item>The <c>TransitQuery.GetPayload</c> assertions are
/// <see cref="DataSubscriptionScenario.AssertCanGetPayloadAsync"/> and friends, which reach the same
/// endpoint through <c>IUniversalRefitPeerQuery</c>. The channel-drive create and the follow call come
/// from the same place, so the drive name is the only thing this fixture still spells out.</item>
/// <item>Trailing unfollow / disconnect calls were cleanup only and are dropped — per-test reset covers
/// them. That leaves Merry and Pippin unused in the delete test beyond the mesh connect, exactly as in
/// the original, where their only other appearance was in that cleanup.</item>
/// </list>
/// </remarks>
[TestFixture]
public class DataSubscriptionAndDistributionTests2 : V2Fixture
{
    protected override string[] HostIdentities =>
        [Identities.Frodo, Identities.Sam, Identities.Pippin, Identities.Merry];

    [Test]
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
        await DataSubscriptionScenario.FollowAsync(samOwnerClient, frodoOwnerClient);
        await DataSubscriptionScenario.FollowAsync(merryOwnerClient, frodoOwnerClient);
        await DataSubscriptionScenario.FollowAsync(pippinOwnerClient, frodoOwnerClient);

        //create a channel drive
        var frodoSecureChannel = await DataSubscriptionScenario.CreateChannelDriveAsync(frodoOwnerClient,
            name: "A Secured channel Drive");

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
        await DataSubscriptionScenario.FollowAsync(samOwnerClient, frodoOwnerClient);

        //create a channel drive
        var frodoSecureChannel = await DataSubscriptionScenario.CreateChannelDriveAsync(frodoOwnerClient,
            name: "A Secured channel Drive");

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
}
