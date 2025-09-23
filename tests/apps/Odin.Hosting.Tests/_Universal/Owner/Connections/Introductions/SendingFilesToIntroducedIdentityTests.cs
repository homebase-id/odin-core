using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Odin.Hosting.Tests._Universal.Owner.Connections.Introductions;

public class SendingFilesToIntroducedIdentityTests
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
    public async Task CanSendFilesOnChatDriveToIntroducedIdentity()
    {
        var frodo = TestIdentities.Frodo.OdinId;
        var sam = TestIdentities.Samwise.OdinId;
        var merry = TestIdentities.Merry.OdinId;

        var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var merryOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);
        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await Prepare();

        var response = await frodoOwnerClient.Connections.SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam, merry]
        });

        var introResult = response.Content;
        ClassicAssert.IsTrue(introResult.RecipientStatus[sam]);
        ClassicAssert.IsTrue(introResult.RecipientStatus[merry]);

        await frodoOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);

        // There are background processes running which will send introductions automatically
        // we can also call an endpoint to force this.
        // since we don't know when this will occur, we'll call the endpoint

        // there's also logic dictating that when sending a connection request due to an
        // introduction, we do not send it if there's already an incoming request

        // so - we have to add some logic into this test

        await samOwnerClient.Connections.AwaitIntroductionsProcessing();
        await merryOwnerClient.Connections.AwaitIntroductionsProcessing();

        //
        // Act - now that merry and sam are introduced; send a file from sam to merry
        // 
        var targetDrive = SystemDriveConstants.ChatDrive;
        var fileMetadata = SampleMetadataData.CreateWithContent(fileType: 333, "sam says hi to merry", AccessControlList.Connected);
        fileMetadata.AllowDistribution = true;

        var storage = new StorageOptions
        {
            Drive = targetDrive
        };
        var transitOptions = new TransitOptions
        {
            Recipients = [merry],
            Priority = OutboxPriority.High
        };

        var (sendFileToMerryResponse, encryptedJsonContent64) = await samOwnerClient.DriveRedux
            .UploadNewEncryptedMetadata(fileMetadata, storage, transitOptions);

        //
        // Assert - sam should have sent the file and merry should have it
        //
        ClassicAssert.IsTrue(sendFileToMerryResponse.IsSuccessStatusCode);
        var samUploadResult = sendFileToMerryResponse.Content;
        ClassicAssert.IsTrue(samUploadResult.RecipientStatus[merry] == TransferStatus.Enqueued);

        await samOwnerClient.DriveRedux.WaitForEmptyOutbox(targetDrive);

        await merryOwnerClient.DriveRedux.ProcessInbox(targetDrive);
        var getFileOnMerryResponse = await merryOwnerClient.DriveRedux
            .QueryByGlobalTransitId(samUploadResult.GlobalTransitIdFileIdentifier);

        ClassicAssert.IsTrue(getFileOnMerryResponse.IsSuccessStatusCode);
        var fileOnMerry = getFileOnMerryResponse.Content.SearchResults.SingleOrDefault();
        ClassicAssert.IsNotNull(fileOnMerry);

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