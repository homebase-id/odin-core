using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Membership.Connections;

namespace Odin.Hosting.Tests._Universal.Owner.Connections;

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
    public async Task MultipleThreadsAcceptingConnectionRequest_OneFails_OneSucceeds()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        #region Firstly, setup a chat app on Frodo's identity with a single circle and 2 drives (one for app, one random drive for circle)

        // Create a drive for the app
        var appDrive = await frodoOwnerClient.DriveManager.CreateDrive(TargetDrive.NewTargetDrive(), "Chat Drive 1", "", false);

        // Create a drive for the circle
        var circleDrive = TargetDrive.NewTargetDrive();
        var circleId = Guid.NewGuid();
        await frodoOwnerClient.DriveManager.CreateDrive(circleDrive, "Random Circle Drive", "", false);
        await frodoOwnerClient.Network.CreateCircle(circleId, "Chat Friends Circle", new PermissionSetGrantRequest()
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

        #endregion

        // Sam will send Frodo connection request.  Sam has given no access but frodo will give access to the chat friend's scirlce
        var circleIdsGrantedToRecipient = new List<GuidId>() { };
        await samOwnerClient.Connections.SendConnectionRequest(frodoOwnerClient.Identity.OdinId, circleIdsGrantedToRecipient);

        // Frodo must accept the connection request.  this Should grant Sam access to the chat friend's circle
        var circlesGrantedToSender = new List<GuidId>() { circleId };
        //ignoring await so we send both at the same time
        var response1 = await frodoOwnerClient.Connections.AcceptConnectionRequest(samOwnerClient.Identity.OdinId, circlesGrantedToSender);
        var response2 = await frodoOwnerClient.Connections.AcceptConnectionRequest(samOwnerClient.Identity.OdinId, circlesGrantedToSender);
        var response3 = await frodoOwnerClient.Connections.AcceptConnectionRequest(samOwnerClient.Identity.OdinId, circlesGrantedToSender);
        
        // All done
        await frodoOwnerClient.Connections.DisconnectFrom(samOwnerClient.Identity.OdinId);
        await samOwnerClient.Connections.DisconnectFrom(frodoOwnerClient.Identity.OdinId);
    }
}