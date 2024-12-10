using System.Collections;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Hosting.Tests._Universal.Owner.Connections.Introductions;

public class AutoAcceptTests
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

    public static IEnumerable OwnerAllowed()
    {
        yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    public static IEnumerable AppAllowed()
    {
        yield return new object[]
            { new AppPermissionsKeysOnly(new TestPermissionKeyList(PermissionKeys.All.ToArray())), HttpStatusCode.OK };
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    public async Task CanAutoAcceptIncomingConnectionRequestsWhenIntroductionExists(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        // Note: for your sanity, remember this is a background process that is
        // automatically accepting introductions that are eligible

        var frodo = TestIdentities.Frodo.OdinId;
        var sam = TestIdentities.Samwise.OdinId;
        var merry = TestIdentities.Merry.OdinId;

        var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merryOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        await samOwnerClient.Configuration.DisableAutoAcceptIntroductions(true);
        await merryOwnerClient.Configuration.DisableAutoAcceptIntroductions(true);

        await Prepare();

        var response = await frodoOwnerClient.Connections.SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam, merry]
        });

        Assert.IsTrue(response.IsSuccessStatusCode, $"failed: status code was: {response.StatusCode}");
        await frodoOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);

        var introResult = response.Content;
        Assert.IsTrue(introResult.RecipientStatus[sam]);
        Assert.IsTrue(introResult.RecipientStatus[merry]);

       
        await samOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);
        await merryOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);
        
        // Assert: Sam should have a connection request from Merry and visa/versa
        await callerContext.Initialize(samOwnerClient);
        var samClient = new UniversalCircleNetworkRequestsApiClient(sam, callerContext.GetFactory());
        var samProcessResponse = await samClient.ProcessIncomingIntroductions();
        Assert.IsTrue(samProcessResponse.IsSuccessStatusCode);

        // var outgoingRequestToSamResponse = await merryOwnerClient.Connections.GetOutgoingSentRequestTo(sam);
        // var outgoingRequestToSam = outgoingRequestToSamResponse.Content;
        // Assert.IsNotNull(outgoingRequestToSam);
        // Assert.IsTrue(outgoingRequestToSam.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction);
        // Assert.IsTrue(outgoingRequestToSam.IntroducerOdinId == frodo);

        var outgoingRequestToMerryResponse = await samOwnerClient.Connections.GetOutgoingSentRequestTo(merry);
        var outgoingRequestToMerry = outgoingRequestToMerryResponse.Content;
        Assert.IsNotNull(outgoingRequestToMerry);
        Assert.IsTrue(outgoingRequestToMerry.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction);
        Assert.IsTrue(outgoingRequestToMerry.IntroducerOdinId == frodo);

        var merryRequestFromSamResponse = await merryOwnerClient.Connections.GetIncomingRequestFrom(sam);
        var requestFromSam = merryRequestFromSamResponse.Content;
        Assert.IsNotNull(requestFromSam, "there should be a request from sam since we have not yet processed the inbox");

        // Note: remember there is a background process that is auto-accepting eligible connections so this call might not run the auto-accept code
        var merryProcessResponse = await merryOwnerClient.Connections.ProcessIncomingIntroductions();
        Assert.IsTrue(merryProcessResponse.IsSuccessStatusCode);

        var merryForceAutoAccept = await merryOwnerClient.Connections.AutoAcceptEligibleIntroductions();
        Assert.IsTrue(merryForceAutoAccept.IsSuccessStatusCode);

        var getSamConnectionInfoResponse = await merryOwnerClient.Network.GetConnectionInfo(sam);
        Assert.IsTrue(getSamConnectionInfoResponse.IsSuccessStatusCode);
        Assert.IsTrue(getSamConnectionInfoResponse.Content.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction);
        Assert.IsTrue(getSamConnectionInfoResponse.Content.Status == ConnectionStatus.Connected);

        Assert.IsTrue(
            getSamConnectionInfoResponse.Content.AccessGrant.CircleGrants.Exists(cg =>
                cg.CircleId == SystemCircleConstants.AutoConnectionsCircleId));
        Assert.IsFalse(getSamConnectionInfoResponse.Content.AccessGrant.CircleGrants.Exists(cg =>
            cg.CircleId == SystemCircleConstants.ConfirmedConnectionsCircleId));

        var merryIntroductionsResponse = await merryOwnerClient.Connections.GetReceivedIntroductions();
        Assert.IsTrue(merryIntroductionsResponse.IsSuccessStatusCode);
        Assert.IsTrue(merryIntroductionsResponse.Content.All(intro => intro.Identity != sam), "there should be no introductions to sam");

        // Check Sam

        var samForceAutoAccept = await samOwnerClient.Connections.AutoAcceptEligibleIntroductions();
        Assert.IsTrue(samForceAutoAccept.IsSuccessStatusCode);

        var getMerryConnectionInfoResponse = await samOwnerClient.Network.GetConnectionInfo(merry);
        Assert.IsTrue(getMerryConnectionInfoResponse.IsSuccessStatusCode);
        Assert.IsTrue(getMerryConnectionInfoResponse.Content.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction,
            $"{getMerryConnectionInfoResponse.Content.ConnectionRequestOrigin}");
        Assert.IsTrue(getMerryConnectionInfoResponse.Content.Status == ConnectionStatus.Connected);

        Assert.IsTrue(
            getMerryConnectionInfoResponse.Content.AccessGrant.CircleGrants.Exists(cg =>
                cg.CircleId == SystemCircleConstants.AutoConnectionsCircleId));
        Assert.IsFalse(getMerryConnectionInfoResponse.Content.AccessGrant.CircleGrants.Exists(cg =>
            cg.CircleId == SystemCircleConstants.ConfirmedConnectionsCircleId));

        var samIntroductionsResponse = await samOwnerClient.Connections.GetReceivedIntroductions();
        Assert.IsTrue(samIntroductionsResponse.IsSuccessStatusCode);
        Assert.IsTrue(samIntroductionsResponse.Content.All(intro => intro.Identity != merry), "there should be no introductions to sam");


        await Cleanup();
    }

    // [Test]
    // public Task WillAutoAcceptIncomingConnectionRequestsWhenOnlyOneRecipientIsConnected()
    // {
    // }

    [Test]
    public async Task WillNotAutoAcceptWhenIntroducerDoesNotHaveAllowIntroductionsPermission()
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
        await frodoOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);

        await samOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);
        await merryOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);
        
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