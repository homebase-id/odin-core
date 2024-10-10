using System;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Hosting.Tests._Universal.Owner.Connections;

public class CircleGrantTests
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
    public async Task CannotGrantCircleWhenIdentityInAutoAcceptCircle()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merryOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        var sam = TestIdentities.Samwise.OdinId;
        var merry = TestIdentities.Merry.OdinId;

        await Prepare();

        var targetCircle = Guid.NewGuid();
        var merryCreatesCircleResponse = await merryOwnerClient.Network.CreateCircle(targetCircle, "some circle", new PermissionSetGrantRequest()
        {
            PermissionSet = new(PermissionKeys.AllowIntroductions)
        });
        Assert.IsTrue(merryCreatesCircleResponse.IsSuccessStatusCode);

        var response = await frodoOwnerClient.Connections.SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam, merry]
        });

        var introResult = response.Content;
        Assert.IsTrue(introResult.RecipientStatus[sam]);
        Assert.IsTrue(introResult.RecipientStatus[merry]);

        //ensure sam sends a request
        var samProcessResponse = await samOwnerClient.Connections.ProcessIncomingIntroductions();
        Assert.IsTrue(samProcessResponse.IsSuccessStatusCode);

        var merryProcessResponse = await merryOwnerClient.Connections.ProcessIncomingIntroductions();
        Assert.IsTrue(merryProcessResponse.IsSuccessStatusCode);

        //validate they are connected
        var samConnectionInfoResponse = await merryOwnerClient.Network.GetConnectionInfo(TestIdentities.Samwise.OdinId);
        Assert.IsTrue(samConnectionInfoResponse.IsSuccessStatusCode);
        Assert.IsTrue(samConnectionInfoResponse.Content.Status == ConnectionStatus.Connected);
        Assert.IsTrue(samConnectionInfoResponse.Content.AccessGrant.CircleGrants.Exists(cg => cg.CircleId == SystemCircleConstants.AutoConnectionsCircleId));
        Assert.IsFalse(samConnectionInfoResponse.Content.AccessGrant.CircleGrants.Exists(
            cg => cg.CircleId == SystemCircleConstants.ConfirmedConnectionsCircleId));

        // Try to grant before confirming connection
        var grantCircleResponse = await merryOwnerClient.Network.GrantCircle(targetCircle, sam);
        Assert.IsFalse(grantCircleResponse.IsSuccessStatusCode);

        await Cleanup();
    }

    [Test]
    public async Task CanGrantCircleAfterConfirmConnection()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merryOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        var sam = TestIdentities.Samwise.OdinId;
        var merry = TestIdentities.Merry.OdinId;

        await Prepare();

        var targetCircle = Guid.NewGuid();
        var merryCreatesCircleResponse = await merryOwnerClient.Network.CreateCircle(targetCircle, "some circle", new PermissionSetGrantRequest()
        {
            PermissionSet = new(PermissionKeys.AllowIntroductions)
        });
        Assert.IsTrue(merryCreatesCircleResponse.IsSuccessStatusCode);

        var response = await frodoOwnerClient.Connections.SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam, merry]
        });

        var introResult = response.Content;
        Assert.IsTrue(introResult.RecipientStatus[sam]);
        Assert.IsTrue(introResult.RecipientStatus[merry]);

        //ensure sam sends a request
        var samProcessResponse = await samOwnerClient.Connections.ProcessIncomingIntroductions();
        Assert.IsTrue(samProcessResponse.IsSuccessStatusCode);

        var merryProcessResponse = await merryOwnerClient.Connections.ProcessIncomingIntroductions();
        Assert.IsTrue(merryProcessResponse.IsSuccessStatusCode);

        //validate they are connected
        var samConnectionInfoResponse = await merryOwnerClient.Network.GetConnectionInfo(TestIdentities.Samwise.OdinId);
        Assert.IsTrue(samConnectionInfoResponse.IsSuccessStatusCode);
        Assert.IsTrue(samConnectionInfoResponse.Content.Status == ConnectionStatus.Connected);
        Assert.IsTrue(samConnectionInfoResponse.Content.AccessGrant.CircleGrants.Exists(cg => cg.CircleId == SystemCircleConstants.AutoConnectionsCircleId));
        Assert.IsFalse(samConnectionInfoResponse.Content.AccessGrant.CircleGrants.Exists(
            cg => cg.CircleId == SystemCircleConstants.ConfirmedConnectionsCircleId));

        // Try to grant before confirming connection
        var grantCircleResponse = await merryOwnerClient.Network.GrantCircle(targetCircle, sam);
        Assert.IsFalse(grantCircleResponse.IsSuccessStatusCode);

        //merry confirms - now sam should be in confirmed circle
        var merryConfirmationResponse = await merryOwnerClient.Network.ConfirmConnection(TestIdentities.Samwise.OdinId);
        Assert.IsTrue(merryConfirmationResponse.IsSuccessStatusCode);
        var samConnectionInfoResponse2 = await merryOwnerClient.Network.GetConnectionInfo(TestIdentities.Samwise.OdinId);
        Assert.IsTrue(samConnectionInfoResponse2.IsSuccessStatusCode);
        Assert.IsFalse(samConnectionInfoResponse2.Content.AccessGrant.CircleGrants.Exists(cg => cg.CircleId == SystemCircleConstants.AutoConnectionsCircleId));
        Assert.IsTrue(samConnectionInfoResponse2.Content.AccessGrant.CircleGrants.Exists(
            cg => cg.CircleId == SystemCircleConstants.ConfirmedConnectionsCircleId));

        // try to add
        var grantCircleResponse2 = await merryOwnerClient.Network.GrantCircle(targetCircle, sam);
        Assert.IsTrue(grantCircleResponse2.IsSuccessStatusCode);

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

        await frodo.Connections.DisconnectFrom(sam.Identity.OdinId);
        await frodo.Connections.DisconnectFrom(merry.Identity.OdinId);

        await merry.Connections.DisconnectFrom(frodo.Identity.OdinId);
        await sam.Connections.DisconnectFrom(frodo.Identity.OdinId);

        await merry.Connections.DisconnectFrom(sam.Identity.OdinId);
        await sam.Connections.DisconnectFrom(merry.Identity.OdinId);
    }
}