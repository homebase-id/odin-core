using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Services.Apps;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Tests.OwnerApi.Membership.Connections;

public class CircleNetworkServiceAppTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests();
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
    public async Task CreateNewApp_WithAuthorizedCircles_AfterSamIsConnected_Multiple_Circles()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        #region Firstly, setup a chat app on Frodo's identity with a single circle and 2 drives (one for app, one random drive for circle)

        // Create a drive for the app
        var appDrive = await frodoOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Chat Drive 1", "", false);

        // Create a drive for the circle
        var circleDrive = await frodoOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Random Circle Drive", "", false);

        // Create the chat friends circle and give it read/write to the circle drive
        var chatFriendsCircle = await frodoOwnerClient.Membership.CreateCircle("Chat Friends Circle", new PermissionSetGrantRequest()
        {
            PermissionSet = new PermissionSet(),
            Drives = new[]
            {
                new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = circleDrive.TargetDriveInfo,
                        Permission = DrivePermission.ReadWrite
                    }
                }
            }
        });

        var documentSharingCircle = await frodoOwnerClient.Membership.CreateCircle("Document Sharing Circle", new PermissionSetGrantRequest()
        {
            PermissionSet = new PermissionSet(),
            Drives = new[]
            {
                new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = circleDrive.TargetDriveInfo,
                        Permission = DrivePermission.ReadWrite
                    }
                }
            }
        });

        // Send Sam connection request and grant him access to the chat friend's circle
        var circleIdsGrantedToRecipient = new List<GuidId>() { chatFriendsCircle.Id, documentSharingCircle.Id };
        await frodoOwnerClient.Network.SendConnectionRequestTo(samOwnerClient.Identity, circleIdsGrantedToRecipient);

        // Sam must accept the connection request to apply the permissions
        var circlesGrantedToSender = new List<GuidId>();
        await samOwnerClient.Network.AcceptConnectionRequest(frodoOwnerClient.Identity, circlesGrantedToSender);

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
                        Drive = appDrive.TargetDriveInfo,
                        Permission = DrivePermission.All
                    }
                }
            },
            PermissionSet = new PermissionSet(PermissionKeys.All)
        };

        // the chat friends circle can work with the app with the permissions of circle member grant
        var authorizedCircles = new List<Guid>() { chatFriendsCircle.Id, documentSharingCircle.Id };

        // circle member grant (i.e. what circles can do ) on the app_drive. the chat friends circle can write to the chat drive
        var circleMemberGrant = new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = appDrive.TargetDriveInfo,
                        Permission = DrivePermission.Write
                    }
                }
            },
            PermissionSet = null
        };

        var appRegistration = await frodoOwnerClient.Apps.RegisterApp(appId, appPermissionsGrant, authorizedCircles, circleMemberGrant);

        #endregion

        //
        // Testing
        //

        // Get Sam's connection info on Frodo's identity
        var samConnectionInfo = await frodoOwnerClient.Network.GetConnectionInfo(samOwnerClient.Identity);
        ClassicAssert.IsTrue(samConnectionInfo.Status == ConnectionStatus.Connected);

        var appGrants = samConnectionInfo.AccessGrant.AppGrants;
        var appKey = appRegistration.AppId.Value;
        ClassicAssert.IsTrue(appGrants.Count == 3, "there should be 3 app grants; mail, chat, and the app created in this test");
        ClassicAssert.IsTrue(appGrants.TryGetValue(appKey, out var chatAppCircleGrants), "The single dictionary item's key should match the single registered app");
        ClassicAssert.IsNotNull(chatAppCircleGrants, "chatAppCircleGrants != null");

        // ReSharper disable once PossibleMultipleEnumeration
        ClassicAssert.IsTrue(chatAppCircleGrants.Count() == 2, "There should be two app circle grants(chat friends and document sharing");
        // ReSharper disable once PossibleMultipleEnumeration
        var chatFriendCircleGrant = chatAppCircleGrants.Single(c => c.CircleId == chatFriendsCircle.Id);
        ClassicAssert.IsTrue(chatFriendCircleGrant.AppId == appRegistration.AppId);
        ClassicAssert.IsTrue(chatFriendCircleGrant.CircleId == chatFriendsCircle.Id, "the circle id of the grant should match the chat friends circle");
        ClassicAssert.IsTrue(chatFriendCircleGrant.PermissionSet == appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet, "The circle should be granted the app's circle member grant");

        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = chatFriendCircleGrant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            ClassicAssert.IsNotNull(shouldBeOnlyOne, "there should be one and only one drive matching the ap's circle member granted drive");
        }

        //Test document sharing circle
        var documentSharingCircleGrant = chatAppCircleGrants.Single(c => c.CircleId == documentSharingCircle.Id);
        ClassicAssert.IsTrue(documentSharingCircleGrant.AppId == appRegistration.AppId);
        ClassicAssert.IsTrue(documentSharingCircleGrant.CircleId == documentSharingCircle.Id, "the circle id of the grant should match the chat friends circle");
        ClassicAssert.IsTrue(documentSharingCircleGrant.PermissionSet == appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet, "The circle should be granted the app's circle member grant");

        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = documentSharingCircleGrant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            ClassicAssert.IsNotNull(shouldBeOnlyOne, "there should be one and only one drive matching the ap's circle member granted drive");
        }


        // All done
        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task CreateNewApp_WithAuthorizedCircles_BeforeSamIsConnected()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        #region Firstly, setup a chat app on Frodo's identity with a single circle and 2 drives (one for app, one random drive for circle)

        // Create a drive for the app
        var appDrive = await frodoOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Chat Drive 1", "", false);

        // Create a drive for the circle
        var circleDrive = await frodoOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Random Circle Drive", "", false);

        // Create the chat friends circle and give it read/write to the circle drive
        var chatFriendsCircle = await frodoOwnerClient.Membership.CreateCircle("Chat Friends Circle", new PermissionSetGrantRequest()
        {
            PermissionSet = new PermissionSet(),
            Drives = new[]
            {
                new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = circleDrive.TargetDriveInfo,
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
                        Drive = appDrive.TargetDriveInfo,
                        Permission = DrivePermission.All
                    }
                }
            },
            PermissionSet = new PermissionSet(PermissionKeys.All)
        };

        // the chat friends circle can work with the app with the permissions of circle member grant
        var authorizedCircles = new List<Guid>() { chatFriendsCircle.Id };

        // circle member grant (i.e. what circles can do ) on the app_drive. the chat friends circle can write to the chat drive
        var circleMemberGrant = new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = appDrive.TargetDriveInfo,
                        Permission = DrivePermission.Write
                    }
                }
            },
            PermissionSet = null
        };

        //
        // Create the app before we send a request
        //
        var appRegistration = await frodoOwnerClient.Apps.RegisterApp(appId, appPermissionsGrant, authorizedCircles, circleMemberGrant);

        #endregion

        // Send Sam connection request and grant him access to the chat friend's circle
        var circleIdsGrantedToRecipient = new List<GuidId>() { chatFriendsCircle.Id };
        await frodoOwnerClient.Network.SendConnectionRequestTo(TestIdentities.Samwise, circleIdsGrantedToRecipient);

        // Sam must accept the connection request to apply the permissions
        var circlesGrantedToSender = new List<GuidId>();
        await samOwnerClient.Network.AcceptConnectionRequest(frodoOwnerClient.Identity, circlesGrantedToSender);

        //
        // Testing
        //

        // Get Sam's connection info on Frodo's identity
        var samConnectionInfo = await frodoOwnerClient.Network.GetConnectionInfo(samOwnerClient.Identity);
        ClassicAssert.IsTrue(samConnectionInfo.Status == ConnectionStatus.Connected);

        var appGrants = samConnectionInfo.AccessGrant.AppGrants;
        var appKey = appRegistration.AppId.Value;
        ClassicAssert.IsTrue(appGrants.Count == 3, "there should be 3 app grants; mail, chat, and the app created in this test");
        ClassicAssert.IsTrue(appGrants.TryGetValue(appKey, out var chatAppCircleGrants), "The single dictionary item's key should match the single registered app");
        ClassicAssert.IsNotNull(chatAppCircleGrants, "chatAppCircleGrants != null");

        // ReSharper disable once PossibleMultipleEnumeration
        ClassicAssert.IsTrue(chatAppCircleGrants.Count() == 1, "There should be only one circle grant");
        // ReSharper disable once PossibleMultipleEnumeration
        var singleGrant = chatAppCircleGrants.First();
        ClassicAssert.IsTrue(singleGrant.AppId == appRegistration.AppId);
        ClassicAssert.IsTrue(singleGrant.CircleId == chatFriendsCircle.Id, "the circle id of the grant should match the chat friends circle");
        ClassicAssert.IsTrue(singleGrant.PermissionSet == appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet, "The circle should be granted the app's circle member grant");

        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = singleGrant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            ClassicAssert.IsNotNull(shouldBeOnlyOne, "there should be one and only one drive matching the ap's circle member granted drive");
        }

        //TODO: test circle grants:         samConnectionInfo.AccessGrant.CircleGrants


        // All done
        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task UpdateAuthorizedCircles_ByAddingOne_AndRemovingExisting()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        #region Firstly, setup a chat app on Frodo's identity with a single circle and 2 drives (one for app, one random drive for circle)

        // Create a drive for the app
        var appDrive = await frodoOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Chat Drive 1", "", false);

        // Create a drive for the circle
        var circleDrive = await frodoOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Random Circle Drive", "", false);

        // Create the chat friends circle and give it read/write to the circle drive
        var chatFriendsCircle = await frodoOwnerClient.Membership.CreateCircle("Chat Friends Circle", new PermissionSetGrantRequest()
        {
            PermissionSet = new PermissionSet(),
            Drives = new[]
            {
                new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = circleDrive.TargetDriveInfo,
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
                        Drive = appDrive.TargetDriveInfo,
                        Permission = DrivePermission.All
                    }
                }
            },
            PermissionSet = new PermissionSet(PermissionKeys.All)
        };

        // the chat friends circle can work with the app with the permissions of circle member grant
        var authorizedCircles = new List<Guid>() { chatFriendsCircle.Id };

        // circle member grant (i.e. what circles can do ) on the app_drive. the chat friends circle can write to the chat drive
        var circleMemberGrant = new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = appDrive.TargetDriveInfo,
                        Permission = DrivePermission.Write
                    }
                }
            },
            PermissionSet = null
        };

        //
        // Create the app before we send a request
        //
        var appRegistration = await frodoOwnerClient.Apps.RegisterApp(appId, appPermissionsGrant, authorizedCircles, circleMemberGrant);

        #endregion

        // Send Sam connection request and grant him access to the chat friend's circle
        var circleIdsGrantedToRecipient = new List<GuidId>() { chatFriendsCircle.Id };
        await frodoOwnerClient.Network.SendConnectionRequestTo(TestIdentities.Samwise, circleIdsGrantedToRecipient);

        // Sam must accept the connection request to apply the permissions
        var circlesGrantedToSender = new List<GuidId>();
        await samOwnerClient.Network.AcceptConnectionRequest(frodoOwnerClient.Identity, circlesGrantedToSender);

        //
        // Testing
        //

        // Get Sam's connection info on Frodo's identity
        var samConnectionInfo = await frodoOwnerClient.Network.GetConnectionInfo(samOwnerClient.Identity);
        ClassicAssert.IsTrue(samConnectionInfo.Status == ConnectionStatus.Connected);

        var appGrants = samConnectionInfo.AccessGrant.AppGrants;
        var appKey = appRegistration.AppId.Value;
        ClassicAssert.IsTrue(appGrants.Count == 3, "there should be 3 app grants; mail, chat, and the app created in this test");

        ClassicAssert.IsTrue(appGrants.TryGetValue(appKey, out var chatAppCircleGrants), "The single dictionary item's key should match the single registered app");
        ClassicAssert.IsNotNull(chatAppCircleGrants, "chatAppCircleGrants != null");

        // ReSharper disable once PossibleMultipleEnumeration
        ClassicAssert.IsTrue(chatAppCircleGrants.Count() == 1, "There should be only one circle grant");
        // ReSharper disable once PossibleMultipleEnumeration
        var singleGrant = chatAppCircleGrants.First();
        ClassicAssert.IsTrue(singleGrant.AppId == appRegistration.AppId);
        ClassicAssert.IsTrue(singleGrant.CircleId == chatFriendsCircle.Id, "the circle id of the grant should match the chat friends circle");
        ClassicAssert.IsTrue(singleGrant.PermissionSet == appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet, "The circle should be granted the app's circle member grant");

        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = singleGrant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            ClassicAssert.IsNotNull(shouldBeOnlyOne, "there should be one and only one drive matching the ap's circle member granted drive");
        }


        //TODO: test circle grants:         samConnectionInfo.AccessGrant.CircleGrants

        //
        // Update authorized Circles
        //

        // Creat a new circle
        var someNewCircle = await frodoOwnerClient.Membership.CreateCircle("Another Circle", new PermissionSetGrantRequest()
        {
            PermissionSet = new PermissionSet(),
            Drives = new[]
            {
                new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = circleDrive.TargetDriveInfo,
                        Permission = DrivePermission.ReadWrite
                    }
                }
            }
        });

        //add Sam into the circle;
        await frodoOwnerClient.Network.GrantCircle(someNewCircle.Id, samOwnerClient.Identity);

        // Update the app, and only give it the new circle, but keep the same circle member grant
        var newAuthorizedCircles = new List<Guid>() { someNewCircle.Id };
        await frodoOwnerClient.Apps.UpdateAppAuthorizedCircles(appRegistration.AppId, newAuthorizedCircles, appRegistration.CircleMemberPermissionSetGrantRequest);

        // Test
        var updatedApp = await frodoOwnerClient.Apps.GetAppRegistration(appRegistration.AppId);
        ClassicAssert.IsNotNull(updatedApp, $"Could not retrieve the app {appId}");

        CollectionAssert.AreEquivalent(updatedApp.AuthorizedCircles, newAuthorizedCircles, "Updated authorized circles are incorrect");
        ClassicAssert.IsTrue(updatedApp.CircleMemberPermissionSetGrantRequest.PermissionSet == appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet,
            "updated app cirlce grant permission set did not match");
        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = updatedApp.CircleMemberPermissionSetGrantRequest.Drives.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            ClassicAssert.IsNotNull(shouldBeOnlyOne, "there should be one and only one drive matching the app's circle member granted drive");
        }

        // Test sam's identity to have new circle

        // Get Sam's connection info on Frodo's identity
        var updatedSamConnectionInfo = await frodoOwnerClient.Network.GetConnectionInfo(samOwnerClient.Identity);
        ClassicAssert.IsTrue(updatedSamConnectionInfo.Status == ConnectionStatus.Connected);

        var appGrants2 = updatedSamConnectionInfo.AccessGrant.AppGrants;
        ClassicAssert.IsTrue(appGrants2.Count == 3, "There should be 3 app grants because we added one and deleted one; plus the two built-in grants");
        ClassicAssert.IsTrue(appGrants2.TryGetValue(appKey, out var updatedChatAppCircleGrants), "The single dictionary item's key should match the single registered app");
        ClassicAssert.IsNotNull(updatedChatAppCircleGrants, "chatAppCircleGrants2 != null");

        // ReSharper disable once PossibleMultipleEnumeration
        ClassicAssert.IsTrue(updatedChatAppCircleGrants.Count() == 1, "There should be only one circle grant");
        // ReSharper disable once PossibleMultipleEnumeration
        var updatedGrant = updatedChatAppCircleGrants.First();
        ClassicAssert.IsTrue(updatedGrant.AppId == appRegistration.AppId);
        ClassicAssert.IsTrue(updatedGrant.CircleId == someNewCircle.Id, "the circle id of the grant should match the 'some new circle' circle");
        ClassicAssert.IsTrue(updatedGrant.PermissionSet == appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet, "The circle should be granted the app's circle member grant");

        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = updatedGrant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            ClassicAssert.IsNotNull(shouldBeOnlyOne, "there should be one and only one drive matching the ap's circle member granted drive");
        }

        // All done
        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task AcceptedConnectionRequest_GrantsAppCircle()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        #region Firstly, setup a chat app on Frodo's identity with a single circle and 2 drives (one for app, one random drive for circle)

        // Create a drive for the app
        var appDrive = await frodoOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Chat Drive 1", "", false);

        // Create a drive for the circle
        var circleDrive = await frodoOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Random Circle Drive", "", false);

        // Create the chat friends circle and give it read/write to the circle drive
        var chatFriendsCircle = await frodoOwnerClient.Membership.CreateCircle("Chat Friends Circle", new PermissionSetGrantRequest()
        {
            PermissionSet = new PermissionSet(),
            Drives = new[]
            {
                new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = circleDrive.TargetDriveInfo,
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
                        Drive = appDrive.TargetDriveInfo,
                        Permission = DrivePermission.All
                    }
                }
            },
            PermissionSet = new PermissionSet(PermissionKeys.All)
        };

        // the chat friends circle can work with the app with the permissions of circle member grant
        var authorizedCircles = new List<Guid>() { chatFriendsCircle.Id };

        // circle member grant (i.e. what circles can do ) on the app_drive. the chat friends circle can write to the chat drive
        var circleMemberGrant = new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = appDrive.TargetDriveInfo,
                        Permission = DrivePermission.Write
                    }
                }
            },
            PermissionSet = null
        };

        //
        // Create the app before we send a request
        //
        var appRegistration = await frodoOwnerClient.Apps.RegisterApp(appId, appPermissionsGrant, authorizedCircles, circleMemberGrant);

        #endregion

        // Sam will send Frodo connection request.  Sam has given no access but frodo will give access to the chat friend's scirlce
        var circleIdsGrantedToRecipient = new List<GuidId>() { };
        await samOwnerClient.Network.SendConnectionRequestTo(frodoOwnerClient.Identity, circleIdsGrantedToRecipient);

        // Frodo must accept the connection request.  this Should grant Sam access to the chat friend's circle
        var circlesGrantedToSender = new List<GuidId>() { chatFriendsCircle.Id };
        await frodoOwnerClient.Network.AcceptConnectionRequest(samOwnerClient.Identity, circlesGrantedToSender);

        //
        // Testing
        //

        // Get Sam's connection info on Frodo's identity
        var samConnectionInfo = await frodoOwnerClient.Network.GetConnectionInfo(samOwnerClient.Identity);
        ClassicAssert.IsTrue(samConnectionInfo.Status == ConnectionStatus.Connected);

        var appGrants = samConnectionInfo.AccessGrant.AppGrants;
        var appKey = appRegistration.AppId.Value;
        ClassicAssert.IsTrue(appGrants.Count == 3, "there should be 3 app grants; mail, chat, and the app created in this test");
        ClassicAssert.IsTrue(appGrants.TryGetValue(SystemAppConstants.MailAppId, out _));
        ClassicAssert.IsTrue(appGrants.TryGetValue(SystemAppConstants.ChatAppId, out _));
        ClassicAssert.IsTrue(appGrants.TryGetValue(appId, out _));
        ClassicAssert.IsTrue(appGrants.TryGetValue(appKey, out var chatAppCircleGrants), "The single dictionary item's key should match the single registered app");
        ClassicAssert.IsNotNull(chatAppCircleGrants, "chatAppCircleGrants != null");

        // ReSharper disable once PossibleMultipleEnumeration
        ClassicAssert.IsTrue(chatAppCircleGrants.Count() == 1, "There should be only one circle grant");
        // ReSharper disable once PossibleMultipleEnumeration
        var singleGrant = chatAppCircleGrants.First();
        ClassicAssert.IsTrue(singleGrant.AppId == appRegistration.AppId);
        ClassicAssert.IsTrue(singleGrant.CircleId == chatFriendsCircle.Id, "the circle id of the grant should match the chat friends circle");
        ClassicAssert.IsTrue(singleGrant.PermissionSet == appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet, "The circle should be granted the app's circle member grant");

        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = singleGrant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            ClassicAssert.IsNotNull(shouldBeOnlyOne, "there should be one and only one drive matching the ap's circle member granted drive");
        }


        //TODO: test circle grants:         samConnectionInfo.AccessGrant.CircleGrants

        //
        // Update authorized Circles
        //

        // Creat a new circle
        var someNewCircle = await frodoOwnerClient.Membership.CreateCircle("Another Circle", new PermissionSetGrantRequest()
        {
            PermissionSet = new PermissionSet(),
            Drives = new[]
            {
                new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = circleDrive.TargetDriveInfo,
                        Permission = DrivePermission.ReadWrite
                    }
                }
            }
        });

        //add Sam into the circle;
        await frodoOwnerClient.Network.GrantCircle(someNewCircle.Id, samOwnerClient.Identity);

        // Update the app, and only give it the new circle, but keep the same circle member grant
        var newAuthorizedCircles = new List<Guid>() { someNewCircle.Id };
        await frodoOwnerClient.Apps.UpdateAppAuthorizedCircles(appRegistration.AppId, newAuthorizedCircles, appRegistration.CircleMemberPermissionSetGrantRequest);

        // Test
        var updatedApp = await frodoOwnerClient.Apps.GetAppRegistration(appRegistration.AppId);
        ClassicAssert.IsNotNull(updatedApp, $"Could not retrieve the app {appId}");
        CollectionAssert.AreEquivalent(updatedApp.AuthorizedCircles, newAuthorizedCircles, "Updated authorized circles are incorrect");
        ClassicAssert.IsTrue(updatedApp.CircleMemberPermissionSetGrantRequest.PermissionSet == appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet,
            "updated app cirlce grant permission set did not match");
        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = updatedApp.CircleMemberPermissionSetGrantRequest.Drives.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            ClassicAssert.IsNotNull(shouldBeOnlyOne, "there should be one and only one drive matching the app's circle member granted drive");
        }

        // Test sam's identity to have new circle

        // Get Sam's connection info on Frodo's identity
        var updatedSamConnectionInfo = await frodoOwnerClient.Network.GetConnectionInfo(samOwnerClient.Identity);
        ClassicAssert.IsTrue(updatedSamConnectionInfo.Status == ConnectionStatus.Connected);

        var appGrants2 = updatedSamConnectionInfo.AccessGrant.AppGrants;
        ClassicAssert.IsTrue(appGrants2.Count == 3, "There should be 3 app grants because we added one and deleted one; and the two built-in grants (mail and chat)");
        ClassicAssert.IsTrue(appGrants2.TryGetValue(appKey, out var updatedChatAppCircleGrants), "The single dictionary item's key should match the single registered app");
        ClassicAssert.IsNotNull(updatedChatAppCircleGrants, "chatAppCircleGrants2 != null");

        // ReSharper disable once PossibleMultipleEnumeration
        ClassicAssert.IsTrue(updatedChatAppCircleGrants.Count() == 1, "There should be only one circle grant");
        // ReSharper disable once PossibleMultipleEnumeration
        var updatedGrant = updatedChatAppCircleGrants.First();
        ClassicAssert.IsTrue(updatedGrant.AppId == appRegistration.AppId);
        ClassicAssert.IsTrue(updatedGrant.CircleId == someNewCircle.Id, "the circle id of the grant should match the 'some new circle' circle");
        ClassicAssert.IsTrue(updatedGrant.PermissionSet == appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet, "The circle should be granted the app's circle member grant");

        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = updatedGrant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            ClassicAssert.IsNotNull(shouldBeOnlyOne, "there should be one and only one drive matching the ap's circle member granted drive");
        }

        // All done
        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task RevokeCircleFromAppAuthorizedCircles()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        #region Firstly, setup a chat app on Frodo's identity with two circles and 2 drives (one for app, one random drive for circle)

        // Create a drive for the app
        var appDrive = await frodoOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Chat Drive 1", "", false);

        // Create a drive for the circle
        var circleDrive = await frodoOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Random Circle Drive", "", false);

        // Create the chat friends circle and give it read/write to the circle drive
        var chatFriendsCircle = await frodoOwnerClient.Membership.CreateCircle("Chat Friends Circle", new PermissionSetGrantRequest()
        {
            PermissionSet = new PermissionSet(),
            Drives = new[]
            {
                new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = circleDrive.TargetDriveInfo,
                        Permission = DrivePermission.ReadWrite
                    }
                }
            }
        });

        var documentShareCircle = await frodoOwnerClient.Membership.CreateCircle("Circle for document sharing", new PermissionSetGrantRequest()
        {
            PermissionSet = new PermissionSet(),
            Drives = new[]
            {
                new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = circleDrive.TargetDriveInfo,
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
                        Drive = appDrive.TargetDriveInfo,
                        Permission = DrivePermission.All
                    }
                }
            },
            PermissionSet = new PermissionSet(PermissionKeys.All)
        };

        // the chat friends circle can work with the app with the permissions of circle member grant
        var authorizedCircles = new List<Guid>() { chatFriendsCircle.Id, documentShareCircle.Id };

        // circle member grant (i.e. what circles can do ) on the app_drive. the chat friends circle can write to the chat drive
        var circleMemberGrant = new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = appDrive.TargetDriveInfo,
                        Permission = DrivePermission.Write
                    }
                }
            },
            PermissionSet = null
        };

        //
        // Create the app before we send a request
        //
        var appRegistration = await frodoOwnerClient.Apps.RegisterApp(appId, appPermissionsGrant, authorizedCircles, circleMemberGrant);

        #endregion

        // Frodo will send sam connection request with access to the two circles
        var circleIdsGrantedToRecipient = new List<GuidId>() { chatFriendsCircle.Id, documentShareCircle.Id };
        await frodoOwnerClient.Network.SendConnectionRequestTo(samOwnerClient.Identity, circleIdsGrantedToRecipient);

        // Frodo must accept the connection request.  this Should grant Sam access to the chat friend's circle
        var circlesGrantedToSender = new List<GuidId>() { };
        await samOwnerClient.Network.AcceptConnectionRequest(frodoOwnerClient.Identity, circlesGrantedToSender);

        //
        // Testing
        //

        // Get Sam's connection info on Frodo's identity
        var samConnectionInfo = await frodoOwnerClient.Network.GetConnectionInfo(samOwnerClient.Identity);
        ClassicAssert.IsTrue(samConnectionInfo.Status == ConnectionStatus.Connected);

        var appGrants = samConnectionInfo.AccessGrant.AppGrants;
        var appKey = appRegistration.AppId.Value;
        ClassicAssert.IsTrue(appGrants.Count == 3, "there should be 3 app grants; mail, chat, and the app created in this test");

        ClassicAssert.IsTrue(appGrants.TryGetValue(appKey, out var chatAppCircleGrantList), "The single dictionary item's key should match the single registered app");
        ClassicAssert.IsNotNull(chatAppCircleGrantList, "chatAppCircleGrants != null");
        var chatAppCircleGrants = chatAppCircleGrantList.ToList();

        ClassicAssert.IsTrue(chatAppCircleGrants.Count() == 2, "There should be two circle grant (chat friends and document share)");
        //Test chat friend's circle
        var chatFriendsCircleGrant = chatAppCircleGrants.Single(c => c.CircleId == chatFriendsCircle.Id);
        ClassicAssert.IsTrue(chatFriendsCircleGrant.AppId == appRegistration.AppId);
        ClassicAssert.IsTrue(chatFriendsCircleGrant.CircleId == chatFriendsCircle.Id, "the circle id of the grant should match the chat friends circle");
        ClassicAssert.IsTrue(chatFriendsCircleGrant.PermissionSet == appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet, "The circle should be granted the app's circle member grant");

        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = chatFriendsCircleGrant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            ClassicAssert.IsNotNull(shouldBeOnlyOne, "there should be one and only one drive matching the ap's circle member granted drive");
        }


        //Test document sharing circle
        var documentSharingCircle = chatAppCircleGrants.Single(c => c.CircleId == documentShareCircle.Id);
        ClassicAssert.IsTrue(documentSharingCircle.AppId == appRegistration.AppId);
        ClassicAssert.IsTrue(documentSharingCircle.CircleId == documentShareCircle.Id, "the circle id of the grant should match the chat friends circle");
        ClassicAssert.IsTrue(documentSharingCircle.PermissionSet == appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet, "The circle should be granted the app's circle member grant");

        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = documentSharingCircle.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            ClassicAssert.IsNotNull(shouldBeOnlyOne, "there should be one and only one drive matching the ap's circle member granted drive");
        }

        //
        // Revoke sam from the chat friend's circle
        //

        await frodoOwnerClient.Network.RevokeCircle(chatFriendsCircle.Id, samOwnerClient.Identity);

        //
        //
        //


        // Test sam's identity to have not have the circle

        // Get Sam's connection info on Frodo's identity
        var updatedSamConnectionInfo = await frodoOwnerClient.Network.GetConnectionInfo(samOwnerClient.Identity);
        ClassicAssert.IsTrue(updatedSamConnectionInfo.Status == ConnectionStatus.Connected);

        var updatedAppGrants = updatedSamConnectionInfo.AccessGrant.AppGrants;
        ClassicAssert.IsTrue(updatedAppGrants.Count == 3, "There should still only be 3 app grants");
        ClassicAssert.IsTrue(updatedAppGrants.TryGetValue(appKey, out var updatedChatAppCircleGrantList), "The single dictionary item's key should match the single registered app");
        ClassicAssert.IsNotNull(updatedChatAppCircleGrantList, "chatAppCircleGrants2 != null");
        var updatedChatAppCircleGrants = updatedChatAppCircleGrantList.ToList();

        ClassicAssert.IsTrue(updatedChatAppCircleGrants.Count == 1, "There should be one circle grant");
        var updatedGrant = updatedChatAppCircleGrants.First();
        ClassicAssert.IsTrue(updatedGrant.AppId == appRegistration.AppId);
        ClassicAssert.IsTrue(updatedGrant.CircleId == documentShareCircle.Id, "the circle id should be the documentSharing circle");
        ClassicAssert.IsTrue(updatedGrant.PermissionSet == appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet, "The circle should be granted the app's circle member grant");

        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = updatedGrant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            ClassicAssert.IsNotNull(shouldBeOnlyOne, "there should be one and only one drive matching the ap's circle member granted drive");
        }

        // All done
        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);
    }
    
}