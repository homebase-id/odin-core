using System;
using System.Collections;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Hosting.Tests._Universal.Owner.Connections.Introductions.AutoAcceptVariations;

public class IntroductionTestsAutoAcceptEnabledOnAllIdentities
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
    public async Task WillAutoAcceptWhenIdentitiesAreNotConnected(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var debugTimeout = TimeSpan.FromMinutes(60);

        var sam = TestIdentities.Samwise.OdinId;
        var merry = TestIdentities.Merry.OdinId;

        var introducer = await IntroductionTestUtils.PrepareIntroducer(_scaffold);

        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merryOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        //
        // Note: Sam and Merry are not connected 
        //
        await callerContext.Initialize(introducer);
        var client = new UniversalCircleNetworkRequestsApiClient(introducer.OdinId, callerContext.GetFactory());
        var response = await client.SendIntroductions(new IntroductionGroup
            // var response = await frodoOwnerClient.Connections.SendIntroductions(new IntroductionGroup
            {
                Message = "test message from frodo",
                Recipients = [sam, merry]
            });

        Assert.IsTrue(response.IsSuccessStatusCode, $"failed: status code was: {response.StatusCode}");
        await introducer.Connections.AwaitIntroductionsProcessing(debugTimeout);

        var introResult = response.Content;
        Assert.IsTrue(introResult.RecipientStatus[sam]);
        Assert.IsTrue(introResult.RecipientStatus[merry]);

        await samOwnerClient.Connections.AwaitIntroductionsProcessing(debugTimeout);
        await merryOwnerClient.Connections.AwaitIntroductionsProcessing(debugTimeout);

        //
        // Validate Sam is connected on merry's identity
        //
        Assert.IsTrue(
            await IntroductionTestUtils.IsConnectedWithExpectedOrigin(merryOwnerClient, sam, ConnectionRequestOrigin.Introduction));
        Assert.IsFalse(await IntroductionTestUtils.HasIntroductionFromIdentity(merryOwnerClient, sam),
            "there should be no introductions to sam");

        //
        // Validate Merry is connected on Sam's identity
        //
        Assert.IsTrue(
            await IntroductionTestUtils.IsConnectedWithExpectedOrigin(samOwnerClient, merry, ConnectionRequestOrigin.Introduction));
        Assert.IsFalse(await IntroductionTestUtils.HasIntroductionFromIdentity(samOwnerClient, merry),
            "there should be no introductions to merry");

        await IntroductionTestUtils.Cleanup(_scaffold);
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    public async Task WillNotSendOrReceiveConnectionRequestWhenOneIntroduceeBlocksAnother(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var debugTimeout = TimeSpan.FromMinutes(60);

        var sam = TestIdentities.Samwise.OdinId;
        var merry = TestIdentities.Merry.OdinId;

        var introducer = await IntroductionTestUtils.PrepareIntroducer(_scaffold);

        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merryOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        //
        // Setup: sam blocks merry
        //
        await samOwnerClient.Network.BlockConnection(merry);

        await callerContext.Initialize(introducer);
        var client = new UniversalCircleNetworkRequestsApiClient(introducer.OdinId, callerContext.GetFactory());
        var response = await client.SendIntroductions(new IntroductionGroup
            // var response = await frodoOwnerClient.Connections.SendIntroductions(new IntroductionGroup
            {
                Message = "test message from frodo",
                Recipients = [sam, merry]
            });

        Assert.IsTrue(response.IsSuccessStatusCode, $"failed: status code was: {response.StatusCode}");
        await introducer.Connections.AwaitIntroductionsProcessing(debugTimeout);

        var introResult = response.Content;
        Assert.IsTrue(introResult.RecipientStatus[sam]);
        Assert.IsTrue(introResult.RecipientStatus[merry]);

        await samOwnerClient.Connections.AwaitIntroductionsProcessing(debugTimeout);
        await merryOwnerClient.Connections.AwaitIntroductionsProcessing(debugTimeout);

        // Assert: Sam does not have a connection from Merry
        Assert.IsFalse(await IntroductionTestUtils.HasReceivedIntroducedConnectionRequestFromIntroducee(samOwnerClient, merry));

        // Assert: Merry does not have a connection from Sam
        Assert.IsFalse(await IntroductionTestUtils.HasReceivedIntroducedConnectionRequestFromIntroducee(merryOwnerClient, sam));

        // Assert: Sam does not have an introduction
        Assert.IsFalse(await IntroductionTestUtils.HasIntroductionFromIdentity(samOwnerClient, merry));

        // Assert: Merry has an introduction
        Assert.IsTrue(await IntroductionTestUtils.HasIntroductionFromIdentity(merryOwnerClient, sam));

        await IntroductionTestUtils.Cleanup(_scaffold);
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    public async Task WillHandleWhenIntroduceesAreAlreadyConnectedAndVerifiable(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var debugTimeout = TimeSpan.FromMinutes(60);

        var sam = TestIdentities.Samwise.OdinId;
        var merry = TestIdentities.Merry.OdinId;

        var introducer = await IntroductionTestUtils.PrepareIntroducer(_scaffold);

        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merryOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        //
        // Setup: Sam and merry are connected
        //
        await samOwnerClient.Connections.SendConnectionRequest(merry);
        await merryOwnerClient.Connections.AcceptConnectionRequest(sam);

        await callerContext.Initialize(introducer);
        var client = new UniversalCircleNetworkRequestsApiClient(introducer.OdinId, callerContext.GetFactory());
        var response = await client.SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam, merry]
        });

        Assert.IsTrue(response.IsSuccessStatusCode, $"failed: status code was: {response.StatusCode}");
        await introducer.Connections.AwaitIntroductionsProcessing(debugTimeout);

        var introResult = response.Content;
        Assert.IsTrue(introResult.RecipientStatus[sam]);
        Assert.IsTrue(introResult.RecipientStatus[merry]);

        await samOwnerClient.Connections.AwaitIntroductionsProcessing(debugTimeout);
        await merryOwnerClient.Connections.AwaitIntroductionsProcessing(debugTimeout);

        //
        // Assert: sam should have merry has a normal connection
        //
        Assert.IsTrue(await IntroductionTestUtils.IsConnectedWithExpectedOrigin(
            samOwnerClient, merry, ConnectionRequestOrigin.IdentityOwner));

        //
        // Assert: merry should have sam as a normal connection
        //
        Assert.IsTrue(await IntroductionTestUtils.IsConnectedWithExpectedOrigin(
            merryOwnerClient, sam, ConnectionRequestOrigin.IdentityOwner));

        //
        // Assert: Sam does not have an introduction
        //
        Assert.IsFalse(await IntroductionTestUtils.HasIntroductionFromIdentity(samOwnerClient, merry));

        //
        // Assert: Merry does not have an introduction
        //
        Assert.IsFalse(await IntroductionTestUtils.HasIntroductionFromIdentity(merryOwnerClient, sam));

        await IntroductionTestUtils.Cleanup(_scaffold);
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    public async Task WillHandleWithAllIntroduceesBlockEachOther(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var debugTimeout = TimeSpan.FromMinutes(60);

        var sam = TestIdentities.Samwise.OdinId;
        var merry = TestIdentities.Merry.OdinId;

        var introducer = await IntroductionTestUtils.PrepareIntroducer(_scaffold);

        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merryOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        //
        // Setup: Sam blocks merry and merry blocks sam
        //
        await samOwnerClient.Network.BlockConnection(merry);
        await merryOwnerClient.Network.BlockConnection(sam);

        await callerContext.Initialize(introducer);
        var client = new UniversalCircleNetworkRequestsApiClient(introducer.OdinId, callerContext.GetFactory());
        var response = await client.SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam, merry]
        });

        Assert.IsTrue(response.IsSuccessStatusCode, $"failed: status code was: {response.StatusCode}");
        await introducer.Connections.AwaitIntroductionsProcessing(debugTimeout);

        var introResult = response.Content;
        Assert.IsTrue(introResult.RecipientStatus[sam]);
        Assert.IsTrue(introResult.RecipientStatus[merry]);

        await samOwnerClient.Connections.AwaitIntroductionsProcessing(debugTimeout);
        await merryOwnerClient.Connections.AwaitIntroductionsProcessing(debugTimeout);

        //
        // Assert: sam has no connection request
        //
        Assert.IsFalse(await IntroductionTestUtils.HasReceivedIntroducedConnectionRequestFromIntroducee(samOwnerClient, merry));

        //
        // Assert: merry has no connection request
        //
        Assert.IsFalse(await IntroductionTestUtils.HasReceivedIntroducedConnectionRequestFromIntroducee(merryOwnerClient, sam));

        //
        // Assert: sam should have merry has a normal connection
        //
        Assert.IsFalse(await IntroductionTestUtils.IsConnected(samOwnerClient, merry));

        //
        // Assert: merry should have sam as a normal connection
        //
        Assert.IsFalse(await IntroductionTestUtils.IsConnected(merryOwnerClient, sam));

        //
        // Assert: Sam does not have an introduction
        //
        Assert.IsFalse(await IntroductionTestUtils.HasIntroductionFromIdentity(samOwnerClient, merry));

        //
        // Assert: Merry does not have an introduction
        //
        Assert.IsFalse(await IntroductionTestUtils.HasIntroductionFromIdentity(merryOwnerClient, sam));

        await IntroductionTestUtils.Cleanup(_scaffold);
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    public async Task WillHandleWhenAllWhenConnectionsFailsVerification(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var debugTimeout = TimeSpan.FromMinutes(60);

        var sam = TestIdentities.Samwise.OdinId;
        var merry = TestIdentities.Merry.OdinId;

        var introducer = await IntroductionTestUtils.PrepareIntroducer(_scaffold);

        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merryOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        //
        // Setup: merry disconnects from sam, but sam stays connected
        //
        await samOwnerClient.Connections.SendConnectionRequest(merry);
        await merryOwnerClient.Connections.AcceptConnectionRequest(sam);
        await merryOwnerClient.Connections.DisconnectFrom(sam);

        await callerContext.Initialize(introducer);
        var client = new UniversalCircleNetworkRequestsApiClient(introducer.OdinId, callerContext.GetFactory());
        var response = await client.SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam, merry]
        });

        Assert.IsTrue(response.IsSuccessStatusCode, $"failed: status code was: {response.StatusCode}");
        await introducer.Connections.AwaitIntroductionsProcessing(debugTimeout);

        var introResult = response.Content;
        Assert.IsTrue(introResult.RecipientStatus[sam]);
        Assert.IsTrue(introResult.RecipientStatus[merry]);

        await samOwnerClient.Connections.AwaitIntroductionsProcessing(debugTimeout);
        await merryOwnerClient.Connections.AwaitIntroductionsProcessing(debugTimeout);
        
        // both send connection request; identities get connected
        // R3's ICR record on R2's identity must be reset to an auto-connection because we won't
        // have master key and the shared secret get reset.  This means all circles also get reset.  "
        
        Assert.IsTrue(await IntroductionTestUtils.IsConnectedWithExpectedOrigin(samOwnerClient, merry, ConnectionRequestOrigin.Introduction));
     
        Assert.IsTrue(await IntroductionTestUtils.IsConnectedWithExpectedOrigin(merryOwnerClient, sam, ConnectionRequestOrigin.Introduction));

        await IntroductionTestUtils.Cleanup(_scaffold);
        
    }
    
}