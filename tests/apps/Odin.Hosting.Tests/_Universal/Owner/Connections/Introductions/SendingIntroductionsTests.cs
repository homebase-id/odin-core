using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Hosting.Tests._Universal.Owner.Connections.Introductions;

public class SendingIntroductionsTests
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
    public async Task WillIgnoreIntroductionIfIntrodceeIsBlocked()
    {
        var frodo = TestIdentities.Frodo.OdinId;
        var sam = TestIdentities.Samwise.OdinId;
        var merry = TestIdentities.Merry.OdinId;

        var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var merryOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);
        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await merryOwnerClient.Configuration.DisableAutoAcceptIntroductions(true);
        await samOwnerClient.Configuration.DisableAutoAcceptIntroductions(true);

        await Prepare();

        // block errrrrbody
        await merryOwnerClient.Network.BlockConnection(sam);
        await samOwnerClient.Network.BlockConnection(merry);

        var response = await frodoOwnerClient.Connections.SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam, merry]
        });

        //
        // we should return true to the sender so they do not know anything other than the introduction was received
        //
        var introResult = response.Content;
        Assert.IsTrue(introResult.RecipientStatus[sam]);
        Assert.IsTrue(introResult.RecipientStatus[merry]);

        await frodoOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);

        //
        // neither should have connection requests
        //
        var samRequestFromMerryResponse = await samOwnerClient.Connections.GetIncomingRequestFrom(merry);
        Assert.IsNull(samRequestFromMerryResponse.Content);

        var merryRequestFromSamResponse = await samOwnerClient.Connections.GetIncomingRequestFrom(merry);
        Assert.IsNull(merryRequestFromSamResponse.Content);

        //
        // neither should have someone in the list
        //
        var samReceivedIntroductionsResponse = await samOwnerClient.Connections.GetReceivedIntroductions();
        Assert.IsTrue(samReceivedIntroductionsResponse.IsSuccessStatusCode);
        Assert.IsTrue(samReceivedIntroductionsResponse.Content.Count == 0);

        var merryReceivedIntroductionsResponse = await merryOwnerClient.Connections.GetReceivedIntroductions();
        Assert.IsTrue(merryReceivedIntroductionsResponse.IsSuccessStatusCode);
        Assert.IsTrue(merryReceivedIntroductionsResponse.Content.Count == 0);

        await Cleanup();
    }


    [Test]
    public async Task WillIgnoreIntroductionIfAlreadyConnected()
    {
        var frodo = TestIdentities.Frodo.OdinId;
        var sam = TestIdentities.Samwise.OdinId;
        var merry = TestIdentities.Merry.OdinId;

        var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var merryOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);
        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await merryOwnerClient.Configuration.DisableAutoAcceptIntroductions(true);
        await samOwnerClient.Configuration.DisableAutoAcceptIntroductions(true);

        await Prepare();

        // connect merry and sam
        await merryOwnerClient.Connections.SendConnectionRequest(sam);
        await samOwnerClient.Connections.AcceptConnectionRequest(merry);

        var response = await frodoOwnerClient.Connections.SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam, merry]
        });

        //
        // we should return true to the sender so they do not know anything other than the introduction was received
        //
        var introResult = response.Content;
        Assert.IsTrue(introResult.RecipientStatus[sam]);
        Assert.IsTrue(introResult.RecipientStatus[merry]);

        await frodoOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);

        //
        // neither should have connection requests
        //
        var samRequestFromMerryResponse = await samOwnerClient.Connections.GetIncomingRequestFrom(merry);
        Assert.IsNull(samRequestFromMerryResponse.Content);

        var merryRequestFromSamResponse = await samOwnerClient.Connections.GetIncomingRequestFrom(merry);
        Assert.IsNull(merryRequestFromSamResponse.Content);

        //
        // neither should have someone in the list
        //
        var samReceivedIntroductionsResponse = await samOwnerClient.Connections.GetReceivedIntroductions();
        Assert.IsTrue(samReceivedIntroductionsResponse.IsSuccessStatusCode);
        Assert.IsTrue(samReceivedIntroductionsResponse.Content.Count == 0);

        var merryReceivedIntroductionsResponse = await merryOwnerClient.Connections.GetReceivedIntroductions();
        Assert.IsTrue(merryReceivedIntroductionsResponse.IsSuccessStatusCode);
        Assert.IsTrue(merryReceivedIntroductionsResponse.Content.Count == 0);

        await Cleanup();
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

        await merryOwnerClient.Configuration.DisableAutoAcceptIntroductions(true);
        await samOwnerClient.Configuration.DisableAutoAcceptIntroductions(true);

        await Prepare();

        var response = await frodoOwnerClient.Connections.SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam, merry]
        });

        var introResult = response.Content;
        Assert.IsTrue(introResult.RecipientStatus[sam]);
        Assert.IsTrue(introResult.RecipientStatus[merry]);

        await frodoOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);

        // There are background processes running which will send introductions automatically
        // we can also call an endpoint to force this.
        // since we don't know when this will occur, we'll call the endpoint

        // there's also logic dictating that when sending a connection request due to an
        // introduction, we do not send it if there's already an incoming request

        // so - we have to add some logic into this test

        // firstly, force sending a request for both parties.
        var merryProcessResponse = await merryOwnerClient.Connections.ProcessIncomingIntroductions();
        Assert.IsTrue(merryProcessResponse.IsSuccessStatusCode);

        var samProcessResponse = await samOwnerClient.Connections.ProcessIncomingIntroductions();
        Assert.IsTrue(samProcessResponse.IsSuccessStatusCode);

        // now, one of them should have a connection request, start with Sam
        var samRequestFromMerryResponse = await samOwnerClient.Connections.GetIncomingRequestFrom(merry);
        var requestFromMerry = samRequestFromMerryResponse.Content;

        if (null == requestFromMerry)
        {
            // merry should have a request from sam
            var merryRequestFromSamResponse = await merryOwnerClient.Connections.GetIncomingRequestFrom(sam);
            var requestFromSam = merryRequestFromSamResponse.Content;

            Assert.IsNotNull(requestFromSam);
            Assert.IsTrue(requestFromSam.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction);
            Assert.IsTrue(requestFromSam.IntroducerOdinId == frodo);
        }
        else
        {
            Assert.IsTrue(requestFromMerry.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction);
            Assert.IsTrue(requestFromMerry.IntroducerOdinId == frodo);
        }

        // both should have introductions in the list
        var samReceivedIntroductionsResponse = await samOwnerClient.Connections.GetReceivedIntroductions();
        Assert.IsTrue(samReceivedIntroductionsResponse.IsSuccessStatusCode);
        var samsIntroductionToMerry = samReceivedIntroductionsResponse.Content.Single();
        Assert.IsTrue(samsIntroductionToMerry.Identity == merry);
        Assert.IsTrue(samsIntroductionToMerry.IntroducerOdinId == frodo);
        Assert.IsTrue(samsIntroductionToMerry.Received > 0);

        var merryReceivedIntroductionsResponse = await merryOwnerClient.Connections.GetReceivedIntroductions();
        Assert.IsTrue(merryReceivedIntroductionsResponse.IsSuccessStatusCode);
        var merrysIntroductionToSam = merryReceivedIntroductionsResponse.Content.Single();
        Assert.IsTrue(merrysIntroductionToSam.Identity == sam);
        Assert.IsTrue(merrysIntroductionToSam.IntroducerOdinId == frodo);
        Assert.IsTrue(merrysIntroductionToSam.Received > 0);

        await Cleanup();
    }

    [Test]
    public async Task WillFailToSendConnectionRequestWithBadRequestWhenRecipientAlreadyConnectedWithValidConnection()
    {
        await Prepare();

        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var sendConnectionRequestResponse = await samOwnerClient.Connections.SendConnectionRequest(TestIdentities.Frodo.OdinId);
        Assert.IsTrue(sendConnectionRequestResponse.StatusCode == HttpStatusCode.BadRequest);

        await Cleanup();
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

        var introsResponse = await merryOwnerClient.Connections.GetReceivedIntroductions();
        Assert.IsFalse(introsResponse.Content.Any(),
            "Cannot start test - merry has pending introductions. this probably happened because they were cleaned up from other tests");

        // Merry blocks sam
        var blockResponse = await merryOwnerClient.Network.BlockConnection(sam);
        Assert.IsTrue(blockResponse.IsSuccessStatusCode);

        var samInfoResponse = await merryOwnerClient.Network.GetConnectionInfo(sam);
        Assert.IsTrue(samInfoResponse.IsSuccessStatusCode);
        Assert.IsTrue(samInfoResponse.Content.Status == ConnectionStatus.Blocked);

        await Prepare();

        var samRequestFromMerryResponse2 = await samOwnerClient.Connections.GetIncomingRequestFrom(merryOwnerClient.OdinId);
        var firstRequestFromMerry2 = samRequestFromMerryResponse2.Content;
        Assert.IsNull(firstRequestFromMerry2, "xx merry already has a request from sam");

        var firstIntroductionResponse = await frodoOwnerClient.Connections.SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam, merry]
        });

        var introResult = firstIntroductionResponse.Content;
        Assert.IsTrue(introResult.RecipientStatus[sam]);
        Assert.IsTrue(introResult.RecipientStatus[merry]);

        await frodoOwnerClient.Connections.AwaitIntroductionsProcessing();
        
        // wait for outbox on sam and merry so they can send their connection requests
        await samOwnerClient.Connections.AwaitIntroductionsProcessing();
        await merryOwnerClient.Connections.AwaitIntroductionsProcessing();
        
        var samRequestFromMerryResponse = await samOwnerClient.Connections.GetIncomingRequestFrom(merry);
        var firstRequestFromMerry = samRequestFromMerryResponse.Content;
        Assert.IsNull(firstRequestFromMerry, "merry should not have been able to send a request to sam");

        var merryRequestFromSamResponse = await merryOwnerClient.Connections.GetIncomingRequestFrom(sam);
        var firstRequestFromSam = merryRequestFromSamResponse.Content;
        Assert.IsNull(firstRequestFromSam, "merry should not have a request from sam");

        var unblockResponse = await merryOwnerClient.Network.UnblockConnection(sam);
        Assert.IsTrue(unblockResponse.IsSuccessStatusCode);

        await Cleanup();
    }

    [Test]
    public async Task WillFailToSendConnectionRequestWhenRecipientIsBlocked()
    {
        var frodo = TestIdentities.Frodo.OdinId;
        var pippin = TestIdentities.Pippin.OdinId;

        var pippinOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);
        var frodoOnwerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var blockResponse = await pippinOwnerClient.Network.BlockConnection(frodo);
        Assert.IsTrue(blockResponse.IsSuccessStatusCode);

        var frodoInfoResponse = await pippinOwnerClient.Network.GetConnectionInfo(frodo);
        Assert.IsTrue(frodoInfoResponse.IsSuccessStatusCode);
        Assert.IsTrue(frodoInfoResponse.Content.Status == ConnectionStatus.Blocked);

        var requestToPippinResponse = await frodoOnwerClient.Connections.SendConnectionRequest(pippin);
        Assert.IsTrue(requestToPippinResponse.StatusCode == HttpStatusCode.Forbidden);

        var unblockResponse = await pippinOwnerClient.Network.UnblockConnection(frodo);
        Assert.IsTrue(unblockResponse.IsSuccessStatusCode);

        await Cleanup();
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
        // Assert.IsFalse(introResult.RecipientStatus[TestIdentities.Samwise.OdinId],
        // "sam should reject since frodo does not have allow-introductions permission");
        Assert.IsTrue(introResult.RecipientStatus[TestIdentities.Merry.OdinId]);

        // Note; I have to use a delay because the outbox will never be
        // empty and, currently, there is no way to do an exclusion test on the outbox 
        // await frodoOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);
        await Task.Delay(1000 * 3);

        var samOutboxItem =
            await frodoOwnerClient.DriveRedux.GetOutboxItem(SystemDriveConstants.TransientTempDrive,
                TestIdentities.Samwise.OdinId.ToHashId(), TestIdentities.Samwise.OdinId);
        Assert.IsNotNull(samOutboxItem, "there should be an outbox item for sam since it failed he blocked incoming introductions");

        
        // ensure introductions are processed
        var samProcessResponse = await samOwnerClient.Connections.ProcessIncomingIntroductions();
        Assert.IsTrue(samProcessResponse.IsSuccessStatusCode);

        var merryProcessResponse = await merryOwnerClient.Connections.ProcessIncomingIntroductions();
        Assert.IsTrue(merryProcessResponse.IsSuccessStatusCode);

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

        await Cleanup();
    }


    [Test]
    public async Task CanAcceptConnectionRequestManually_AndRelatedIntroductionsAreDeleted()
    {
        // Note: for your sanity, remember this is a background process that is
        // automatically accepting introductions that are eligible
        var sam = TestIdentities.Samwise.OdinId;
        var merry = TestIdentities.Merry.OdinId;

        var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merryOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        await merryOwnerClient.Configuration.DisableAutoAcceptIntroductions(true);
        await samOwnerClient.Configuration.DisableAutoAcceptIntroductions(true);

        await Prepare();

        var response = await frodoOwnerClient.Connections.SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam, merry]
        });

        var introResult = response.Content;
        Assert.IsTrue(introResult.RecipientStatus[sam]);
        Assert.IsTrue(introResult.RecipientStatus[merry]);

        await frodoOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);

        //
        // validate introductions exist
        //

        var merryIntroductionsResponse = await merryOwnerClient.Connections.GetReceivedIntroductions();
        Assert.IsTrue(merryIntroductionsResponse.IsSuccessStatusCode);
        Assert.IsTrue(merryIntroductionsResponse.Content.Any(intro => intro.Identity == sam));

        var samIntroductionsResponse = await samOwnerClient.Connections.GetReceivedIntroductions();
        Assert.IsTrue(samIntroductionsResponse.IsSuccessStatusCode);
        Assert.IsTrue(samIntroductionsResponse.Content.Any(intro => intro.Identity == merry));


        await merryOwnerClient.Connections.SendConnectionRequest(sam);
        await samOwnerClient.Connections.AcceptConnectionRequest(merry);

        // there should now be no introductions

        var merryIntroductionsResponse2 = await merryOwnerClient.Connections.GetReceivedIntroductions();
        Assert.IsTrue(merryIntroductionsResponse2.IsSuccessStatusCode);
        Assert.IsFalse(merryIntroductionsResponse2.Content.Any(intro => intro.Identity == sam));

        var samIntroductionsResponse2 = await samOwnerClient.Connections.GetReceivedIntroductions();
        Assert.IsTrue(samIntroductionsResponse2.IsSuccessStatusCode);
        Assert.IsFalse(samIntroductionsResponse2.Content.Any(intro => intro.Identity == merry));

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

        await merry.Connections.DeleteConnectionRequestFrom(sam.Identity.OdinId);
        await merry.Connections.DeleteSentRequestTo(sam.Identity.OdinId);

        await sam.Connections.DeleteConnectionRequestFrom(merry.Identity.OdinId);
        await sam.Connections.DeleteSentRequestTo(merry.Identity.OdinId);

        await sam.Network.UnblockConnection(merry.Identity.OdinId);
        await merry.Network.UnblockConnection(sam.Identity.OdinId);
    }
}