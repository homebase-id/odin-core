using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Hosting.Tests._Universal.Owner.Connections.Introductions;

public class ConfirmConnectionTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Frodo, TestIdentities.Merry, TestIdentities.Samwise });
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
    public async Task CanConfirmConnection()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merryOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        await Prepare();

        var response = await frodoOwnerClient.Connections.SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [TestIdentities.Samwise.OdinId, TestIdentities.Merry.OdinId]
        });

        await frodoOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);

        var introResult = response.Content;
        ClassicAssert.IsTrue(introResult.RecipientStatus[TestIdentities.Samwise.OdinId]);
        ClassicAssert.IsTrue(introResult.RecipientStatus[TestIdentities.Merry.OdinId]);

        await merryOwnerClient.Connections.AwaitIntroductionsProcessing();
        await samOwnerClient.Connections.AwaitIntroductionsProcessing();

        //validate they are connected
        var samConnectionInfoResponse = await merryOwnerClient.Network.GetConnectionInfo(TestIdentities.Samwise.OdinId);
        ClassicAssert.IsTrue(samConnectionInfoResponse.IsSuccessStatusCode);
        ClassicAssert.IsTrue(samConnectionInfoResponse.Content.Status == ConnectionStatus.Connected);
        ClassicAssert.IsTrue(
            samConnectionInfoResponse.Content.AccessGrant.CircleGrants.Exists(cg =>
                cg.CircleId == SystemCircleConstants.AutoConnectionsCircleId));
        ClassicAssert.IsFalse(samConnectionInfoResponse.Content.AccessGrant.CircleGrants.Exists(
            cg => cg.CircleId == SystemCircleConstants.ConfirmedConnectionsCircleId));

        //merry confirms - now sam should be in a new circle
        var merryConfirmationResponse = await merryOwnerClient.Network.ConfirmConnection(TestIdentities.Samwise.OdinId);
        ClassicAssert.IsTrue(merryConfirmationResponse.IsSuccessStatusCode);
        var samConnectionInfoResponse2 = await merryOwnerClient.Network.GetConnectionInfo(TestIdentities.Samwise.OdinId);
        ClassicAssert.IsTrue(samConnectionInfoResponse2.IsSuccessStatusCode);
        ClassicAssert.IsFalse(
            samConnectionInfoResponse2.Content.AccessGrant.CircleGrants.Exists(cg =>
                cg.CircleId == SystemCircleConstants.AutoConnectionsCircleId));
        ClassicAssert.IsTrue(samConnectionInfoResponse2.Content.AccessGrant.CircleGrants.Exists(
            cg => cg.CircleId == SystemCircleConstants.ConfirmedConnectionsCircleId));


        var samConfirmationResponse = await samOwnerClient.Network.ConfirmConnection(TestIdentities.Merry.OdinId);
        ClassicAssert.IsTrue(samConfirmationResponse.IsSuccessStatusCode, $"status code was {samConfirmationResponse.StatusCode}");
        var merryConnectionInfo = await merryOwnerClient.Network.GetConnectionInfo(TestIdentities.Samwise.OdinId);
        ClassicAssert.IsTrue(merryConnectionInfo.IsSuccessStatusCode);
        ClassicAssert.IsFalse(
            merryConnectionInfo.Content.AccessGrant.CircleGrants.Exists(cg =>
                cg.CircleId == SystemCircleConstants.AutoConnectionsCircleId));
        ClassicAssert.IsTrue(merryConnectionInfo.Content.AccessGrant.CircleGrants.Exists(
            cg => cg.CircleId == SystemCircleConstants.ConfirmedConnectionsCircleId));

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


        await frodo.Connections.DeleteAllIntroductions();
        await sam.Connections.DeleteAllIntroductions();
        await merry.Connections.DeleteAllIntroductions();
    }

    private async Task Cleanup()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merry = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        await frodo.Connections.DeleteAllIntroductions();
        await sam.Connections.DeleteAllIntroductions();
        await merry.Connections.DeleteAllIntroductions();

        await frodo.Connections.DisconnectFrom(sam.Identity.OdinId);
        await frodo.Connections.DisconnectFrom(merry.Identity.OdinId);

        await merry.Connections.DisconnectFrom(frodo.Identity.OdinId);
        await sam.Connections.DisconnectFrom(frodo.Identity.OdinId);

        await merry.Connections.DisconnectFrom(sam.Identity.OdinId);
        await sam.Connections.DisconnectFrom(merry.Identity.OdinId);
    }
}