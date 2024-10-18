using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Odin.Hosting.Tests._Universal.Owner.ProductionDataConversion;

public class EnsureVerificationHashTests
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
    public async Task CanUpdateExistingConnectionsWithVerificationHash()
    {
        // this is a little hard to test since we are generating
        // the verification hash in this new code and we do not send the verification
        // hash back to the client

        // none the less, I will at least call the endpoint

        await Prepare();

        var frodo = TestIdentities.Frodo.OdinId;
        var sam = TestIdentities.Samwise.OdinId;

        var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        var response1 = await frodoOwnerClient.DataConversion.PrepareIntroductionsRelease();
        Assert.IsTrue(response1.IsSuccessStatusCode);

        var response2 = await samOwnerClient.DataConversion.PrepareIntroductionsRelease();
        Assert.IsTrue(response2.IsSuccessStatusCode);

        var samVerificationResponse = await frodoOwnerClient.Network.VerifyConnection(sam);
        Assert.IsTrue(samVerificationResponse.IsSuccessStatusCode);
        Assert.IsTrue(samVerificationResponse.Content.IsValid);
        Assert.IsTrue(samVerificationResponse.Content.RemoteIdentityWasConnected);

        var frodoVerificationResponse = await samOwnerClient.Network.VerifyConnection(frodo);
        Assert.IsTrue(frodoVerificationResponse.IsSuccessStatusCode);
        Assert.IsTrue(frodoVerificationResponse.Content.IsValid);
        Assert.IsTrue(frodoVerificationResponse.Content.RemoteIdentityWasConnected);

        await Cleanup();
    }

    private async Task Prepare()
    {
        //you have 3 hobbits

        // Frodo is connected to Sam and Merry
        // Sam and Merry are not connected

        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merry = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        await frodo.Connections.SendConnectionRequest(sam.OdinId, []);
        await frodo.Connections.SendConnectionRequest(merry.OdinId, []);

        await merry.Connections.AcceptConnectionRequest(frodo.OdinId);
        await sam.Connections.AcceptConnectionRequest(frodo.OdinId);
    }

    private async Task Cleanup()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merry = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        await frodo.Connections.DisconnectFrom(sam.OdinId);
        await frodo.Connections.DisconnectFrom(merry.OdinId);

        await merry.Connections.DisconnectFrom(frodo.OdinId);
        await sam.Connections.DisconnectFrom(frodo.OdinId);

        await merry.Connections.DisconnectFrom(sam.OdinId);
        await sam.Connections.DisconnectFrom(merry.OdinId);
    }
}