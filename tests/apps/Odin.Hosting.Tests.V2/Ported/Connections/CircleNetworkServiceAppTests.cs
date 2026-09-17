using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;
using static Odin.Hosting.Tests.V2.Ported.Connections.ConnectionAsserts;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// Port of <c>OwnerApi/Membership/Connections/CircleNetworkServiceAppTests</c>. How an app's
/// authorized circles turn into app-circle grants on a connected identity's ICR: whether the app is
/// registered before or after the connection, when the app's authorized-circle list is later
/// rewritten, and when a member is revoked from one of those circles.
/// </summary>
/// <remarks>
/// Arrange goes through <c>owner.Admin</c> (drives, circles, app registration) and
/// <c>owner.Connections</c> (send / accept / grant-circle / revoke-circle / connection info); the two
/// app-management endpoints neither handle wraps — update-authorized-circles and get-registered-app —
/// go through the V1 Refit interface via <see cref="OwnerSession.RefitFor{T}"/>.
/// <para>
/// All five tests opened with the same ~80-line chat-app arrangement (app drive, circle drive, one or
/// two circles, app registration) and closed on the same app-circle-grant assertions; those are
/// <see cref="SetupChatAppAsync"/> and <see cref="AssertAppCircleGrant"/> here. Only the circle count
/// and the ordering of the app registration relative to the connection request ever varied — the
/// latter is the <c>beforeAppRegistration</c> hook. The second circle's name differed between the two
/// tests that used one ("Document Sharing Circle" / "Circle for document sharing"); no assertion reads
/// a circle name, so the helper spells it one way.
/// </para>
/// <para>
/// The original created circles with <c>client.Membership.CreateCircle</c>, which generates the
/// circle id itself and returns the definition; here the ids are generated test-side. The only
/// thing these tests read off a circle is its id. <c>owner.Admin.CreateCircle</c> spells the
/// description as <c>"Description for {name}"</c> — unread here — and
/// <c>owner.Admin.RegisterApp</c> sends the same <c>Name = "Test_{appId}"</c> the original's
/// <c>Apps.RegisterApp</c> did, plus a null <c>AppSlug</c> (server-derived), which no assertion
/// reads either.
/// </para>
/// <para>
/// Every test ended with a pair of <c>DisconnectFrom</c> calls restoring state for the next test;
/// pure lifecycle, now owned by per-test reset, so dropped. <c>SetupCallerWithOwner</c> ordering is
/// not in play — no caller matrix, <c>LoginAsOwner</c> only.
/// </para>
/// </remarks>
[TestFixture]
public class CircleNetworkServiceAppTests : V2Fixture
{

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task CreateNewApp_WithAuthorizedCircles_AfterSamIsConnected_Multiple_Circles()
    {
        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        // Note - the app is created *after* the connection request is made, so this exercises the
        // register-app function's ability to reconcile authorized circles against a live connection.
        var setup = await SetupChatAppAsync(frodoOwnerClient, circleCount: 2, beforeAppRegistration: async circleIds =>
        {
            // Send Sam a connection request granting him both circles
            await SendConnectionRequestTo(frodoOwnerClient, samOwnerClient.Identity, ToCircleIds(circleIds));

            // Sam must accept the connection request to apply the permissions
            await AcceptConnectionRequest(samOwnerClient, frodoOwnerClient.Identity, new List<GuidId>());
        });

        //
        // Testing
        //

        var appGrants = await GetConnectedAppGrants(frodoOwnerClient, samOwnerClient.Identity);
        var chatAppCircleGrants = AppCircleGrantsFor(appGrants, setup.App, expectedCount: 2);

        AssertAppCircleGrant(chatAppCircleGrants, setup.ChatFriendsCircleId, setup.App);
        AssertAppCircleGrant(chatAppCircleGrants, setup.DocumentSharingCircleId, setup.App);
    }

    [Test]
    public async Task CreateNewApp_WithAuthorizedCircles_BeforeSamIsConnected()
    {
        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        // The app is created before the connection request
        var setup = await SetupChatAppAsync(frodoOwnerClient, circleCount: 1);

        // Send Sam connection request and grant him access to the chat friend's circle
        await SendConnectionRequestTo(frodoOwnerClient, samOwnerClient.Identity, ToCircleIds(setup.CircleIds));

        // Sam must accept the connection request to apply the permissions
        await AcceptConnectionRequest(samOwnerClient, frodoOwnerClient.Identity, new List<GuidId>());

        //
        // Testing
        //

        var appGrants = await GetConnectedAppGrants(frodoOwnerClient, samOwnerClient.Identity);
        var chatAppCircleGrants = AppCircleGrantsFor(appGrants, setup.App, expectedCount: 1);

        AssertAppCircleGrant(chatAppCircleGrants, setup.ChatFriendsCircleId, setup.App);

        //TODO: test circle grants:         samConnectionInfo.AccessGrant.CircleGrants
    }

