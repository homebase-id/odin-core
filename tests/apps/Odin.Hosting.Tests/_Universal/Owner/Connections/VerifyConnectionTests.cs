using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Odin.Hosting.Tests._Universal.Owner.Connections;

public class VerifyConnectionTests
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
    public async Task VerificationReturnsFalseWhenRecipientNotConnectedButSenderIs()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await frodo.Connections.SendConnectionRequest(sam.OdinId, []);
        await sam.Connections.AcceptConnectionRequest(frodo.OdinId);

        await sam.Network.DisconnectFrom(frodo.OdinId);

        // resend it since we're already connected
        var response = await frodo.Network.VerifyConnection(sam.OdinId);
        
        Assert.IsTrue(response.StatusCode == HttpStatusCode.OK, $"Status code was {response.StatusCode}");
        Assert.IsNotNull(response.Content);
        Assert.IsFalse(response.Content.IsValid);
        Assert.IsFalse(response.Content.RemoteIdentityWasConnected);
        
        await Disconnect();
    }
    
    [Test]
    public async Task VerificationReturnsFalseWhenSenderNotConnectedButRecipientIs()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await frodo.Connections.SendConnectionRequest(sam.OdinId, []);
        await sam.Connections.AcceptConnectionRequest(frodo.OdinId);

        await frodo.Network.DisconnectFrom(sam.OdinId);

        // resend it since we're already connected
        var response = await frodo.Network.VerifyConnection(sam.OdinId);
        Assert.IsTrue(response.StatusCode == HttpStatusCode.OK, $"Status code was {response.StatusCode}");
        Assert.IsNotNull(response.Content);
        Assert.IsFalse(response.Content.IsValid);
        Assert.IsNull(response.Content.RemoteIdentityWasConnected);

        await Disconnect();
    }

    [Test]
    public async Task WillVerifyValidConnectionWhenAlreadyConnectedAndReturnBadRequest()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await frodo.Connections.SendConnectionRequest(sam.OdinId, []);
        await sam.Connections.AcceptConnectionRequest(frodo.OdinId);

        // resend it since we're already connected
        var response = await frodo.Connections.SendConnectionRequest(sam.OdinId, []);
        Assert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest, $"Status code was {response.StatusCode}");

        await Disconnect();
    }

    [Test]
    public async Task CanVerifyValidConnection()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await frodo.Connections.SendConnectionRequest(sam.OdinId);
        await sam.Connections.AcceptConnectionRequest(frodo.OdinId);

        var response = await frodo.Network.VerifyConnection(sam.OdinId);
        var result = response.Content;
        Assert.IsNotNull(result);
        Assert.IsTrue(response.IsSuccessStatusCode);
        Assert.IsTrue(result.IsValid);
        Assert.IsTrue(result.RemoteIdentityWasConnected);

        await Disconnect();
    }
    
    [Test]
    public async Task VerifyConnectionFailsWhenRecipientNotConnected()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await frodo.Connections.SendConnectionRequest(sam.OdinId, []);
        await sam.Connections.AcceptConnectionRequest(frodo.OdinId);


        //
        // Now: have frodo delete it but sam keep it
        //
        await frodo.Connections.DisconnectFrom(sam.OdinId);
        var response = await frodo.Network.VerifyConnection(sam.OdinId);
        Assert.IsTrue(response.IsSuccessStatusCode);

        var result = response.Content;
        Assert.IsFalse(result.IsValid);
        Assert.IsNull(result.RemoteIdentityWasConnected);


        await Disconnect();
    }
    
    
    [Test]
    public async Task CanSendConnectionRequestWhenSenderNotConnectedButRecipientIsConnected()
    {
        // This causes an issue because the verification fails as it cannot get to the ICR key
        // this is because the sender is not connected to the recipient
        // but the recipient has a record of the sender
        
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        
        await frodo.Configuration.DisableAutoAcceptIntroductions(true);
        await sam.Configuration.DisableAutoAcceptIntroductions(true);
        
        await frodo.Connections.SendConnectionRequest(sam.OdinId);
        await sam.Connections.AcceptConnectionRequest(frodo.OdinId);

        await frodo.Network.DisconnectFrom(sam.OdinId);

        // resend it since we're already connected
        var response = await frodo.Connections.SendConnectionRequest(sam.OdinId);
        Assert.IsTrue(response.StatusCode == HttpStatusCode.OK);

        await Disconnect();
    }

    [Test]
    public async Task CanSendingConnectionRequestWhenRecipientNotConnectedButSenderIsConnected()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await frodo.Connections.SendConnectionRequest(sam.OdinId, []);
        await sam.Connections.AcceptConnectionRequest(frodo.OdinId);

        await sam.Network.DisconnectFrom(frodo.OdinId);

        // resend it since we're already connected
        var response = await frodo.Connections.SendConnectionRequest(sam.OdinId, []);
        Assert.IsTrue(response.StatusCode == HttpStatusCode.OK, $"Status code was {response.StatusCode}");

        await Disconnect();
    }
    
    private async Task Disconnect()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merry = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        await frodo.Connections.DisconnectFrom(sam.Identity.OdinId);
        await frodo.Connections.DisconnectFrom(merry.Identity.OdinId);

        await merry.Connections.DisconnectFrom(frodo.Identity.OdinId);
        await sam.Connections.DisconnectFrom(frodo.Identity.OdinId);

        await sam.Connections.DisconnectFrom(merry.Identity.OdinId);
        await merry.Connections.DisconnectFrom(sam.Identity.OdinId);
    }
}