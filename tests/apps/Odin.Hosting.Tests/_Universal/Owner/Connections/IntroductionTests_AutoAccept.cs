using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Hosting.Tests._Universal.Owner.Connections;

public class IntroductionTests_AutoAccept
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
    public async Task CanAutoAcceptIncomingConnectionRequestsWhenIntroductionExists()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        await Prepare();

        var response = await frodoOwnerClient.Connections.SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [TestIdentities.Samwise.OdinId, TestIdentities.Merry.OdinId]
        });


        var introResult = response.Content;
        Assert.IsTrue(introResult.RecipientStatus[TestIdentities.Samwise.OdinId]);
        Assert.IsTrue(introResult.RecipientStatus[TestIdentities.Merry.OdinId]);


        // Assert: Sam should have a connection request from Merry and visa/versa

        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merryOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        await merryOwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);
        await samOwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);

        var outgoingRequestToMerryResponse = await samOwnerClient.Connections.GetOutgoingSentRequestTo(TestIdentities.Merry.OdinId);
        var outgoingRequestToMerry = outgoingRequestToMerryResponse.Content;
        var samRequestFromMerryResponse = await samOwnerClient.Connections.GetIncomingRequestFrom(TestIdentities.Merry.OdinId);
        var requestFromMerry = samRequestFromMerryResponse.Content;
        Assert.IsNotNull(requestFromMerry);
        Assert.IsTrue(requestFromMerry.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction);
        Assert.IsTrue(requestFromMerry.IntroducerOdinId == TestIdentities.Frodo.OdinId);
        Assert.IsTrue(outgoingRequestToMerry.CircleIds.Exists(cid => cid == SystemCircleConstants.AutoConnectionsCircleId));
        Assert.IsFalse(outgoingRequestToMerry.CircleIds.Exists(cid => cid == SystemCircleConstants.ConfirmedConnectionsCircleId));

        var merryRequestFromSamResponse = await merryOwnerClient.Connections.GetIncomingRequestFrom(TestIdentities.Samwise.OdinId);
        var requestFromSam = merryRequestFromSamResponse.Content;
        Assert.IsNotNull(requestFromSam);
        Assert.IsTrue(requestFromSam.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction);
        Assert.IsTrue(requestFromSam.IntroducerOdinId == TestIdentities.Frodo.OdinId);
        Assert.IsTrue(requestFromSam.CircleIds.Exists(cid => cid == SystemCircleConstants.AutoConnectionsCircleId));
        Assert.IsFalse(requestFromSam.CircleIds.Exists(cid => cid == SystemCircleConstants.ConfirmedConnectionsCircleId));

        await Shutdown();
    }


    [Test]
    public async Task WillNotAutoAcceptWhenIntroducerDoesNotHaveAllowIntroductionsPermission()
    {
        await Task.CompletedTask;
        Assert.Inconclusive("TODO");
    }

    [Test]
    public async Task RequestTypeIsAutoWhenSentBecauseOfAnIntroduction()
    {
        await Task.CompletedTask;
        Assert.Inconclusive("TODO");
    }

    [Test]
    public async Task WillMergeOutgoingRequestWhenExistingRequestAndNewRequestAreAuto()
    {
        await Task.CompletedTask;
        Assert.Inconclusive("TODO");
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

    private async Task Shutdown()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merry = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        await frodo.Connections.DisconnectFrom(sam.OdinId);
        await frodo.Connections.DisconnectFrom(merry.OdinId);

        await merry.Connections.DisconnectFrom(frodo.OdinId);
        await sam.Connections.DisconnectFrom(frodo.OdinId);
    }
}