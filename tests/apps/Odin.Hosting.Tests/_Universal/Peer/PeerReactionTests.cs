using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._Universal.Peer;

public class PeerReactionTests
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

    [Test]
    public async Task CanSendReactionsToMultipleIdentities()
    {
        var pippinOwnerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);
        var merryOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);
        var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var targetDrive = SystemDriveConstants.ChatDrive;

        var chatGroupRecipients = new List<OdinId>()
        {
            TestIdentities.Frodo.OdinId,
            TestIdentities.Merry.OdinId
        };

        //
        // errr-body connected
        //
        foreach (var recipient in chatGroupRecipients)
        {
            await pippinOwnerApiClient.Connections.SendConnectionRequest(recipient);
            var client = _scaffold.CreateOwnerApiClientRedux(TestIdentities.All[recipient]);
            await client.Connections.AcceptConnectionRequest(TestIdentities.Pippin.OdinId);
        }

        await merryOwnerClient.Connections.SendConnectionRequest(TestIdentities.Frodo.OdinId);
        await frodoOwnerClient.Connections.AcceptConnectionRequest(TestIdentities.Merry.OdinId);

        //
        // Send a file
        //
        var clientUniqueId = new ClientUniqueIdFileIdentifier()
        {
            ClientUniqueId = Guid.NewGuid(),
            TargetDrive = targetDrive
        };

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected, uniqueId: clientUniqueId.ClientUniqueId);
        uploadedFileMetadata.AllowDistribution = true;
        var transitOptions = new TransitOptions()
        {
            Recipients = chatGroupRecipients.Select(r => (string)r).ToList(),
            UseGlobalTransitId = true,
            Schedule = ScheduleOptions.SendNowAwaitResponse,
        };

        var uploadMetadataResponse = await pippinOwnerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata, transitOptions);
        var uploadResult = uploadMetadataResponse.Content;

        //process inboxes
        foreach (var recipient in chatGroupRecipients)
        {
            Assert.IsTrue(uploadResult.RecipientStatus.TryGetValue(recipient, out var transferStatus));
            Assert.IsTrue(transferStatus == TransferStatus.DeliveredToTargetDrive);
            
            var client = _scaffold.CreateOwnerApiClient(TestIdentities.All[recipient]);
            await client.Transit.ProcessInbox(targetDrive);
        }
        
        const string reactionContent1 = ":cake:";
        var addGroupReactionResponse =
            await pippinOwnerApiClient.PeerReactions.AddGroupReaction(chatGroupRecipients, uploadResult.GlobalTransitIdFileIdentifier, reactionContent1);
        Assert.IsTrue(addGroupReactionResponse.IsSuccessStatusCode);

        // all recipients must have the reaction AND the corresponding file must have the preview
        foreach (var recipient in chatGroupRecipients)
        {
            var client = _scaffold.CreateOwnerApiClientRedux(TestIdentities.All[recipient]);
            var getRecipientHeaderResponse = await client.DriveRedux.GetFileHeaderByUniqueId(clientUniqueId);
            Assert.IsTrue(getRecipientHeaderResponse.IsSuccessStatusCode, $"failed to get header for {recipient}");
            Assert.IsNotNull(getRecipientHeaderResponse.Content.FileMetadata.ReactionPreview.Reactions
                    .SingleOrDefault(r => r.Value.ReactionContent == reactionContent1), $"reaction preview missing for {recipient}");
        }


        // Validate the reaction is there (get file)
        var getHeaderResponse1 = await pippinOwnerApiClient.DriveRedux.GetFileHeader(uploadResult.File);
        Assert.IsNotNull(getHeaderResponse1.Content.FileMetadata.ReactionPreview.Reactions
            .SingleOrDefault(pair => pair.Value.ReactionContent == reactionContent1));

        // update the same file
        uploadedFileMetadata.AppData.Content = "changed data";
        var updateResponse =
            await pippinOwnerApiClient.DriveRedux.UpdateExistingMetadata(uploadResult.File, getHeaderResponse1.Content.FileMetadata.VersionTag,
                uploadedFileMetadata);
        Assert.IsTrue(updateResponse.IsSuccessStatusCode);

        // Validate the reaction is there (get file)
        var getHeaderResponse2 = await pippinOwnerApiClient.DriveRedux.GetFileHeader(uploadResult.File);
        Assert.IsTrue(getHeaderResponse2.Content.FileMetadata.AppData.Content == "changed data");
        Assert.IsNotNull(
            getHeaderResponse2.Content.FileMetadata.ReactionPreview.Reactions
                .SingleOrDefault(pair => pair.Value.ReactionContent == reactionContent1));
    }
}