    [Test]
    public async Task UpdateAuthorizedCircles_ByAddingOne_AndRemovingExisting()
    {
        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        // The app is created before the connection request, so we can test updating its authorized circles
        var setup = await SetupChatAppAsync(frodoOwnerClient, circleCount: 1);

        // Send Sam connection request and grant him access to the chat friend's circle
        await SendConnectionRequestTo(frodoOwnerClient, samOwnerClient.Identity, ToCircleIds(setup.CircleIds));

        // Sam must accept the connection request to apply the permissions
        await AcceptConnectionRequest(samOwnerClient, frodoOwnerClient.Identity, new List<GuidId>());

        //
        // Testing
        //

        var appGrants = await GetConnectedAppGrants(frodoOwnerClient, samOwnerClient.Identity);
        var chatAppCircleGrants = AppCircleGrantsFor(appGrants, setup.App, expectedCount: 1);

        AssertAppCircleGrant(chatAppCircleGrants, setup.ChatFriendsCircleId, setup.App);

        //TODO: test circle grants:         samConnectionInfo.AccessGrant.CircleGrants

        await ReplaceAuthorizedCirclesWithNewCircle(frodoOwnerClient, samOwnerClient.Identity, setup);
    }

    [Test]
    public async Task AcceptedConnectionRequest_GrantsAppCircle()
    {
        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        // The app is created before the connection request
        var setup = await SetupChatAppAsync(frodoOwnerClient, circleCount: 1);

        // Sam will send Frodo a connection request. Sam grants no access, but Frodo will grant access
        // to the chat friend's circle when he accepts.
        await SendConnectionRequestTo(samOwnerClient, frodoOwnerClient.Identity, new List<GuidId>());
        await AcceptConnectionRequest(frodoOwnerClient, samOwnerClient.Identity, ToCircleIds(setup.CircleIds));

        //
        // Testing
        //

        var appGrants = await GetConnectedAppGrants(frodoOwnerClient, samOwnerClient.Identity);
        Assert.That(appGrants.Keys, Does.Contain(SystemAppConstants.ChatAppId));
        Assert.That(appGrants.Keys, Does.Contain(setup.App.AppId.Value));

        var chatAppCircleGrants = AppCircleGrantsFor(appGrants, setup.App, expectedCount: 1);
        AssertAppCircleGrant(chatAppCircleGrants, setup.ChatFriendsCircleId, setup.App);

        //TODO: test circle grants:         samConnectionInfo.AccessGrant.CircleGrants

        await ReplaceAuthorizedCirclesWithNewCircle(frodoOwnerClient, samOwnerClient.Identity, setup);
    }

    [Test]
    public async Task RevokeCircleFromAppAuthorizedCircles()
    {
        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        // The app is created before the connection request
        var setup = await SetupChatAppAsync(frodoOwnerClient, circleCount: 2);

        // Frodo sends Sam a connection request with access to the two circles
        await SendConnectionRequestTo(frodoOwnerClient, samOwnerClient.Identity, ToCircleIds(setup.CircleIds));
        await AcceptConnectionRequest(samOwnerClient, frodoOwnerClient.Identity, new List<GuidId>());

        //
        // Testing
        //

        var appGrants = await GetConnectedAppGrants(frodoOwnerClient, samOwnerClient.Identity);
        var chatAppCircleGrants = AppCircleGrantsFor(appGrants, setup.App, expectedCount: 2);

        AssertAppCircleGrant(chatAppCircleGrants, setup.ChatFriendsCircleId, setup.App);
        AssertAppCircleGrant(chatAppCircleGrants, setup.DocumentSharingCircleId, setup.App);

        //
        // Revoke sam from the chat friend's circle
        //

        await RevokeCircle(frodoOwnerClient, setup.ChatFriendsCircleId, samOwnerClient.Identity);

        //
        // Sam should no longer have the revoked circle's app grant, only the document sharing one
        //

        var updatedAppGrants = await GetConnectedAppGrants(frodoOwnerClient, samOwnerClient.Identity);
        var updatedChatAppCircleGrants = AppCircleGrantsFor(updatedAppGrants, setup.App, expectedCount: 1);

        AssertAppCircleGrant(updatedChatAppCircleGrants, setup.DocumentSharingCircleId, setup.App);
    }

