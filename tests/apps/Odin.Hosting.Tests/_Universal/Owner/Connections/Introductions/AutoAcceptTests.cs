using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

namespace Odin.Hosting.Tests._Universal.Owner.Connections.Introductions;

public class AutoAcceptTests
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

        var debugTimeout = _scaffold.DebugTimeout;
        
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

        ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"failed: status code was: {response.StatusCode}");
        await frodoOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive, debugTimeout);

        var introResult = response.Content;
        ClassicAssert.IsTrue(introResult.RecipientStatus[sam]);
        ClassicAssert.IsTrue(introResult.RecipientStatus[merry]);

       
        await samOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive, debugTimeout);
        await merryOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive, debugTimeout);
        
        // Assert: Sam should have a connection request from Merry and visa/versa
        await callerContext.Initialize(samOwnerClient);
        var samClient = new UniversalCircleNetworkRequestsApiClient(sam, callerContext.GetFactory());
        var samProcessResponse = await samClient.ProcessIncomingIntroductions();
        ClassicAssert.IsTrue(samProcessResponse.IsSuccessStatusCode);

        // var outgoingRequestToSamResponse = await merryOwnerClient.Connections.GetOutgoingSentRequestTo(sam);
        // var outgoingRequestToSam = outgoingRequestToSamResponse.Content;
        // ClassicAssert.IsNotNull(outgoingRequestToSam);
        // ClassicAssert.IsTrue(outgoingRequestToSam.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction);
        // ClassicAssert.IsTrue(outgoingRequestToSam.IntroducerOdinId == frodo);

        var outgoingRequestToMerryResponse = await samOwnerClient.Connections.GetOutgoingSentRequestTo(merry);
        var outgoingRequestToMerry = outgoingRequestToMerryResponse.Content;
        ClassicAssert.IsNotNull(outgoingRequestToMerry);
        ClassicAssert.IsTrue(outgoingRequestToMerry.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction);
        ClassicAssert.IsTrue(outgoingRequestToMerry.IntroducerOdinId == frodo);

        var merryRequestFromSamResponse = await merryOwnerClient.Connections.GetIncomingRequestFrom(sam);
        var requestFromSam = merryRequestFromSamResponse.Content;
        ClassicAssert.IsNotNull(requestFromSam, "there should be a request from sam since we have not yet processed the inbox");

        // Note: remember there is a background process that is auto-accepting eligible connections so this call might not run the auto-accept code
        var merryProcessResponse = await merryOwnerClient.Connections.ProcessIncomingIntroductions();
        ClassicAssert.IsTrue(merryProcessResponse.IsSuccessStatusCode);

        var merryForceAutoAccept = await merryOwnerClient.Connections.AutoAcceptEligibleIntroductions();
        ClassicAssert.IsTrue(merryForceAutoAccept.IsSuccessStatusCode);

        var getSamConnectionInfoResponse = await merryOwnerClient.Network.GetConnectionInfo(sam);
        ClassicAssert.IsTrue(getSamConnectionInfoResponse.IsSuccessStatusCode);
        ClassicAssert.IsTrue(getSamConnectionInfoResponse.Content.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction);
        ClassicAssert.IsTrue(getSamConnectionInfoResponse.Content.Status == ConnectionStatus.Connected);

        ClassicAssert.IsTrue(
            getSamConnectionInfoResponse.Content.AccessGrant.CircleGrants.Exists(cg =>
                cg.CircleId == SystemCircleConstants.AutoConnectionsCircleId));
        ClassicAssert.IsFalse(getSamConnectionInfoResponse.Content.AccessGrant.CircleGrants.Exists(cg =>
            cg.CircleId == SystemCircleConstants.ConfirmedConnectionsCircleId));

        var merryIntroductionsResponse = await merryOwnerClient.Connections.GetReceivedIntroductions();
        ClassicAssert.IsTrue(merryIntroductionsResponse.IsSuccessStatusCode);
        ClassicAssert.IsTrue(merryIntroductionsResponse.Content.All(intro => intro.Identity != sam), "there should be no introductions to sam");

        // Check Sam

        var samForceAutoAccept = await samOwnerClient.Connections.AutoAcceptEligibleIntroductions();
        ClassicAssert.IsTrue(samForceAutoAccept.IsSuccessStatusCode);

        var getMerryConnectionInfoResponse = await samOwnerClient.Network.GetConnectionInfo(merry);
        ClassicAssert.IsTrue(getMerryConnectionInfoResponse.IsSuccessStatusCode);
        ClassicAssert.IsTrue(getMerryConnectionInfoResponse.Content.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction,
            $"{getMerryConnectionInfoResponse.Content.ConnectionRequestOrigin}");
        ClassicAssert.IsTrue(getMerryConnectionInfoResponse.Content.Status == ConnectionStatus.Connected);

        ClassicAssert.IsTrue(
            getMerryConnectionInfoResponse.Content.AccessGrant.CircleGrants.Exists(cg =>
                cg.CircleId == SystemCircleConstants.AutoConnectionsCircleId));
        ClassicAssert.IsFalse(getMerryConnectionInfoResponse.Content.AccessGrant.CircleGrants.Exists(cg =>
            cg.CircleId == SystemCircleConstants.ConfirmedConnectionsCircleId));

        var samIntroductionsResponse = await samOwnerClient.Connections.GetReceivedIntroductions();
        ClassicAssert.IsTrue(samIntroductionsResponse.IsSuccessStatusCode);
        ClassicAssert.IsTrue(samIntroductionsResponse.Content.All(intro => intro.Identity != merry), "there should be no introductions to sam");


        await Cleanup();
    }

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
        // ClassicAssert.IsFalse(introResult.RecipientStatus[TestIdentities.Samwise.OdinId],
        //     "sam should reject since frodo does not have allow introductions permission");
        ClassicAssert.IsTrue(introResult.RecipientStatus[TestIdentities.Merry.OdinId]);
        
        // Note; I have to use a delay because the outbox will never be
        // empty and, currently, there is no way to do an exclusion test on the outbox 
        // await frodoOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);
        await Task.Delay(1000 * 3);

        var samOutboxItem =
            await frodoOwnerClient.DriveRedux.GetOutboxItem(SystemDriveConstants.TransientTempDrive,
                TestIdentities.Samwise.OdinId.ToHashId(), TestIdentities.Samwise.OdinId);
        ClassicAssert.IsNotNull(samOutboxItem, "there should be an outbox item for sam since it failed he blocked incoming introductions");
        
        await samOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);
        await merryOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);
        
        // ensure introductions are processed
        var samProcessResponse = await samOwnerClient.Connections.ProcessIncomingIntroductions();
        ClassicAssert.IsTrue(samProcessResponse.IsSuccessStatusCode);

        var merryProcessResponse = await merryOwnerClient.Connections.ProcessIncomingIntroductions();
        ClassicAssert.IsTrue(merryProcessResponse.IsSuccessStatusCode);

        // Sam should get a connection request from merry (via frodo)
        var incomingRequestFromMerryResponse = await samOwnerClient.Connections.GetIncomingRequestFrom(TestIdentities.Merry.OdinId);
        ClassicAssert.IsTrue(incomingRequestFromMerryResponse.IsSuccessStatusCode);
        ClassicAssert.IsTrue(incomingRequestFromMerryResponse.Content.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction);

        var outgoingRequestToMerryResponse = await samOwnerClient.Connections.GetOutgoingSentRequestTo(TestIdentities.Merry.OdinId);
        ClassicAssert.IsTrue(outgoingRequestToMerryResponse.StatusCode == HttpStatusCode.NotFound, "sam should not have sent a request");

        var merryRequestFromSamResponse = await merryOwnerClient.Connections.GetIncomingRequestFrom(TestIdentities.Samwise.OdinId);
        ClassicAssert.IsTrue(merryRequestFromSamResponse.StatusCode == HttpStatusCode.NotFound, "sam should not have sent a request");

        var getSamConnectionInfoResponse = await merryOwnerClient.Network.GetConnectionInfo(TestIdentities.Samwise.OdinId);
        ClassicAssert.IsTrue(getSamConnectionInfoResponse.IsSuccessStatusCode);
        ClassicAssert.IsTrue(getSamConnectionInfoResponse.Content.Status == ConnectionStatus.None, "sam should not be connected to merry");

        var getMerryConnectionInfoResponse = await samOwnerClient.Network.GetConnectionInfo(TestIdentities.Merry.OdinId);
        ClassicAssert.IsTrue(getMerryConnectionInfoResponse.IsSuccessStatusCode);
        ClassicAssert.IsTrue(getMerryConnectionInfoResponse.Content.Status == ConnectionStatus.None, "merry should not be connected to sam");

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