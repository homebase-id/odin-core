using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using NUnit.Framework;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Membership.Connections;

namespace Odin.Hosting.Tests.OwnerApi.Membership.Connections;

public class ConnectionRequestTests
{
    private WebScaffold _scaffold;
    private readonly PermissionSetGrantRequest _senderPermissions;
    private readonly PermissionSetGrantRequest _recipientPermissions;

    public ConnectionRequestTests()
    {
        _senderPermissions = new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = TargetDrive.NewTargetDrive(),
                        Permission = DrivePermission.Read | DrivePermission.Write | DrivePermission.WriteReactionsAndComments
                    }
                }
            }
        };

        _recipientPermissions = new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = TargetDrive.NewTargetDrive(),
                        Permission = DrivePermission.Read | DrivePermission.Write | DrivePermission.WriteReactionsAndComments
                    }
                }
            }
        };
    }

    /*
        The most direct method - regardless of the state of the identity ICR
            - you can send a connection request; it deletes the old
            - you can accept an connection request; it overwrites the ICR and deletes any outgoing requests you have
            Exceptions:

        Which actions can be taken in each of these states?
            - send connection request
            - receive connection request
            - accept connection request
            - delete connection request

        And it varies by - what Can merry do and what can pippin do?


            */

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
    [Description("Merry: None, Pippin: None")]
    public async Task CanConnectWhenState_Merry_NotConnected_Pippin_NotConnected()
    {
        var sender = TestIdentities.Merry;
        var recipient = TestIdentities.Pippin;

        await Connect(sender, recipient);
        await AssertConnected(sender, recipient);
    }

    [Test]
    [Description("Merry: Connected, Pippin: Connected")]
    public async Task CanConnectWhenState_Merry_Connected_Pippin_Connected()
    {
        var sender = TestIdentities.Merry;
        var recipient = TestIdentities.Pippin;

        await Connect(sender, recipient);
        await AssertConnected(sender, recipient);

        //
        // try to connect a second time
        //

        await Connect(sender, recipient);
        await AssertConnected(sender, recipient);
    }

    [Test]
    [Description("Merry: Connected, Pippin: None")]
    public async Task CanConnectWhenState_Merry_Connected_Pippin_NotConnected()
    {
        var sender = TestIdentities.Merry;
        var recipient = TestIdentities.Pippin;

        await Connect(sender, recipient);
        await AssertConnected(sender, recipient);

        // Pippin should disconnect (note: this relies on not telling the remote server you are disconnecting)
        var pippinClient = _scaffold.CreateOwnerApiClient(recipient);
        await pippinClient.Network.DisconnectFrom(sender);

        await Connect(sender, recipient);
        await AssertConnected(sender, recipient);
    }


    [Test]
    [Description("Merry: Connected, Pippin: Outgoing")]
    public async Task CanConnectWhenState_Merry_Connected_Pippin_Outgoing()
    {
    }

    [Test]
    [Description("Merry: Connected, Pippin: Incoming")]
    public async Task CanConnectWhenState_Merry__Connected_Pippin_Incoming()
    {
    }

    [Test]
    [Description("Merry: Outgoing, Pippin: Connected")]
    public async Task CanConnectWhenState_Merry_Outgoing_Pippin_Connected()
    {
    }

    [Test]
    [Description("Merry: Outgoing, Pippin: Outgoing")]
    public async Task CanConnectWhenState_Merry_Outgoing_Pippin_Outgoing()
    {
    }

    [Test]
    [Description("Merry: Outgoing, Pippin: Incoming")]
    public async Task CanConnectWhenState_Merry_Outgoing_Pippin_Incoming()
    {
    }

    [Test]
    [Description("Merry: Outgoing, Pippin: None")]
    public async Task CanConnectWhenState_Merry_Outgoing_Pippin_None()
    {
    }

    [Test]
    [Description("Merry: Incoming, Pippin: Connected")]
    public async Task CanConnectWhenState_Merry_Incoming_Pippin_Connected()
    {
    }

    [Test]
    [Description("Merry: Incoming, Pippin: Outgoing")]
    public async Task CanConnectWhenState_Merry_Incoming_Pippin_Outgoing()
    {
    }

    [Test]
    [Description("Merry: Incoming, Pippin: Incoming")]
    public async Task CanConnectWhenState_Merry_Incoming_Pippin_Incoming()
    {
    }

    [Test]
    [Description("Merry: Incoming, Pippin: None")]
    public async Task CanConnectWhenState_Merry_Incoming_Pippin_None()
    {
    }

    [Test]
    [Description("Merry: None, Pippin: Connected")]
    public async Task CanConnectWhenState_Merry_None_Pippin_Connected()
    {
    }

    [Test]
    [Description("Merry: None, Pippin: Outgoing")]
    public async Task CanConnectWhenState_Merry_None_Pippin_Outgoing()
    {
    }

    [Test]
    [Description("Merry: None, Pippin: Incoming")]
    public async Task CanConnectWhenState_Merry_None_Pippin_Incoming()
    {
    }


    [Test]
    [Description("If the outgoing connection request is deleted before attempting establish a connection; the accept connection request will fail")]
    public async Task FailToAcceptConnectionRequest_when_SendersOutgoingRequestWasDeleted()
    {
        /*
         * 1. merry sends connection request to frodo
         * 2. merry deletes outgoing request to frodo
         * 3. Frodo accepts merry's connection request
         * 4. Frodo receives error, system deletes merry's connection request, frodo is told to resend it
         */
    }

    [Test]
    public async Task CanReceive_And_AcceptConnectionRequestWhenRecipientAlreadyConnectedToSender()
    {
        // Merry: Connected, Pippin: Connected
        // Action: Merry sends a connection request
        /*
         * 1. Merry sends connection request to Pippin
         * 2. Pippin accepts; they are connected
         * 3. Merry sends a second connection request to Pippin
         * 4. Pippin accepts; they are connected (note the existing connections are overwritten with the new one; this is a good way to rotate the ICR KEY
         */
    }

    [Test]
    public async Task Reject_ConnectionRequest_when_SenderIsBlocked()
    {
        Assert.Fail("TODO");
    }

    [Test]
    public async Task CanReceiveConnectionRequest_EvenWhenRecipientHasOutgoingConnectionRequest()
    {
        /*
         * 1. Pippin sends connection request to Merry
         *  - Merry has an incoming connection request from Pippin
         *  - Pippin has an outgoing connection request from Merry
         * 2. Merry sends connection request to Pippin;
         *   - Pippin has an incoming connection request from Merry
         *   - Merry has an outgoing connection request from Pippin
         */

        /*
         Sam has an outgoing request to Frodo
           Result: just receive the incoming request
           if sam accepts first, delete both requests
           if Frodo accepts first, delete both requests in the Establish connection process
         */
    }

    [Test]
    public async Task CanReceiveMultipleConnectionRequestsFromSameSender()
    {
        /*
         *
         *
         */
        /*
        Sam already has an incoming request from Frodo
               Result: replace existing with new request
        */
    }

    [Test]
    public async Task WhenConnectionIsSevered_BothPartiesHaveICRDeleted()
    {
        Assert.Fail("TODO - should we support this?");
    }

    private async Task Connect(TestIdentity sender, TestIdentity recipient)
    {
        //Note
        var senderOwnerClient = _scaffold.CreateOwnerApiClient(sender);
        var recipientOwnerClient = _scaffold.CreateOwnerApiClient(recipient);

        await senderOwnerClient.Network.SendConnectionRequest(recipient, new List<GuidId>() { });
        await recipientOwnerClient.Network.AcceptConnectionRequest(sender, new List<GuidId>() { });
    }

    private async Task AssertConnected(TestIdentity sender, TestIdentity recipient)
    {
        var senderOwnerClient = _scaffold.CreateOwnerApiClient(sender);
        var recipientOwnerClient = _scaffold.CreateOwnerApiClient(recipient);

        //
        // Test Sender's record on recipient server
        //

        var senderConnectionInfoOnRecipientIdentity = await recipientOwnerClient.Network.GetConnectionInfo(sender);
        Assert.IsTrue(senderConnectionInfoOnRecipientIdentity.Status == ConnectionStatus.Connected);

        //
        // Test recipient's record on sender server
        //
        var recipientConnectionInfo = await senderOwnerClient.Network.GetConnectionInfo(recipient);
        Assert.IsTrue(recipientConnectionInfo.Status == ConnectionStatus.Connected);
    }

    private async Task Disconnect(TestIdentity sender, TestIdentity recipient)
    {
        await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipient.OdinId);
    }
}