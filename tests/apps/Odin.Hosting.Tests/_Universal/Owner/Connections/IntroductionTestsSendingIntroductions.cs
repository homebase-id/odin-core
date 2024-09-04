using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Hosting.Tests._Universal.Owner.Connections;

public class IntroductionTestsSendingIntroductions
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
    public async Task WillSendConnectionRequestToIntroductions()
    {
        var frodo = TestIdentities.Frodo.OdinId;
        var sam = TestIdentities.Samwise.OdinId;
        var merry = TestIdentities.Merry.OdinId;

        var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var merryOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);
        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await merryOwnerClient.Configuration.EnableAutoAcceptIntroductions(false);
        await samOwnerClient.Configuration.EnableAutoAcceptIntroductions(false);

        await Prepare();

        var response = await frodoOwnerClient.Connections.SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam, merry]
        });

        var introResult = response.Content;
        Assert.IsTrue(introResult.RecipientStatus[sam]);
        Assert.IsTrue(introResult.RecipientStatus[merry]);

        // Assert: Sam should have a connection request from Merry and visa/versa
        await merryOwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);
        await samOwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);

        var samRequestFromMerryResponse = await samOwnerClient.Connections.GetIncomingRequestFrom(merry);
        var requestFromMerry = samRequestFromMerryResponse.Content;
        Assert.IsNotNull(requestFromMerry);
        Assert.IsTrue(requestFromMerry.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction);
        Assert.IsTrue(requestFromMerry.IntroducerOdinId == frodo);

        var merryRequestFromSamResponse = await merryOwnerClient.Connections.GetIncomingRequestFrom(sam);
        var requestFromSam = merryRequestFromSamResponse.Content;
        Assert.IsNotNull(requestFromSam);
        Assert.IsTrue(requestFromSam.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction);
        Assert.IsTrue(requestFromSam.IntroducerOdinId == frodo);

        await Shutdown();
    }

    [Test]
    public async Task WillFailWithBadRequestToSendConnectionRequestWhenRecipientAlreadyConnectedWithValidConnection()
    {
        await Prepare();

        // send a new connection request sam to frodo
        // should give back bad request

        Assert.Inconclusive("TODO");
    }

    [Test]
    public async Task WillFailToSendConnectionRequestViaIntroductionWhenRecipientIsBlocked()
    {
        // var frodo = TestIdentities.Frodo.OdinId;
        var sam = TestIdentities.Samwise.OdinId;
        var merry = TestIdentities.Merry.OdinId;

        var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var merryOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);
        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        // Merry blocks sam
        var blockResponse = await merryOwnerClient.Network.BlockConnection(sam);
        Assert.IsTrue(blockResponse.IsSuccessStatusCode);

        var samInfoResponse = await merryOwnerClient.Network.GetConnectionInfo(sam);
        Assert.IsTrue(samInfoResponse.IsSuccessStatusCode);
        Assert.IsTrue(samInfoResponse.Content.Status == ConnectionStatus.Blocked);

        await Prepare();

        var firstIntroductionResponse = await frodoOwnerClient.Connections.SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam, merry]
        });

        var introResult = firstIntroductionResponse.Content;
        Assert.IsTrue(introResult.RecipientStatus[sam]);
        Assert.IsTrue(introResult.RecipientStatus[merry]);

        // Assert: Sam should have a connection request from Merry and visa/versa
        await merryOwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);
        await samOwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);

        var samRequestFromMerryResponse = await samOwnerClient.Connections.GetIncomingRequestFrom(merry);
        var firstRequestFromMerry = samRequestFromMerryResponse.Content;
        Assert.IsNull(firstRequestFromMerry, "merry should not have been able to send a request to sam");

        var merryRequestFromSamResponse = await merryOwnerClient.Connections.GetIncomingRequestFrom(sam);
        var firstRequestFromSam = merryRequestFromSamResponse.Content;
        Assert.IsNull(firstRequestFromSam, "merry should not have a request from sam");
        
        var unblockResponse = await merryOwnerClient.Network.UnblockConnection(sam);
        Assert.IsTrue(unblockResponse.IsSuccessStatusCode);
    }

    [Test]
    public async Task WillFailToSendConnectionRequestWhenRecipientIsBlocked()
    {
        var sam = TestIdentities.Samwise.OdinId;
        var merry = TestIdentities.Merry.OdinId;

        var merryOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);
        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        var blockResponse = await merryOwnerClient.Network.BlockConnection(sam);
        Assert.IsTrue(blockResponse.IsSuccessStatusCode);

        var samInfoResponse = await merryOwnerClient.Network.GetConnectionInfo(sam);
        Assert.IsTrue(samInfoResponse.IsSuccessStatusCode);
        Assert.IsTrue(samInfoResponse.Content.Status == ConnectionStatus.Blocked);

        var requestToMerryResponse = await samOwnerClient.Connections.SendConnectionRequest(merry);
        Assert.IsTrue(requestToMerryResponse.StatusCode == HttpStatusCode.Forbidden);
        
        var unblockResponse = await merryOwnerClient.Network.UnblockConnection(sam);
        Assert.IsTrue(unblockResponse.IsSuccessStatusCode);
    }

    [Test]
    public async Task WillMergeOutgoingRequestWhenExistingRequestAndNewRequestAreIntroduced()
    {
        var frodo = TestIdentities.Frodo.OdinId;
        var sam = TestIdentities.Samwise.OdinId;
        var merry = TestIdentities.Merry.OdinId;

        var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var merryOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);
        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await merryOwnerClient.Configuration.EnableAutoAcceptIntroductions(false);
        await samOwnerClient.Configuration.EnableAutoAcceptIntroductions(false);

        await Prepare();

        var firstIntroductionResponse = await frodoOwnerClient.Connections.SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam, merry]
        });

        var introResult = firstIntroductionResponse.Content;
        Assert.IsTrue(introResult.RecipientStatus[sam]);
        Assert.IsTrue(introResult.RecipientStatus[merry]);

        // Assert: Sam should have a connection request from Merry and visa/versa
        await merryOwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);
        await samOwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);

        var samRequestFromMerryResponse = await samOwnerClient.Connections.GetIncomingRequestFrom(merry);
        var firstRequestFromMerry = samRequestFromMerryResponse.Content;
        Assert.IsNotNull(firstRequestFromMerry);
        Assert.IsTrue(firstRequestFromMerry.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction);
        Assert.IsTrue(firstRequestFromMerry.IntroducerOdinId == frodo);

        var merryRequestFromSamResponse = await merryOwnerClient.Connections.GetIncomingRequestFrom(sam);
        var firstRequestFromSam = merryRequestFromSamResponse.Content;
        Assert.IsNotNull(firstRequestFromSam);
        Assert.IsTrue(firstRequestFromSam.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction);
        Assert.IsTrue(firstRequestFromSam.IntroducerOdinId == frodo);

        //
        // Now that both have connection requests, send another introduction and validate they were merged
        //

        var secondInvitationResponse = await frodoOwnerClient.Connections.SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam, merry]
        });
        Assert.IsTrue(secondInvitationResponse.IsSuccessStatusCode);

        // Assert: Sam should have a connection request from Merry and visa/versa
        await merryOwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);
        await samOwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);

        var samRequestFromMerryResponse2 = await samOwnerClient.Connections.GetIncomingRequestFrom(merry);
        var secondRequestFromMerry = samRequestFromMerryResponse2.Content;
        Assert.IsNotNull(secondRequestFromMerry);
        Assert.IsTrue(secondRequestFromMerry.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction);
        Assert.IsTrue(secondRequestFromMerry.IntroducerOdinId == frodo);
        Assert.IsTrue(secondRequestFromMerry.ReceivedTimestampMilliseconds > firstRequestFromMerry.ReceivedTimestampMilliseconds);

        var merryRequestFromSamResponse2 = await merryOwnerClient.Connections.GetIncomingRequestFrom(sam);
        var secondRequestFromSam = merryRequestFromSamResponse2.Content;
        Assert.IsNotNull(secondRequestFromSam);
        Assert.IsTrue(secondRequestFromSam.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction);
        Assert.IsTrue(secondRequestFromSam.IntroducerOdinId == frodo);
        Assert.IsTrue(secondRequestFromSam.ReceivedTimestampMilliseconds > firstRequestFromSam.ReceivedTimestampMilliseconds);

        await Shutdown();
    }


    [Test]
    public async Task WhenAllowIntroductionPermissionNotGivenDuringIntroduction_OneRecipientGetConnectionRequest_SecondRecipientDoesNot()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merryOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        await Prepare();

        //removing frodo from Confirmed connections removes the allow introductions permission
        await samOwnerClient.Network.RevokeCircle(SystemCircleConstants.ConfirmedConnectionsCircleId, TestIdentities.Frodo.OdinId);

        var response = await frodoOwnerClient.Connections.SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [TestIdentities.Samwise.OdinId, TestIdentities.Merry.OdinId]
        });

        var introResult = response.Content;
        Assert.IsFalse(introResult.RecipientStatus[TestIdentities.Samwise.OdinId],
            "sam should reject since frodo does not have allow introductions permission");
        Assert.IsTrue(introResult.RecipientStatus[TestIdentities.Merry.OdinId]);

        // ensure introductions are processed
        await samOwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);
        await merryOwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);

        // Sam should get a connection request from merry (via frodo)
        var incomingRequestFromMerryResponse = await samOwnerClient.Connections.GetIncomingRequestFrom(TestIdentities.Merry.OdinId);
        Assert.IsTrue(incomingRequestFromMerryResponse.IsSuccessStatusCode);
        Assert.IsTrue(incomingRequestFromMerryResponse.Content.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction);

        var outgoingRequestToMerryResponse = await samOwnerClient.Connections.GetOutgoingSentRequestTo(TestIdentities.Merry.OdinId);
        Assert.IsTrue(outgoingRequestToMerryResponse.StatusCode == HttpStatusCode.NotFound, "sam should not have sent a request");

        var merryRequestFromSamResponse = await merryOwnerClient.Connections.GetIncomingRequestFrom(TestIdentities.Samwise.OdinId);
        Assert.IsTrue(merryRequestFromSamResponse.StatusCode == HttpStatusCode.NotFound, "sam should not have sent a request");

        var getSamConnectionInfoResponse = await merryOwnerClient.Network.GetConnectionInfo(TestIdentities.Samwise.OdinId);
        Assert.IsTrue(getSamConnectionInfoResponse.IsSuccessStatusCode);
        Assert.IsTrue(getSamConnectionInfoResponse.Content.Status == ConnectionStatus.None, "sam should not be connected to merry");

        var getMerryConnectionInfoResponse = await samOwnerClient.Network.GetConnectionInfo(TestIdentities.Merry.OdinId);
        Assert.IsTrue(getMerryConnectionInfoResponse.IsSuccessStatusCode);
        Assert.IsTrue(getMerryConnectionInfoResponse.Content.Status == ConnectionStatus.None, "merry should not be connected to sam");

        await Shutdown();
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

        await frodo.Connections.DisconnectFrom(sam.Identity.OdinId);
        await frodo.Connections.DisconnectFrom(merry.Identity.OdinId);

        await merry.Connections.DisconnectFrom(frodo.Identity.OdinId);
        await sam.Connections.DisconnectFrom(frodo.Identity.OdinId);
    }
}