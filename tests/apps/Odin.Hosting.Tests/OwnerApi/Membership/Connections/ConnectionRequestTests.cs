using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Services.Membership.Connections;
using Odin.Hosting.Controllers;

namespace Odin.Hosting.Tests.OwnerApi.Membership.Connections;

public class ConnectionRequestTests
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
    [Description("Merry: None, Pippin: None")]
    public async Task CanConnectWhenState_Merry_NotConnected_Pippin_NotConnected()
    {
        var sender = TestIdentities.Merry;
        var recipient = TestIdentities.Pippin;

        await Connect(sender, recipient);
        await AssertConnected(sender, recipient);
        await Cleanup(sender, recipient);
    }

    [Test]
    [Description("Merry: Connected, Pippin: Connected")]
    [Ignore("Invalid due to invitations feature")]
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
        await Cleanup(sender, recipient);
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
        await Cleanup(sender, recipient);
    }


    [Test]
    [Description("Merry: Connected, Pippin: Outgoing")]
    public async Task CanConnectWhenState_Merry_Connected_Pippin_Outgoing()
    {
        var merry = TestIdentities.Merry;
        var pippin = TestIdentities.Pippin;

        await Connect(merry, pippin);
        await AssertConnected(merry, pippin);

        // Pippin should disconnect (note: this relies on not telling the remote server you are disconnecting)
        var pippinClient = _scaffold.CreateOwnerApiClient(pippin);
        await pippinClient.Network.DisconnectFrom(merry);
        await pippinClient.Network.SendConnectionRequestTo(merry);

        // Merry deletes the incoming request
        var merryClient = _scaffold.CreateOwnerApiClient(merry);
        await merryClient.Network.DeleteConnectionRequestFrom(pippin);

        // They try to reconnect again fully
        await Connect(merry, pippin);
        await AssertConnected(merry, pippin);
        await Cleanup(merry, pippin);
    }

    [Test]
    [Ignore("Invalid due to invitations feature; we now accept the incoming request if you're sending one to the same recipient")]
    public async Task CanStoreSentRequestAndOutgoingRequestForSameIdentity()
    {
        // Scenario: merry sends request to pippin, therefore pippin has an incoming request from merry
        // pippin sends a request to merry, therefore pippin has an outgoing request to merry
        var merry = TestIdentities.Merry;
        var pippin = TestIdentities.Pippin;

        var pippinClient = _scaffold.CreateOwnerApiClient(pippin);
        var merryClient = _scaffold.CreateOwnerApiClient(merry);

        await merryClient.Network.SendConnectionRequestTo(pippin);

        var pendingRequestFromMerry = await pippinClient.Network.GetIncomingRequestFrom(merry);
        Assert.IsNotNull(pendingRequestFromMerry);
        Assert.IsTrue(pendingRequestFromMerry.SenderOdinId == merryClient.Identity.OdinId);
        Assert.IsTrue(pendingRequestFromMerry.Direction == ConnectionRequestDirection.Incoming);


        //Now that Pippin has an incoming request
        await pippinClient.Network.SendConnectionRequestTo(merry);

        // Assert that we still have an outgoing request to merry and an incoming request from merry; two different requests
        var sentRequestToMerry = await pippinClient.Network.GetOutgoingSentRequestTo(merry);
        Assert.IsNotNull(sentRequestToMerry);
        Assert.IsTrue(sentRequestToMerry.Recipient == merryClient.Identity.OdinId);
        Assert.IsTrue(sentRequestToMerry.Direction == ConnectionRequestDirection.Outgoing);

        var pendingRequestFromMerry2 = await pippinClient.Network.GetIncomingRequestFrom(merry);
        Assert.IsNotNull(pendingRequestFromMerry2);
        Assert.IsTrue(pendingRequestFromMerry2.SenderOdinId == merryClient.Identity.OdinId);
        Assert.IsTrue(pendingRequestFromMerry2.Direction == ConnectionRequestDirection.Incoming);

        // They try to reconnect again fully
        await Connect(merry, pippin);
        await AssertConnected(merry, pippin);
        await Cleanup(merry, pippin);
    }

    [Test]
    [Description("Merry: Connected, Pippin: Incoming")]
    public async Task CanConnectWhenState_Merry__Connected_Pippin_Incoming()
    {
        var merry = TestIdentities.Merry;
        var pippin = TestIdentities.Pippin;

        
        await Connect(merry, pippin);
        await AssertConnected(merry, pippin);

        // Pippin should disconnect (note: this relies on not telling the remote server you are disconnecting)
        var pippinClient = _scaffold.CreateOwnerApiClient(pippin);
        await pippinClient.Network.DisconnectFrom(merry);

        var merryClient = _scaffold.CreateOwnerApiClient(merry);
        await merryClient.Network.SendConnectionRequestTo(pippin);
        await merryClient.Network.DeleteSentRequestTo(pippin);

        //Now that Pippin has an incoming request

        // They try to reconnect again fully
        await Connect(merry, pippin);
        await AssertConnected(merry, pippin);
        await Cleanup(merry, pippin);
    }

    [Test]
    [Description("Merry: Outgoing, Pippin: Connected")]
    public async Task CanConnectWhenState_Merry_Outgoing_Pippin_Connected()
    {
        var merry = TestIdentities.Merry;
        var pippin = TestIdentities.Pippin;

        var merryClient = _scaffold.CreateOwnerApiClient(merry);
        var pippinClient = _scaffold.CreateOwnerApiClient(pippin);

        //

        await Connect(merry, pippin);
        await AssertConnected(merry, pippin);

        await merryClient.Network.DisconnectFrom(pippin);
        await merryClient.Network.SendConnectionRequestTo(pippin);

        await pippinClient.Network.DeleteConnectionRequestFrom(merry);

        //
        // Assert state is ready for test
        //

        var merryInfo = await pippinClient.Network.GetConnectionInfo(merry);
        Assert.IsTrue(merryInfo.Status == ConnectionStatus.Connected);

        var pippinInfo = await merryClient.Network.GetOutgoingSentRequestTo(pippin);
        Assert.IsNotNull(pippinInfo);

        //
        // They try to reconnect again fully
        //
        await Connect(merry, pippin);
        await AssertConnected(merry, pippin);
        await Cleanup(merry, pippin);
    }

    [Test]
    [Description("Merry: Outgoing, Pippin: Outgoing")]
    [Ignore("Invalid due to invitations feature; we now auto-approve requests when sending a request to a recipient who already sent you a request")]
    public async Task CanConnectWhenState_Merry_Outgoing_Pippin_Outgoing()
    {
        var merry = TestIdentities.Merry;
        var pippin = TestIdentities.Pippin;

        var merryClient = _scaffold.CreateOwnerApiClient(merry);
        var pippinClient = _scaffold.CreateOwnerApiClient(pippin);

        await merryClient.Network.SendConnectionRequestTo(pippin);
        await pippinClient.Network.SendConnectionRequestTo(merry);

        await merryClient.Network.DeleteConnectionRequestFrom(pippin);
        await pippinClient.Network.DeleteConnectionRequestFrom(merry);

        //
        // Assert state is ready for test
        //

        var merryInfo = await pippinClient.Network.GetOutgoingSentRequestTo(merry);
        Assert.IsNotNull(merryInfo);

        var pippinInfo = await merryClient.Network.GetOutgoingSentRequestTo(pippin);
        Assert.IsNotNull(pippinInfo);

        //
        // They try to reconnect again fully
        //
        await Connect(merry, pippin);
        await AssertConnected(merry, pippin);
        await Cleanup(merry, pippin);
    }

    [Test]
    [Description("Merry: Outgoing, Pippin: Incoming")]
    public async Task CanConnectWhenState_Merry_Outgoing_Pippin_Incoming()
    {
        var merry = TestIdentities.Merry;
        var pippin = TestIdentities.Pippin;

        var merryClient = _scaffold.CreateOwnerApiClient(merry);
        var pippinClient = _scaffold.CreateOwnerApiClient(pippin);

        await merryClient.Network.SendConnectionRequestTo(pippin);

        //
        // Assert state is ready for test
        //

        var pippinInfo = await merryClient.Network.GetOutgoingSentRequestTo(pippin);
        Assert.IsNotNull(pippinInfo);

        var merryInfo = await pippinClient.Network.GetIncomingRequestFrom(merry);
        Assert.IsNotNull(merryInfo);

        //
        // They try to reconnect again fully
        //
        await Connect(merry, pippin);
        await AssertConnected(merry, pippin);
        await Cleanup(merry, pippin);
    }

    [Test]
    [Description("Merry: Outgoing, Pippin: None")]
    public async Task CanConnectWhenState_Merry_Outgoing_Pippin_None()
    {
        var merry = TestIdentities.Merry;
        var pippin = TestIdentities.Pippin;

        var merryClient = _scaffold.CreateOwnerApiClient(merry);
        var pippinClient = _scaffold.CreateOwnerApiClient(pippin);

        await merryClient.Network.SendConnectionRequestTo(pippin);

        await pippinClient.Network.DeleteConnectionRequestFrom(merry);

        //
        // Assert state is ready for test
        //

        var pippinInfo = await merryClient.Network.GetOutgoingSentRequestTo(pippin);
        Assert.IsNotNull(pippinInfo);

        var merryInfo = await pippinClient.Network.GetIncomingRequestFrom(merry);
        Assert.IsNull(merryInfo);

        //
        // They try to reconnect again fully
        //
        await Connect(merry, pippin);
        await AssertConnected(merry, pippin);
        await Cleanup(merry, pippin);
    }

    [Test]
    [Description("Merry: Incoming, Pippin: Connected")]
    public async Task Will_AutoAccept_WhenState_Merry_Incoming_Pippin_Connected()
    {
        var merry = TestIdentities.Merry;
        var pippin = TestIdentities.Pippin;

        var merryClient = _scaffold.CreateOwnerApiClient(merry);
        var pippinClient = _scaffold.CreateOwnerApiClient(pippin);

        await Connect(merry, pippin);
        await AssertConnected(merry, pippin);

        await merryClient.Network.DisconnectFrom(pippin);
        await pippinClient.Network.SendConnectionRequestTo(merry);

        //
        // Assert state is ready for test
        //

        var pippinInfo = await merryClient.Network.GetIncomingRequestFrom(pippin);
        Assert.IsNotNull(pippinInfo);

        var merryInfo = await pippinClient.Network.GetConnectionInfo(merry);
        Assert.IsTrue(merryInfo.Status == ConnectionStatus.Connected);

        //
        // Send the request; it should be auto-approved
        //
        await merryClient.Network.SendConnectionRequestTo(pippin, new List<GuidId>());

        await AssertConnected(merry, pippin);
        await Cleanup(merry, pippin);
    }

    [Test]
    [Description("Merry: Incoming, Pippin: Connected")]
    public async Task WillFailToAutoAccept_WhenState_Merry_Incoming_Pippin_Connected_and_Pippin_No_outgoing()
    {
        var merry = TestIdentities.Merry;
        var pippin = TestIdentities.Pippin;

        var merryClient = _scaffold.CreateOwnerApiClient(merry);
        var pippinClient = _scaffold.CreateOwnerApiClient(pippin);

        await Connect(merry, pippin);
        await AssertConnected(merry, pippin);

        await merryClient.Network.DisconnectFrom(pippin);
        await pippinClient.Network.SendConnectionRequestTo(merry);

        // ensure pippin does not have an outgoing request
        await pippinClient.Network.DeleteSentRequestTo(merry);

        //
        // Assert state is ready for test
        //

        var pippinInfo = await merryClient.Network.GetIncomingRequestFrom(pippin);
        Assert.IsNotNull(pippinInfo);

        var merryInfo = await pippinClient.Network.GetConnectionInfo(merry);
        Assert.IsTrue(merryInfo.Status == ConnectionStatus.Connected);

        //
        // Send the request; it should be auto-approved but will fail because there is no outgoing request from pippin
        //
        var response = await merryClient.Network.SendConnectionRequestRaw(pippin, new List<GuidId>());

        Assert.IsFalse(response.IsSuccessStatusCode);
        Assert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest);
        await Cleanup(merry, pippin);
    }

    [Test]
    [Description("Merry: Incoming, Pippin: Outgoing")]
    public void CanConnectWhenState_Merry_Incoming_Pippin_Outgoing()
    {
        Assert.Pass("Already tested above");
    }

    [Test]
    [Description("Merry: Incoming, Pippin: Incoming")]
    public async Task WillAutoConnectWhenState_Merry_Incoming_Pippin_Incoming()
    {
        var merry = TestIdentities.Merry;
        var pippin = TestIdentities.Pippin;

        var merryClient = _scaffold.CreateOwnerApiClient(merry);
        var pippinClient = _scaffold.CreateOwnerApiClient(pippin);

        await pippinClient.Network.SendConnectionRequestTo(merry);
        await merryClient.Network.SendConnectionRequestTo(pippin);

        await AssertConnected(merry, pippin);

        await Cleanup(merry, pippin);
    }

    [Test]
    [Description("Sam: Incoming, Frodo: None")]
    public async Task FailToConnect_Sam_Incoming_And_Frodo_Has_Deleted_Outgoing_Request()
    {
        //I know.. I switch to sam and frodo coz of a clean up issue :( bad me
        var sam = TestIdentities.Samwise;
        var frodo = TestIdentities.Frodo;

        var samClient = _scaffold.CreateOwnerApiClient(sam);
        var frodoClient = _scaffold.CreateOwnerApiClient(frodo);

        await frodoClient.Network.SendConnectionRequestTo(sam);
        await frodoClient.Network.DeleteSentRequestTo(sam);

        //
        // Assert state is ready for test
        //

        Assert.IsNotNull(await samClient.Network.GetIncomingRequestFrom(frodo));
        Assert.IsNull(await samClient.Network.GetOutgoingSentRequestTo(frodo));

        Assert.IsNull(await frodoClient.Network.GetIncomingRequestFrom(sam));
        Assert.IsNull(await frodoClient.Network.GetOutgoingSentRequestTo(sam));


        var response = await samClient.Network.SendConnectionRequestRaw(frodo, new List<GuidId>());
        Assert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest);

        await samClient.Network.DeleteConnectionRequestFrom(frodo);
    }

    [Test]
    [Description("Merry: None, Pippin: Connected")]
    public async Task CanConnectWhenState_Merry_None_Pippin_Connected()
    {
        var merry = TestIdentities.Merry;
        var pippin = TestIdentities.Pippin;

        var merryClient = _scaffold.CreateOwnerApiClient(merry);
        var pippinClient = _scaffold.CreateOwnerApiClient(pippin);

        await pippinClient.Network.SendConnectionRequestTo(merry);
        await merryClient.Network.AcceptConnectionRequest(pippin);

        await merryClient.Network.DisconnectFrom(pippin);

        //
        // Assert state is ready for test
        //

        Assert.IsTrue((await merryClient.Network.GetConnectionInfo(pippin)).Status == ConnectionStatus.None);

        //
        // They try to reconnect again fully
        //
        await Connect(merry, pippin);
        await AssertConnected(merry, pippin);
        await Cleanup(merry, pippin);
    }

    [Test]
    [Description("Merry: None, Pippin: Outgoing")]
    public async Task CanConnectWhenState_Merry_None_Pippin_Outgoing()
    {
        var merry = TestIdentities.Merry;
        var pippin = TestIdentities.Pippin;

        var merryClient = _scaffold.CreateOwnerApiClient(merry);
        var pippinClient = _scaffold.CreateOwnerApiClient(pippin);

        await merryClient.Configuration.DisableAutoAcceptIntroductions(true);
        await pippinClient.Configuration.DisableAutoAcceptIntroductions(true);
        
        await pippinClient.Network.SendConnectionRequestTo(merry);
        await merryClient.Network.DeleteConnectionRequestFrom(pippin);

        //
        // Assert state is ready for test
        //

        Assert.IsNotNull(await pippinClient.Network.GetOutgoingSentRequestTo(merry));
        Assert.IsNull(await pippinClient.Network.GetIncomingRequestFrom(merry));

        Assert.IsNull(await merryClient.Network.GetIncomingRequestFrom(pippin));
        Assert.IsNull(await merryClient.Network.GetOutgoingSentRequestTo(pippin));

        //
        // They try to reconnect again fully
        //
        await Connect(merry, pippin);
        await AssertConnected(merry, pippin);
        await Cleanup(merry, pippin);
    }

    [Test]
    [Description("Merry: None, Pippin: Incoming")]
    public async Task CanConnectWhenState_Merry_None_Pippin_Incoming()
    {
        var merry = TestIdentities.Merry;
        var pippin = TestIdentities.Pippin;

        var merryClient = _scaffold.CreateOwnerApiClient(merry);
        var pippinClient = _scaffold.CreateOwnerApiClient(pippin);

        await merryClient.Network.SendConnectionRequestTo(pippin);
        await merryClient.Network.DeleteSentRequestTo(pippin);

        //
        // Assert state is ready for test
        //

        Assert.IsNotNull(await pippinClient.Network.GetIncomingRequestFrom(merry));
        Assert.IsNull(await pippinClient.Network.GetOutgoingSentRequestTo(merry));

        Assert.IsNull(await merryClient.Network.GetIncomingRequestFrom(pippin));
        Assert.IsNull(await merryClient.Network.GetOutgoingSentRequestTo(pippin));

        //
        // They try to reconnect again fully
        //
        await Connect(merry, pippin);
        await AssertConnected(merry, pippin);
        await Cleanup(merry, pippin);
    }


    [Test]
    public async Task CanAutoAcceptIncomingConnectionRequestsWhenOutgoingRequestExists()
    {
        var merry = TestIdentities.Merry;
        var pippin = TestIdentities.Pippin;

        var merryClient = _scaffold.CreateOwnerApiClient(merry);
        var pippinClient = _scaffold.CreateOwnerApiClient(pippin);

        await merryClient.Network.SendConnectionRequestTo(pippin);
        await pippinClient.Network.SendConnectionRequestTo(merry);

        //
        await AssertConnected(merry, pippin);

        //
        Assert.IsNull(await merryClient.Network.GetOutgoingSentRequestTo(pippin));
        Assert.IsNull(await merryClient.Network.GetIncomingRequestFrom(pippin));

        await Cleanup(merry, pippin);
    }


    // [Test]
    // public async Task Reject_ConnectionRequest_when_SenderIsBlocked()
    // {
    //     Assert.Inconclusive("TODO");
    // }


    // [Test]
    // [Description("If the outgoing connection request is deleted before attempting establish a connection; the accept connection request will fail")]
    // public async Task FailToAcceptConnectionRequest_when_SendersOutgoingRequestWasDeleted()
    // {
    //     /*
    //      * 1. merry sends connection request to frodo
    //      * 2. merry deletes outgoing request to frodo
    //      * 3. Frodo accepts merry's connection request
    //      * 4. Frodo receives error, system deletes merry's connection request, frodo is told to resend it
    //      */
    // }

    [Test]
    public async Task CanReceiveMultipleConnectionRequestsFromSameSender()
    {
        var merry = TestIdentities.Merry;
        var pippin = TestIdentities.Pippin;

        var pippinClient = _scaffold.CreateOwnerApiClient(pippin);
        var merryClient = _scaffold.CreateOwnerApiClient(merry);

        await merryClient.Network.SendConnectionRequestTo(pippin);
        Assert.IsNotNull(await pippinClient.Network.GetIncomingRequestFrom(merry));

        await merryClient.Network.SendConnectionRequestTo(pippin);
        Assert.IsNotNull(await pippinClient.Network.GetIncomingRequestFrom(merry));
    }

    // [Test]
    // public async Task WhenConnectionIsSevered_BothPartiesHaveICRDeleted()
    // {
    //     Assert.Fail("TODO - should we support this?");
    // }

    private async Task Connect(TestIdentity sender, TestIdentity recipient)
    {
        //Note
        var senderOwnerClient = _scaffold.CreateOwnerApiClient(sender);
        var recipientOwnerClient = _scaffold.CreateOwnerApiClient(recipient);

        await senderOwnerClient.Network.SendConnectionRequestTo(recipient, new List<GuidId>());
        await recipientOwnerClient.Network.AcceptConnectionRequest(sender, new List<GuidId>());
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
        Assert.IsNull(await recipientOwnerClient.Network.GetIncomingRequestFrom(sender));
        Assert.IsNull(await recipientOwnerClient.Network.GetOutgoingSentRequestTo(sender));

        //
        // Test recipient's record on sender server
        //
        var recipientConnectionInfo = await senderOwnerClient.Network.GetConnectionInfo(recipient);
        Assert.IsTrue(recipientConnectionInfo.Status == ConnectionStatus.Connected);

        Assert.IsNull(await senderOwnerClient.Network.GetIncomingRequestFrom(recipient));
        Assert.IsNull(await senderOwnerClient.Network.GetOutgoingSentRequestTo(recipient));
    }

    private async Task Cleanup(TestIdentity merry, TestIdentity pippin)
    {
        var pippinClient = _scaffold.CreateOwnerApiClient(pippin);
        var merryClient = _scaffold.CreateOwnerApiClient(merry);

        await pippinClient.Network.DeleteConnectionRequestFrom(merry);
        await pippinClient.Network.DeleteSentRequestTo(merry);
        await pippinClient.Network.DisconnectFrom(merry);

        await merryClient.Network.DeleteConnectionRequestFrom(pippin);
        await merryClient.Network.DeleteSentRequestTo(pippin);
        await merryClient.Network.DisconnectFrom(pippin);

        // await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipient.OdinId);
    }
}