using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Connections;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// Port of <c>OwnerApi/Membership/Connections/CircleNetworkServiceAppTests</c>. How an app's
/// authorized circles turn into app-circle grants on a connected identity's ICR: whether the app is
/// registered before or after the connection, when the app's authorized-circle list is later
/// rewritten, and when a member is revoked from one of those circles.
/// </summary>
/// <remarks>
/// Arrange goes through <c>owner.Admin</c> (drives, circles, app registration); the two
/// app-management endpoints <c>OwnerAdmin</c> doesn't wrap — update-authorized-circles and
/// get-registered-app — and circle grant/revoke go through the V1 Refit interfaces via
/// <see cref="OwnerSession.RefitFor{T}"/>.
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

        #region Firstly, setup a chat app on Frodo's identity with a single circle and 2 drives (one for app, one random drive for circle)

        // Create a drive for the app
        var appDrive = TargetDrive.NewTargetDrive();
        await frodoOwnerClient.Admin.CreateDrive(appDrive, "Chat Drive 1", allowAnonymousReads: false);

        // Create a drive for the circle
        var circleDrive = TargetDrive.NewTargetDrive();
        await frodoOwnerClient.Admin.CreateDrive(circleDrive, "Random Circle Drive", allowAnonymousReads: false);

        // Create the chat friends circle and give it read/write to the circle drive
        var chatFriendsCircleId = Guid.NewGuid();
        await frodoOwnerClient.Admin.CreateCircle(chatFriendsCircleId, "Chat Friends Circle", new PermissionSetGrantRequest()
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
        });

        var documentSharingCircleId = Guid.NewGuid();
        await frodoOwnerClient.Admin.CreateCircle(documentSharingCircleId, "Document Sharing Circle", new PermissionSetGrantRequest()
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
        });

        // Send Sam connection request and grant him access to the chat friend's circle
        var circleIdsGrantedToRecipient = new List<GuidId>() { chatFriendsCircleId, documentSharingCircleId };
        await SendConnectionRequestTo(frodoOwnerClient, samOwnerClient.Identity, circleIdsGrantedToRecipient);

        // Sam must accept the connection request to apply the permissions
        var circlesGrantedToSender = new List<GuidId>();
        await AcceptConnectionRequest(samOwnerClient, frodoOwnerClient.Identity, circlesGrantedToSender);

        // Create the app - Note - if you add the app after the connection request is made, you are testing the register app function's ability to reconcile authorized circles
        var appId = Guid.NewGuid();

        // with app-permissions to the app_drive.  these will be full permissions to the drive and to reading connections
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

        // the chat friends circle can work with the app with the permissions of circle member grant
        var authorizedCircles = new List<Guid>() { chatFriendsCircleId, documentSharingCircleId };

        // circle member grant (i.e. what circles can do ) on the app_drive. the chat friends circle can write to the chat drive
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

        var appRegistration = (await frodoOwnerClient.Admin.RegisterApp(appId, appPermissionsGrant, authorizedCircles, circleMemberGrant)).Content;

        #endregion

        //
        // Testing
        //

        // Get Sam's connection info on Frodo's identity
        var samConnectionInfo = await GetConnectionInfo(frodoOwnerClient, samOwnerClient.Identity);
        Assert.That(samConnectionInfo.Status, Is.EqualTo(ConnectionStatus.Connected));

        var appGrants = samConnectionInfo.AccessGrant.AppGrants;
        var appKey = appRegistration.AppId.Value;
        Assert.That(appGrants.Count, Is.EqualTo(2), "there should be 2 app grants; chat, and the app created in this test");
        Assert.That(appGrants.Keys, Does.Contain(appKey), "The single dictionary item's key should match the single registered app");
        var chatAppCircleGrants = appGrants[appKey].ToList();
        Assert.That(chatAppCircleGrants, Is.Not.Null, "chatAppCircleGrants != null");

        Assert.That(chatAppCircleGrants.Count, Is.EqualTo(2), "There should be two app circle grants(chat friends and document sharing");
        var chatFriendCircleGrant = chatAppCircleGrants.Single(c => c.CircleId == chatFriendsCircleId);
        Assert.That(chatFriendCircleGrant.AppId, Is.EqualTo(appRegistration.AppId));
        Assert.That(chatFriendCircleGrant.CircleId.Value, Is.EqualTo(chatFriendsCircleId),
            "the circle id of the grant should match the chat friends circle");
        Assert.That(chatFriendCircleGrant.PermissionSet, Is.EqualTo(appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet),
            "The circle should be granted the app's circle member grant");

        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = chatFriendCircleGrant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            Assert.That(shouldBeOnlyOne, Is.Not.Null, "there should be one and only one drive matching the ap's circle member granted drive");
        }

        //Test document sharing circle
        var documentSharingCircleGrant = chatAppCircleGrants.Single(c => c.CircleId == documentSharingCircleId);
        Assert.That(documentSharingCircleGrant.AppId, Is.EqualTo(appRegistration.AppId));
        Assert.That(documentSharingCircleGrant.CircleId.Value, Is.EqualTo(documentSharingCircleId),
            "the circle id of the grant should match the chat friends circle");
        Assert.That(documentSharingCircleGrant.PermissionSet, Is.EqualTo(appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet),
            "The circle should be granted the app's circle member grant");

        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = documentSharingCircleGrant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            Assert.That(shouldBeOnlyOne, Is.Not.Null, "there should be one and only one drive matching the ap's circle member granted drive");
        }
    }

    [Test]
    public async Task CreateNewApp_WithAuthorizedCircles_BeforeSamIsConnected()
    {
        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        #region Firstly, setup a chat app on Frodo's identity with a single circle and 2 drives (one for app, one random drive for circle)

        // Create a drive for the app
        var appDrive = TargetDrive.NewTargetDrive();
        await frodoOwnerClient.Admin.CreateDrive(appDrive, "Chat Drive 1", allowAnonymousReads: false);

        // Create a drive for the circle
        var circleDrive = TargetDrive.NewTargetDrive();
        await frodoOwnerClient.Admin.CreateDrive(circleDrive, "Random Circle Drive", allowAnonymousReads: false);

        // Create the chat friends circle and give it read/write to the circle drive
        var chatFriendsCircleId = Guid.NewGuid();
        await frodoOwnerClient.Admin.CreateCircle(chatFriendsCircleId, "Chat Friends Circle", new PermissionSetGrantRequest()
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
        });

        // Create the app - Note - this test, we will create the app before a connection request  so we can test updating an app's authorized circles
        var appId = Guid.NewGuid();

        // with app-permissions to the app_drive.  these will be full permissions to the drive and to reading connections
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

        // the chat friends circle can work with the app with the permissions of circle member grant
        var authorizedCircles = new List<Guid>() { chatFriendsCircleId };

        // circle member grant (i.e. what circles can do ) on the app_drive. the chat friends circle can write to the chat drive
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

        //
        // Create the app before we send a request
        //
        var appRegistration = (await frodoOwnerClient.Admin.RegisterApp(appId, appPermissionsGrant, authorizedCircles, circleMemberGrant)).Content;

        #endregion

        // Send Sam connection request and grant him access to the chat friend's circle
        var circleIdsGrantedToRecipient = new List<GuidId>() { chatFriendsCircleId };
        await SendConnectionRequestTo(frodoOwnerClient, samOwnerClient.Identity, circleIdsGrantedToRecipient);

        // Sam must accept the connection request to apply the permissions
        var circlesGrantedToSender = new List<GuidId>();
        await AcceptConnectionRequest(samOwnerClient, frodoOwnerClient.Identity, circlesGrantedToSender);

        //
        // Testing
        //

        // Get Sam's connection info on Frodo's identity
        var samConnectionInfo = await GetConnectionInfo(frodoOwnerClient, samOwnerClient.Identity);
        Assert.That(samConnectionInfo.Status, Is.EqualTo(ConnectionStatus.Connected));

        var appGrants = samConnectionInfo.AccessGrant.AppGrants;
        var appKey = appRegistration.AppId.Value;
        Assert.That(appGrants.Count, Is.EqualTo(2), "there should be 2 app grants; chat, and the app created in this test");
        Assert.That(appGrants.Keys, Does.Contain(appKey), "The single dictionary item's key should match the single registered app");
        var chatAppCircleGrants = appGrants[appKey].ToList();
        Assert.That(chatAppCircleGrants, Is.Not.Null, "chatAppCircleGrants != null");

        Assert.That(chatAppCircleGrants.Count, Is.EqualTo(1), "There should be only one circle grant");
        var singleGrant = chatAppCircleGrants.First();
        Assert.That(singleGrant.AppId, Is.EqualTo(appRegistration.AppId));
        Assert.That(singleGrant.CircleId.Value, Is.EqualTo(chatFriendsCircleId),
            "the circle id of the grant should match the chat friends circle");
        Assert.That(singleGrant.PermissionSet, Is.EqualTo(appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet),
            "The circle should be granted the app's circle member grant");

        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = singleGrant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            Assert.That(shouldBeOnlyOne, Is.Not.Null, "there should be one and only one drive matching the ap's circle member granted drive");
        }

        //TODO: test circle grants:         samConnectionInfo.AccessGrant.CircleGrants
    }

    [Test]
    public async Task UpdateAuthorizedCircles_ByAddingOne_AndRemovingExisting()
    {
        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        #region Firstly, setup a chat app on Frodo's identity with a single circle and 2 drives (one for app, one random drive for circle)

        // Create a drive for the app
        var appDrive = TargetDrive.NewTargetDrive();
        await frodoOwnerClient.Admin.CreateDrive(appDrive, "Chat Drive 1", allowAnonymousReads: false);

        // Create a drive for the circle
        var circleDrive = TargetDrive.NewTargetDrive();
        await frodoOwnerClient.Admin.CreateDrive(circleDrive, "Random Circle Drive", allowAnonymousReads: false);

        // Create the chat friends circle and give it read/write to the circle drive
        var chatFriendsCircleId = Guid.NewGuid();
        await frodoOwnerClient.Admin.CreateCircle(chatFriendsCircleId, "Chat Friends Circle", new PermissionSetGrantRequest()
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
        });

        // Create the app - Note - this test, we will create the app before a connection request  so we can test updating an app's authorized circles
        var appId = Guid.NewGuid();

        // with app-permissions to the app_drive.  these will be full permissions to the drive and to reading connections
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

        // the chat friends circle can work with the app with the permissions of circle member grant
        var authorizedCircles = new List<Guid>() { chatFriendsCircleId };

        // circle member grant (i.e. what circles can do ) on the app_drive. the chat friends circle can write to the chat drive
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

        //
        // Create the app before we send a request
        //
        var appRegistration = (await frodoOwnerClient.Admin.RegisterApp(appId, appPermissionsGrant, authorizedCircles, circleMemberGrant)).Content;

        #endregion

        // Send Sam connection request and grant him access to the chat friend's circle
        var circleIdsGrantedToRecipient = new List<GuidId>() { chatFriendsCircleId };
        await SendConnectionRequestTo(frodoOwnerClient, samOwnerClient.Identity, circleIdsGrantedToRecipient);

        // Sam must accept the connection request to apply the permissions
        var circlesGrantedToSender = new List<GuidId>();
        await AcceptConnectionRequest(samOwnerClient, frodoOwnerClient.Identity, circlesGrantedToSender);

        //
        // Testing
        //

        // Get Sam's connection info on Frodo's identity
        var samConnectionInfo = await GetConnectionInfo(frodoOwnerClient, samOwnerClient.Identity);
        Assert.That(samConnectionInfo.Status, Is.EqualTo(ConnectionStatus.Connected));

        var appGrants = samConnectionInfo.AccessGrant.AppGrants;
        var appKey = appRegistration.AppId.Value;
        Assert.That(appGrants.Count, Is.EqualTo(2), "there should be 2 app grants; chat, and the app created in this test");

        Assert.That(appGrants.Keys, Does.Contain(appKey), "The single dictionary item's key should match the single registered app");
        var chatAppCircleGrants = appGrants[appKey].ToList();
        Assert.That(chatAppCircleGrants, Is.Not.Null, "chatAppCircleGrants != null");

        Assert.That(chatAppCircleGrants.Count, Is.EqualTo(1), "There should be only one circle grant");
        var singleGrant = chatAppCircleGrants.First();
        Assert.That(singleGrant.AppId, Is.EqualTo(appRegistration.AppId));
        Assert.That(singleGrant.CircleId.Value, Is.EqualTo(chatFriendsCircleId),
            "the circle id of the grant should match the chat friends circle");
        Assert.That(singleGrant.PermissionSet, Is.EqualTo(appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet),
            "The circle should be granted the app's circle member grant");

        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = singleGrant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            Assert.That(shouldBeOnlyOne, Is.Not.Null, "there should be one and only one drive matching the ap's circle member granted drive");
        }

        //TODO: test circle grants:         samConnectionInfo.AccessGrant.CircleGrants

        //
        // Update authorized Circles
        //

        // Creat a new circle
        var someNewCircleId = Guid.NewGuid();
        await frodoOwnerClient.Admin.CreateCircle(someNewCircleId, "Another Circle", new PermissionSetGrantRequest()
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
        });

        //add Sam into the circle;
        await GrantCircle(frodoOwnerClient, someNewCircleId, samOwnerClient.Identity);

        // Update the app, and only give it the new circle, but keep the same circle member grant
        var newAuthorizedCircles = new List<Guid>() { someNewCircleId };
        await UpdateAppAuthorizedCircles(frodoOwnerClient, appRegistration.AppId, newAuthorizedCircles,
            appRegistration.CircleMemberPermissionSetGrantRequest);

        // Test
        var updatedApp = await GetAppRegistration(frodoOwnerClient, appRegistration.AppId);
        Assert.That(updatedApp, Is.Not.Null, $"Could not retrieve the app {appId}");

        Assert.That(updatedApp.AuthorizedCircles, Is.EquivalentTo(newAuthorizedCircles), "Updated authorized circles are incorrect");
        Assert.That(updatedApp.CircleMemberPermissionSetGrantRequest.PermissionSet,
            Is.EqualTo(appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet),
            "updated app cirlce grant permission set did not match");
        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = updatedApp.CircleMemberPermissionSetGrantRequest.Drives.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            Assert.That(shouldBeOnlyOne, Is.Not.Null, "there should be one and only one drive matching the app's circle member granted drive");
        }

        // Test sam's identity to have new circle

        // Get Sam's connection info on Frodo's identity
        var updatedSamConnectionInfo = await GetConnectionInfo(frodoOwnerClient, samOwnerClient.Identity);
        Assert.That(updatedSamConnectionInfo.Status, Is.EqualTo(ConnectionStatus.Connected));

        var appGrants2 = updatedSamConnectionInfo.AccessGrant.AppGrants;
        Assert.That(appGrants2.Count, Is.EqualTo(2), "There should be 2 app grants because we added one and deleted one; plus the built-in chat grant");
        Assert.That(appGrants2.Keys, Does.Contain(appKey), "The single dictionary item's key should match the single registered app");
        var updatedChatAppCircleGrants = appGrants2[appKey].ToList();
        Assert.That(updatedChatAppCircleGrants, Is.Not.Null, "chatAppCircleGrants2 != null");

        Assert.That(updatedChatAppCircleGrants.Count, Is.EqualTo(1), "There should be only one circle grant");
        var updatedGrant = updatedChatAppCircleGrants.First();
        Assert.That(updatedGrant.AppId, Is.EqualTo(appRegistration.AppId));
        Assert.That(updatedGrant.CircleId.Value, Is.EqualTo(someNewCircleId),
            "the circle id of the grant should match the 'some new circle' circle");
        Assert.That(updatedGrant.PermissionSet, Is.EqualTo(appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet),
            "The circle should be granted the app's circle member grant");

        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = updatedGrant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            Assert.That(shouldBeOnlyOne, Is.Not.Null, "there should be one and only one drive matching the ap's circle member granted drive");
        }
    }

    [Test]
    public async Task AcceptedConnectionRequest_GrantsAppCircle()
    {
        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        #region Firstly, setup a chat app on Frodo's identity with a single circle and 2 drives (one for app, one random drive for circle)

        // Create a drive for the app
        var appDrive = TargetDrive.NewTargetDrive();
        await frodoOwnerClient.Admin.CreateDrive(appDrive, "Chat Drive 1", allowAnonymousReads: false);

        // Create a drive for the circle
        var circleDrive = TargetDrive.NewTargetDrive();
        await frodoOwnerClient.Admin.CreateDrive(circleDrive, "Random Circle Drive", allowAnonymousReads: false);

        // Create the chat friends circle and give it read/write to the circle drive
        var chatFriendsCircleId = Guid.NewGuid();
        await frodoOwnerClient.Admin.CreateCircle(chatFriendsCircleId, "Chat Friends Circle", new PermissionSetGrantRequest()
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
        });

        // Create the app - Note - this test, we will create the app before a connection request  so we can test updating an app's authorized circles
        var appId = Guid.NewGuid();

        // with app-permissions to the app_drive.  these will be full permissions to the drive and to reading connections
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

        // the chat friends circle can work with the app with the permissions of circle member grant
        var authorizedCircles = new List<Guid>() { chatFriendsCircleId };

        // circle member grant (i.e. what circles can do ) on the app_drive. the chat friends circle can write to the chat drive
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

        //
        // Create the app before we send a request
        //
        var appRegistration = (await frodoOwnerClient.Admin.RegisterApp(appId, appPermissionsGrant, authorizedCircles, circleMemberGrant)).Content;

        #endregion

        // Sam will send Frodo connection request.  Sam has given no access but frodo will give access to the chat friend's scirlce
        var circleIdsGrantedToRecipient = new List<GuidId>() { };
        await SendConnectionRequestTo(samOwnerClient, frodoOwnerClient.Identity, circleIdsGrantedToRecipient);

        // Frodo must accept the connection request.  this Should grant Sam access to the chat friend's circle
        var circlesGrantedToSender = new List<GuidId>() { chatFriendsCircleId };
        await AcceptConnectionRequest(frodoOwnerClient, samOwnerClient.Identity, circlesGrantedToSender);

        //
        // Testing
        //

        // Get Sam's connection info on Frodo's identity
        var samConnectionInfo = await GetConnectionInfo(frodoOwnerClient, samOwnerClient.Identity);
        Assert.That(samConnectionInfo.Status, Is.EqualTo(ConnectionStatus.Connected));

        var appGrants = samConnectionInfo.AccessGrant.AppGrants;
        var appKey = appRegistration.AppId.Value;
        Assert.That(appGrants.Count, Is.EqualTo(2), "there should be 2 app grants; chat, and the app created in this test");
        Assert.That(appGrants.Keys, Does.Contain(SystemAppConstants.ChatAppId));
        Assert.That(appGrants.Keys, Does.Contain(appId));
        Assert.That(appGrants.Keys, Does.Contain(appKey), "The single dictionary item's key should match the single registered app");
        var chatAppCircleGrants = appGrants[appKey].ToList();
        Assert.That(chatAppCircleGrants, Is.Not.Null, "chatAppCircleGrants != null");

        Assert.That(chatAppCircleGrants.Count, Is.EqualTo(1), "There should be only one circle grant");
        var singleGrant = chatAppCircleGrants.First();
        Assert.That(singleGrant.AppId, Is.EqualTo(appRegistration.AppId));
        Assert.That(singleGrant.CircleId.Value, Is.EqualTo(chatFriendsCircleId),
            "the circle id of the grant should match the chat friends circle");
        Assert.That(singleGrant.PermissionSet, Is.EqualTo(appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet),
            "The circle should be granted the app's circle member grant");

        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = singleGrant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            Assert.That(shouldBeOnlyOne, Is.Not.Null, "there should be one and only one drive matching the ap's circle member granted drive");
        }

        //TODO: test circle grants:         samConnectionInfo.AccessGrant.CircleGrants

        //
        // Update authorized Circles
        //

        // Creat a new circle
        var someNewCircleId = Guid.NewGuid();
        await frodoOwnerClient.Admin.CreateCircle(someNewCircleId, "Another Circle", new PermissionSetGrantRequest()
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
        });

        //add Sam into the circle;
        await GrantCircle(frodoOwnerClient, someNewCircleId, samOwnerClient.Identity);

        // Update the app, and only give it the new circle, but keep the same circle member grant
        var newAuthorizedCircles = new List<Guid>() { someNewCircleId };
        await UpdateAppAuthorizedCircles(frodoOwnerClient, appRegistration.AppId, newAuthorizedCircles,
            appRegistration.CircleMemberPermissionSetGrantRequest);

        // Test
        var updatedApp = await GetAppRegistration(frodoOwnerClient, appRegistration.AppId);
        Assert.That(updatedApp, Is.Not.Null, $"Could not retrieve the app {appId}");
        Assert.That(updatedApp.AuthorizedCircles, Is.EquivalentTo(newAuthorizedCircles), "Updated authorized circles are incorrect");
        Assert.That(updatedApp.CircleMemberPermissionSetGrantRequest.PermissionSet,
            Is.EqualTo(appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet),
            "updated app cirlce grant permission set did not match");
        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = updatedApp.CircleMemberPermissionSetGrantRequest.Drives.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            Assert.That(shouldBeOnlyOne, Is.Not.Null, "there should be one and only one drive matching the app's circle member granted drive");
        }

        // Test sam's identity to have new circle

        // Get Sam's connection info on Frodo's identity
        var updatedSamConnectionInfo = await GetConnectionInfo(frodoOwnerClient, samOwnerClient.Identity);
        Assert.That(updatedSamConnectionInfo.Status, Is.EqualTo(ConnectionStatus.Connected));

        var appGrants2 = updatedSamConnectionInfo.AccessGrant.AppGrants;
        Assert.That(appGrants2.Count, Is.EqualTo(2), "There should be 2 app grants because we added one and deleted one; and the built-in chat grant");
        Assert.That(appGrants2.Keys, Does.Contain(appKey), "The single dictionary item's key should match the single registered app");
        var updatedChatAppCircleGrants = appGrants2[appKey].ToList();
        Assert.That(updatedChatAppCircleGrants, Is.Not.Null, "chatAppCircleGrants2 != null");

        Assert.That(updatedChatAppCircleGrants.Count, Is.EqualTo(1), "There should be only one circle grant");
        var updatedGrant = updatedChatAppCircleGrants.First();
        Assert.That(updatedGrant.AppId, Is.EqualTo(appRegistration.AppId));
        Assert.That(updatedGrant.CircleId.Value, Is.EqualTo(someNewCircleId),
            "the circle id of the grant should match the 'some new circle' circle");
        Assert.That(updatedGrant.PermissionSet, Is.EqualTo(appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet),
            "The circle should be granted the app's circle member grant");

        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = updatedGrant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            Assert.That(shouldBeOnlyOne, Is.Not.Null, "there should be one and only one drive matching the ap's circle member granted drive");
        }
    }

    [Test]
    public async Task RevokeCircleFromAppAuthorizedCircles()
    {
        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        #region Firstly, setup a chat app on Frodo's identity with two circles and 2 drives (one for app, one random drive for circle)

        // Create a drive for the app
        var appDrive = TargetDrive.NewTargetDrive();
        await frodoOwnerClient.Admin.CreateDrive(appDrive, "Chat Drive 1", allowAnonymousReads: false);

        // Create a drive for the circle
        var circleDrive = TargetDrive.NewTargetDrive();
        await frodoOwnerClient.Admin.CreateDrive(circleDrive, "Random Circle Drive", allowAnonymousReads: false);

        // Create the chat friends circle and give it read/write to the circle drive
        var chatFriendsCircleId = Guid.NewGuid();
        await frodoOwnerClient.Admin.CreateCircle(chatFriendsCircleId, "Chat Friends Circle", new PermissionSetGrantRequest()
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
        });

        var documentShareCircleId = Guid.NewGuid();
        await frodoOwnerClient.Admin.CreateCircle(documentShareCircleId, "Circle for document sharing", new PermissionSetGrantRequest()
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
        });

        // Create the app - Note - this test, we will create the app before a connection request  so we can test updating an app's authorized circles
        var appId = Guid.NewGuid();

        // with app-permissions to the app_drive.  these will be full permissions to the drive and to reading connections
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

        // the chat friends circle can work with the app with the permissions of circle member grant
        var authorizedCircles = new List<Guid>() { chatFriendsCircleId, documentShareCircleId };

        // circle member grant (i.e. what circles can do ) on the app_drive. the chat friends circle can write to the chat drive
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

        //
        // Create the app before we send a request
        //
        var appRegistration = (await frodoOwnerClient.Admin.RegisterApp(appId, appPermissionsGrant, authorizedCircles, circleMemberGrant)).Content;

        #endregion

        // Frodo will send sam connection request with access to the two circles
        var circleIdsGrantedToRecipient = new List<GuidId>() { chatFriendsCircleId, documentShareCircleId };
        await SendConnectionRequestTo(frodoOwnerClient, samOwnerClient.Identity, circleIdsGrantedToRecipient);

        // Frodo must accept the connection request.  this Should grant Sam access to the chat friend's circle
        var circlesGrantedToSender = new List<GuidId>() { };
        await AcceptConnectionRequest(samOwnerClient, frodoOwnerClient.Identity, circlesGrantedToSender);

        //
        // Testing
        //

        // Get Sam's connection info on Frodo's identity
        var samConnectionInfo = await GetConnectionInfo(frodoOwnerClient, samOwnerClient.Identity);
        Assert.That(samConnectionInfo.Status, Is.EqualTo(ConnectionStatus.Connected));

        var appGrants = samConnectionInfo.AccessGrant.AppGrants;
        var appKey = appRegistration.AppId.Value;
        Assert.That(appGrants.Count, Is.EqualTo(2), "there should be 2 app grants; chat, and the app created in this test");

        Assert.That(appGrants.Keys, Does.Contain(appKey), "The single dictionary item's key should match the single registered app");
        var chatAppCircleGrantList = appGrants[appKey];
        Assert.That(chatAppCircleGrantList, Is.Not.Null, "chatAppCircleGrants != null");
        var chatAppCircleGrants = chatAppCircleGrantList.ToList();

        Assert.That(chatAppCircleGrants.Count, Is.EqualTo(2), "There should be two circle grant (chat friends and document share)");
        //Test chat friend's circle
        var chatFriendsCircleGrant = chatAppCircleGrants.Single(c => c.CircleId == chatFriendsCircleId);
        Assert.That(chatFriendsCircleGrant.AppId, Is.EqualTo(appRegistration.AppId));
        Assert.That(chatFriendsCircleGrant.CircleId.Value, Is.EqualTo(chatFriendsCircleId),
            "the circle id of the grant should match the chat friends circle");
        Assert.That(chatFriendsCircleGrant.PermissionSet, Is.EqualTo(appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet),
            "The circle should be granted the app's circle member grant");

        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = chatFriendsCircleGrant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            Assert.That(shouldBeOnlyOne, Is.Not.Null, "there should be one and only one drive matching the ap's circle member granted drive");
        }

        //Test document sharing circle
        var documentSharingCircle = chatAppCircleGrants.Single(c => c.CircleId == documentShareCircleId);
        Assert.That(documentSharingCircle.AppId, Is.EqualTo(appRegistration.AppId));
        Assert.That(documentSharingCircle.CircleId.Value, Is.EqualTo(documentShareCircleId),
            "the circle id of the grant should match the chat friends circle");
        Assert.That(documentSharingCircle.PermissionSet, Is.EqualTo(appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet),
            "The circle should be granted the app's circle member grant");

        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = documentSharingCircle.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            Assert.That(shouldBeOnlyOne, Is.Not.Null, "there should be one and only one drive matching the ap's circle member granted drive");
        }

        //
        // Revoke sam from the chat friend's circle
        //

        await RevokeCircle(frodoOwnerClient, chatFriendsCircleId, samOwnerClient.Identity);

        //
        //
        //


        // Test sam's identity to have not have the circle

        // Get Sam's connection info on Frodo's identity
        var updatedSamConnectionInfo = await GetConnectionInfo(frodoOwnerClient, samOwnerClient.Identity);
        Assert.That(updatedSamConnectionInfo.Status, Is.EqualTo(ConnectionStatus.Connected));

        var updatedAppGrants = updatedSamConnectionInfo.AccessGrant.AppGrants;
        Assert.That(updatedAppGrants.Count, Is.EqualTo(2), "There should still only be 2 app grants");
        Assert.That(updatedAppGrants.Keys, Does.Contain(appKey), "The single dictionary item's key should match the single registered app");
        var updatedChatAppCircleGrantList = updatedAppGrants[appKey];
        Assert.That(updatedChatAppCircleGrantList, Is.Not.Null, "chatAppCircleGrants2 != null");
        var updatedChatAppCircleGrants = updatedChatAppCircleGrantList.ToList();

        Assert.That(updatedChatAppCircleGrants.Count, Is.EqualTo(1), "There should be one circle grant");
        var updatedGrant = updatedChatAppCircleGrants.First();
        Assert.That(updatedGrant.AppId, Is.EqualTo(appRegistration.AppId));
        Assert.That(updatedGrant.CircleId.Value, Is.EqualTo(documentShareCircleId), "the circle id should be the documentSharing circle");
        Assert.That(updatedGrant.PermissionSet, Is.EqualTo(appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet),
            "The circle should be granted the app's circle member grant");

        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = updatedGrant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            Assert.That(shouldBeOnlyOne, Is.Not.Null, "there should be one and only one drive matching the ap's circle member granted drive");
        }
    }

    // -------------------------------------------------------------------------------------------
    // Local stand-ins for the V1 OwnerApiClient.Network / .Apps helpers the original used.
    // -------------------------------------------------------------------------------------------

    private static async Task SendConnectionRequestTo(OwnerSession sender, Odin.Core.Identity.OdinId recipient,
        List<GuidId> circlesGrantedToRecipient)
    {
        var response = await sender.Connections.SendConnectionRequest(recipient, circlesGrantedToRecipient);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static async Task AcceptConnectionRequest(OwnerSession recipient, Odin.Core.Identity.OdinId sender,
        List<GuidId> circleIdsGrantedToSender)
    {
        var response = await recipient.Connections.AcceptConnectionRequest(sender, circleIdsGrantedToSender);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static async Task<RedactedIdentityConnectionRegistration> GetConnectionInfo(OwnerSession owner,
        Odin.Core.Identity.OdinId recipient)
    {
        var response = await owner.Connections.GetConnectionInfo(recipient);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content, Is.Not.Null, $"No status for {recipient} found");
        return response.Content;
    }

    private static async Task GrantCircle(OwnerSession owner, Guid circleId, Odin.Core.Identity.OdinId recipient)
    {
        var response = await owner.RefitFor<IRefitOwnerCircleNetworkConnections>()
            .AddCircle(new AddCircleMembershipRequest() { CircleId = circleId, OdinId = recipient });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static async Task RevokeCircle(OwnerSession owner, Guid circleId, Odin.Core.Identity.OdinId recipient)
    {
        var response = await owner.RefitFor<IRefitOwnerCircleNetworkConnections>()
            .RevokeCircle(new RevokeCircleMembershipRequest() { CircleId = circleId, OdinId = recipient });
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
