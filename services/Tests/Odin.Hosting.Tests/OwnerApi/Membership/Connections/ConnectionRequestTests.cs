using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;

namespace Odin.Hosting.Tests.OwnerApi.Membership.Connections;

public class ConnectionRequestTests
{
    private WebScaffold _scaffold;

    /*
     
        Which actions can be taken in each of these states?
            - send connection request
            - receive connection request
            - accept connection request
            - delete connection request
        And it varies by - what Can merry do and what can pippin do?
          
        Merry: Connected, Pippin: Connected
        Merry: Connected, Pippin: Outgoing  -> must be able to delete a connection w/o notifying the remote server
        Merry: Connected, Pippin: Incoming  -> must be able to delete a connection w/o notifying the remote server
        Merry: Connected, Pippin: None      -> must be able to delete a connection w/o notifying the remote server          
        Merry: Outgoing, Pippin: Connected  -> must be able to delete a connection w/o notifying the remote server
        Merry: Outgoing, Pippin: Outgoing   -> must be able to delete a connection w/o notifying the remote server
        Merry: Outgoing, Pippin: Incoming   -> must be able to delete a connection w/o notifying the remote server 
        Merry: Outgoing, Pippin: None       -> ✓
        Merry: Incoming, Pippin: Connected  -> must be able to delete a connection w/o notifying the remote server
        Merry: Incoming, Pippin: Outgoing   -> ✓
        Merry: Incoming, Pippin: Incoming   -> ✓
        Merry: Incoming, Pippin: None       -> ✓
        Merry: None, Pippin: Connected      -> must be able to delete a connection w/o notifying the remote server
        Merry: None, Pippin: Outgoing       -> ✓
        Merry: None, Pippin: Incoming       -> ✓
        Merry: None, Pippin: None           -> ✓
        
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
    public async Task CanReceiveAndAcceptConnectionRequestWhenConnectionAlreadyExistsFromSender()
    {
        /*
         Frodo
         Frodo already has an ICR record for sam but Sam does not have a record for Frodo
         */
    }
    
    [Test]
    public async Task CanReceiveConnectionRequest_EvenWhenRecipientSendsConnectionRequest()
    {
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
        Sam already has an incoming request from Frodo
               Result: replace existing with new request
        */
    }

    [Test]
    public async Task WhenConnectionIsSevered_BothPartiesHaveICRDeleted()
    {
        Assert.Fail("TODO");
    }
    
    
    [Test]
    public async Task WillEstablishConnection_EvenWhenAlreadyConnected()
    {
        /*
         Sam is already connected to Frodo and Frodo's ICR is valid or invalid
           Action: delete old ICR, save connection request
         */

        //
        // Merry sends connection request to Pippin
        // Pippin Accepts; they are connected
        // 
        // Pippin sends connection request to Merry
        // Merry Accepts; they are connected
        // 
        // 
        // 
        // 
        // 


        var sender = TestIdentities.Frodo;
        var recipient = TestIdentities.Samwise;

        var senderOwnerClient = _scaffold.CreateOwnerApiClient(sender);
        var recipientOwnerClient = _scaffold.CreateOwnerApiClient(recipient);

        var senderChatDrive = await senderOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Chat drive",
            metadata: "",
            allowAnonymousReads: false,
            allowSubscriptions: false,
            ownerOnly: false);

        var expectedPermissionedDrive = new PermissionedDrive()
        {
            Drive = senderChatDrive.TargetDriveInfo,
            Permission = DrivePermission.Read | DrivePermission.Write | DrivePermission.WriteReactionsAndComments
        };

        var senderChatCircle = await senderOwnerClient.Membership.CreateCircle("Chat Participants", new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new()
                {
                    PermissionedDrive = expectedPermissionedDrive
                }
            }
        });

        await senderOwnerClient.Network.SendConnectionRequest(recipient, new List<GuidId>() { senderChatCircle.Id });
        await recipientOwnerClient.Network.AcceptConnectionRequest(sender, new List<GuidId>());

        // Test
        // At this point: recipient should have an ICR record on sender's identity that does not have a key
        // 

        var recipientConnectionInfo = await senderOwnerClient.Network.GetConnectionInfo(recipient);

        //find the drive grant 
        var actualCircleGrant = recipientConnectionInfo.AccessGrant.CircleGrants.SingleOrDefault(cg =>
            cg.DriveGrants.Any(dg => dg.PermissionedDrive == expectedPermissionedDrive));
        Assert.IsNotNull(actualCircleGrant, "actualPermissionedDrive != null");
        Assert.IsTrue(actualCircleGrant.DriveGrants.Count == 1, "There should only be drive grant from the single circle we created");
        Assert.IsTrue(actualCircleGrant.DriveGrants.Single().HasStorageKey, "the drive granted should have storage key");

        await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipient.OdinId);
    }
}