    // -------------------------------------------------------------------------------------------
    // Arrange
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The chat-app arrangement every test here starts from: a drive for the app, a drive for the
    /// circles, <paramref name="circleCount"/> circles holding read/write on the circle drive, and an
    /// app registered with full permission on the app drive, those circles as its authorized circles,
    /// and write on the app drive as its circle-member grant.
    /// </summary>
    /// <param name="beforeAppRegistration">
    /// Runs after the circles exist but before the app is registered — the one test that connects the
    /// two identities first (to exercise authorized-circle reconciliation) hangs its handshake here.
    /// </param>
    private static async Task<ChatAppSetup> SetupChatAppAsync(OwnerSession owner, int circleCount,
        Func<List<Guid>, Task> beforeAppRegistration = null)
    {
        // Create a drive for the app
        var appDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(appDrive, "Chat Drive 1", allowAnonymousReads: false);

        // Create a drive for the circles
        var circleDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(circleDrive, "Random Circle Drive", allowAnonymousReads: false);

        // Create the circles and give each read/write to the circle drive
        var circleNames = new[] { "Chat Friends Circle", "Document Sharing Circle" };
        var circleIds = new List<Guid>();
        for (var i = 0; i < circleCount; i++)
        {
            var circleId = Guid.NewGuid();
            await owner.Admin.CreateCircle(circleId, circleNames[i], CircleDriveGrant(circleDrive));
            circleIds.Add(circleId);
        }

        if (beforeAppRegistration != null)
        {
            await beforeAppRegistration(circleIds);
        }

        // app-permissions to the app drive: full permissions to the drive and to reading connections
        var appPermissionsGrant = new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = appDrive,
                        Permission = DrivePermission.All
                    }
                }
            },
            PermissionSet = new PermissionSet(PermissionKeys.All)
        };

        // circle member grant (i.e. what circles can do) on the app drive: the circles can write to it
        var circleMemberGrant = new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = appDrive,
                        Permission = DrivePermission.Write
                    }
                }
            },
            PermissionSet = null
        };

        var appRegistration = (await owner.Admin.RegisterApp(Guid.NewGuid(), appPermissionsGrant, circleIds, circleMemberGrant)).Content;

        return new ChatAppSetup(appDrive, circleDrive, circleIds, appRegistration);
    }

    /// <summary>What <see cref="SetupChatAppAsync"/> arranged, as the tests read it back.</summary>
    private sealed record ChatAppSetup(
        TargetDrive AppDrive,
        TargetDrive CircleDrive,
        List<Guid> CircleIds,
        RedactedAppRegistration App)
    {
        public Guid ChatFriendsCircleId => CircleIds[0];
        public Guid DocumentSharingCircleId => CircleIds[1];
    }

    private static PermissionSetGrantRequest CircleDriveGrant(TargetDrive circleDrive) => new()
    {
        PermissionSet = new PermissionSet(),
        Drives = new[]
        {
            new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = circleDrive,
                    Permission = DrivePermission.ReadWrite
                }
            }
        }
    };

    private static List<GuidId> ToCircleIds(IEnumerable<Guid> circleIds) => circleIds.Select(c => (GuidId)c).ToList();

    /// <summary>
    /// Creates one more circle, puts <paramref name="member"/> in it, rewrites the app's authorized
    /// circles to that circle alone (keeping the circle-member grant), then asserts both the updated
    /// registration and the member's single resulting app-circle grant.
    /// </summary>
    private static async Task ReplaceAuthorizedCirclesWithNewCircle(OwnerSession owner, OdinId member, ChatAppSetup setup)
    {
        // Create a new circle
        var someNewCircleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(someNewCircleId, "Another Circle", CircleDriveGrant(setup.CircleDrive));

        //add the member into the circle;
        await GrantCircle(owner, someNewCircleId, member);

        // Update the app, and only give it the new circle, but keep the same circle member grant
        var newAuthorizedCircles = new List<Guid>() { someNewCircleId };
        await UpdateAppAuthorizedCircles(owner, setup.App.AppId, newAuthorizedCircles,
            setup.App.CircleMemberPermissionSetGrantRequest);

        // Test
        var updatedApp = await GetAppRegistration(owner, setup.App.AppId);
        Assert.That(updatedApp, Is.Not.Null, $"Could not retrieve the app {setup.App.AppId}");

        Assert.That(updatedApp.AuthorizedCircles, Is.EquivalentTo(newAuthorizedCircles), "Updated authorized circles are incorrect");
        Assert.That(updatedApp.CircleMemberPermissionSetGrantRequest.PermissionSet,
            Is.EqualTo(setup.App.CircleMemberPermissionSetGrantRequest.PermissionSet),
            "updated app cirlce grant permission set did not match");
        foreach (var d in setup.App.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = updatedApp.CircleMemberPermissionSetGrantRequest.Drives.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            Assert.That(shouldBeOnlyOne, Is.Not.Null, "there should be one and only one drive matching the app's circle member granted drive");
        }

        // The member's identity should now carry the new circle, and only it
        var updatedAppGrants = await GetConnectedAppGrants(owner, member);
        var updatedChatAppCircleGrants = AppCircleGrantsFor(updatedAppGrants, setup.App, expectedCount: 1);

        AssertAppCircleGrant(updatedChatAppCircleGrants, someNewCircleId, setup.App);
    }

    // -------------------------------------------------------------------------------------------
    // Assert
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads <paramref name="member"/>'s connection info off <paramref name="owner"/>'s identity,
    /// asserting the two are connected, and hands back the app grants on that ICR.
    /// </summary>
    private static async Task<Dictionary<Guid, IEnumerable<RedactedAppCircleGrant>>> GetConnectedAppGrants(
        OwnerSession owner, OdinId member)
    {
        var connectionInfo = await GetConnectionInfo(owner, member);
        Assert.That(connectionInfo.Status, Is.EqualTo(ConnectionStatus.Connected));

        var appGrants = connectionInfo.AccessGrant.AppGrants;
        Assert.That(appGrants.Count, Is.EqualTo(2), "there should be 2 app grants; the built-in chat app and the app created in this test");
        return appGrants;
    }

    /// <summary>The app-circle grants one app holds on an ICR, asserted to be <paramref name="expectedCount"/> of them.</summary>
    private static List<RedactedAppCircleGrant> AppCircleGrantsFor(
        Dictionary<Guid, IEnumerable<RedactedAppCircleGrant>> appGrants, RedactedAppRegistration app, int expectedCount)
    {
        var appKey = app.AppId.Value;
        Assert.That(appGrants.Keys, Does.Contain(appKey), "the app registered by this test should have a grant");

        var grantsForApp = appGrants[appKey];
        Assert.That(grantsForApp, Is.Not.Null);

        var grants = grantsForApp.ToList();
        Assert.That(grants.Count, Is.EqualTo(expectedCount));
        return grants;
    }

    /// <summary>
    /// One app-circle grant for <paramref name="expectedCircleId"/> exists, carrying the app's
    /// circle-member permission set and one drive grant per drive in it.
    /// </summary>
    private static void AssertAppCircleGrant(IEnumerable<RedactedAppCircleGrant> appCircleGrants, Guid expectedCircleId,
        RedactedAppRegistration app)
    {
        var grant = appCircleGrants.SingleOrDefault(c => c.CircleId == expectedCircleId);
        Assert.That(grant, Is.Not.Null, $"there should be exactly one app circle grant for circle {expectedCircleId}");
        Assert.That(grant.AppId, Is.EqualTo(app.AppId));
        Assert.That(grant.PermissionSet, Is.EqualTo(app.CircleMemberPermissionSetGrantRequest.PermissionSet),
            "The circle should be granted the app's circle member grant");

        foreach (var d in app.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = grant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            Assert.That(shouldBeOnlyOne, Is.Not.Null, "there should be one and only one drive matching the app's circle member granted drive");
        }
    }

    // -------------------------------------------------------------------------------------------
    // Local stand-ins for the V1 OwnerApiClient.Network / .Apps helpers the original used.
    // -------------------------------------------------------------------------------------------

    private static async Task SendConnectionRequestTo(OwnerSession sender, OdinId recipient, List<GuidId> circlesGrantedToRecipient)
    {
        var response = await sender.Connections.SendConnectionRequest(recipient, circlesGrantedToRecipient);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static async Task AcceptConnectionRequest(OwnerSession recipient, OdinId sender, List<GuidId> circleIdsGrantedToSender)
    {
        var response = await recipient.Connections.AcceptConnectionRequest(sender, circleIdsGrantedToSender);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static async Task GrantCircle(OwnerSession owner, Guid circleId, OdinId recipient)
    {
        var response = await owner.Connections.GrantCircle(circleId, recipient);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static async Task RevokeCircle(OwnerSession owner, Guid circleId, OdinId recipient)
    {
        var response = await owner.Connections.RevokeCircle(circleId, recipient);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static Task UpdateAppAuthorizedCircles(OwnerSession owner, Guid appId, List<Guid> authorizedCircles,
        PermissionSetGrantRequest grant)
    {
        return owner.RefitFor<IRefitOwnerAppRegistration>().UpdateAuthorizedCircles(new UpdateAuthorizedCirclesRequest()
        {
            AppId = appId,
            AuthorizedCircles = authorizedCircles,
            CircleMemberPermissionGrant = grant
        });
    }

    private static async Task<RedactedAppRegistration> GetAppRegistration(OwnerSession owner, Guid appId)
    {
        var response = await owner.RefitFor<IRefitOwnerAppRegistration>().GetRegisteredApp(new GetAppRequest() { AppId = appId });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"Could not retrieve the app {appId}");
        return response.Content;
    }
}
