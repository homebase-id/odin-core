using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.OwnerToken.Circles;

namespace Youverse.Hosting.Tests.OwnerApi.Circle;

public class CircleNetworkServiceAppTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [Test]
    public async Task GrantCircleWith_New_AppAuthorizedCircles()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApi(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApi(TestIdentities.Samwise);

        #region Firstly, setup a chat app on Frodo's identity with a single circle and 2 drives (one for app, one random drive for circle)

        // Create a drive for the app
        var appDrive = await frodoOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Chat Drive 1", "", false);

        // Create a drive for the circle
        var circleDrive = await frodoOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Random Circle Drive", "", false);

        // Create the chat friends circle and give it read/write to the circle drive
        var chatFriendsCircle = await frodoOwnerClient.Network.CreateCircle("Chat Friends Circle", new PermissionSetGrantRequest()
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
        var circleIdsGrantedToRecipient = new List<GuidId>() { chatFriendsCircle.Id };
        await frodoOwnerClient.Network.SendConnectionRequest(TestIdentities.Samwise, circleIdsGrantedToRecipient);

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

        var appRegistration = await frodoOwnerClient.Apps.RegisterApp(appId, appPermissionsGrant, authorizedCircles, circleMemberGrant);
        // var (clientAuthToken, sharedSecret) = await frodoOwnerApi.RegisterAppClient(appRegistration.AppId);

        #endregion

        //
        // Testing
        //

        // Get Sam's connection info on Frodo's identity
        var samConnectionInfo = await frodoOwnerClient.Network.GetConnectionInfo(samOwnerClient.Identity);
        Assert.IsTrue(samConnectionInfo.Status == ConnectionStatus.Connected);

        var appGrants = samConnectionInfo.AccessGrant.AppGrants;
        var appKey = appRegistration.AppId.Value.ToString();
        Assert.IsTrue(appGrants.Count == 1, "There should be one app grant");
        Assert.IsTrue(appGrants.TryGetValue(appKey, out var chatAppCircleGrants), "The single dictionary item's key should match the single registered app");
        Assert.IsNotNull(chatAppCircleGrants, "chatAppCircleGrants != null");

        // ReSharper disable once PossibleMultipleEnumeration
        Assert.IsTrue(chatAppCircleGrants.Count() == 1, "There should be only one circle grant");
        // ReSharper disable once PossibleMultipleEnumeration
        var singleGrant = chatAppCircleGrants.First();
        Assert.IsTrue(singleGrant.AppId == appRegistration.AppId);
        Assert.IsTrue(singleGrant.CircleId == chatFriendsCircle.Id, "the circle id of the grant should match the chat friends circle");
        Assert.IsTrue(singleGrant.PermissionSet == appRegistration.CircleMemberPermissionSetGrantRequest.PermissionSet, "The circle should be granted the app's circle member grant");

        foreach (var d in appRegistration.CircleMemberPermissionSetGrantRequest.Drives)
        {
            var shouldBeOnlyOne = singleGrant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive == d.PermissionedDrive);
            Assert.IsNotNull(shouldBeOnlyOne, "there should be one and only one drive matching the ap's circle member granted drive");
        }

        // All done
        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);
    }

    private void AssertAllDrivesGrantedFromCircle(CircleDefinition circleDefinition, RedactedCircleGrant actual)
    {
        foreach (var circleDriveGrant in circleDefinition.DriveGrants)
        {
            //be sure it's in the list of granted drives; use Single to be sure it's only in there once
            var result = actual.DriveGrants.SingleOrDefault(x =>
                x.PermissionedDrive.Drive == circleDriveGrant.PermissionedDrive.Drive && x.PermissionedDrive.Permission == circleDriveGrant.PermissionedDrive.Permission);
            Assert.NotNull(result);
        }
    }

    private async Task AssertIdentityIsInCircle(HttpClient client, SensitiveByteArray ownerSharedSecret, GuidId circleId, DotYouIdentity expectedIdentity)
    {
        var circleMemberSvc = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
        var getCircleMemberResponse = await circleMemberSvc.GetCircleMembers(new GetCircleMembersRequest() { CircleId = circleId });
        Assert.IsTrue(getCircleMemberResponse.IsSuccessStatusCode, $"Actual status code {getCircleMemberResponse.StatusCode}");
        var members = getCircleMemberResponse.Content;
        Assert.NotNull(members);
        Assert.IsTrue(members.Any());
        Assert.IsFalse(members.SingleOrDefault(m => m == expectedIdentity).Id == null);
    }
}