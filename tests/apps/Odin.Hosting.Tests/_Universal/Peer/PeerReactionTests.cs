using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.Tests._Universal.Peer;

public class PeerGroupReactionTests
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
    public async Task CanSendReactionsUsingEncryptedFileEvenWhenTargetFileIsInInbox()
    {
        var pippinOwnerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);
        var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var targetDrive = SystemDriveConstants.ChatDrive;

        await pippinOwnerApiClient.Connections.SendConnectionRequest(TestIdentities.Frodo.OdinId);
        await frodoOwnerClient.Connections.AcceptConnectionRequest(TestIdentities.Pippin.OdinId);

        //
        // Send an encrypted file
        //
        var clientUniqueId = new ClientUniqueIdFileIdentifier()
        {
            ClientUniqueId = Guid.NewGuid(),
            TargetDrive = targetDrive
        };

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected, uniqueId: clientUniqueId.ClientUniqueId);
        uploadedFileMetadata.AllowDistribution = true;
        uploadedFileMetadata.AppData.Content = "ping ping pong";

        var transitOptions = new TransitOptions()
        {
            Recipients = [TestIdentities.Frodo.OdinId],
            UseGlobalTransitId = true,
            Schedule = ScheduleOptions.SendNowAwaitResponse,
        };

        var uploadMetadataResponse = await pippinOwnerApiClient.DriveRedux.UploadNewEncryptedMetadata(targetDrive, uploadedFileMetadata, transitOptions);
        var uploadResult = uploadMetadataResponse.response.Content;

        //
        // validate the files went to the inbox
        //
        Assert.IsTrue(uploadResult.RecipientStatus.TryGetValue(TestIdentities.Frodo.OdinId, out var transferStatus));
        Assert.IsTrue(transferStatus == TransferStatus.DeliveredToInbox);

        //
        // send the reactions before frodo has processed his inbox
        //
        const string reactionContent1 = ":cake:";
        var addGroupReactionResponse = await pippinOwnerApiClient.Reactions
            .AddGroupReaction(uploadResult.File, reactionContent1, [TestIdentities.Frodo.OdinId]);

        Assert.IsTrue(addGroupReactionResponse.IsSuccessStatusCode);
        foreach (var response in addGroupReactionResponse.Content.Responses)
        {
            Assert.IsTrue(response.Status == AddDeleteRemoteReactionStatusCode.Success, $"failed to add reaction for {response.Recipient}");
        }

        // At this point, the reactions should be stored in a queue somewhere but i have no way to test

        //
        // Process the inbox
        //
        var frodoOwnerClientOld = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        await frodoOwnerClientOld.Transit.ProcessInbox(targetDrive);


        // The reactions should be applied to the files 

        // All recipients must have the reaction AND the corresponding file must have the preview
        var getRecipientHeaderResponse = await frodoOwnerClient.DriveRedux.GetFileHeaderByUniqueId(clientUniqueId);
        Assert.IsTrue(getRecipientHeaderResponse.IsSuccessStatusCode, $"failed to get header for {TestIdentities.Frodo.OdinId}");
        Assert.IsNotNull(getRecipientHeaderResponse.Content.FileMetadata.ReactionPreview?.Reactions
                .SingleOrDefault(r => r.Value.ReactionContent == reactionContent1), $"reaction preview missing for {TestIdentities.Frodo.OdinId}");

        var frodosReactionsViaPippin =
            await pippinOwnerApiClient.PeerReactions.GetAllReactions(TestIdentities.Frodo.OdinId, uploadResult.GlobalTransitIdFileIdentifier);
        Assert.IsTrue(frodosReactionsViaPippin.IsSuccessStatusCode);
        Assert.IsNotNull(
            frodosReactionsViaPippin.Content.Reactions.SingleOrDefault(r =>
                r.ReactionContent == reactionContent1 && r.GlobalTransitIdFileIdentifier == uploadResult.GlobalTransitIdFileIdentifier),
            "Frodo should have one reaction");
    }

    [Test]
    public async Task CanSendReactionsToMultipleIdentitiesUsingEncryptedFile()
    {
        var pippinOwnerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);
        var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        await pippinOwnerApiClient.Connections.SendConnectionRequest(TestIdentities.Frodo.OdinId);
        await frodoOwnerClient.Connections.AcceptConnectionRequest(TestIdentities.Pippin.OdinId);

        var targetDrive = SystemDriveConstants.ChatDrive;

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
        uploadedFileMetadata.AppData.Content = "ping ping pong";

        var transitOptions = new TransitOptions()
        {
            Recipients = [TestIdentities.Frodo.OdinId],
            UseGlobalTransitId = true,
            Schedule = ScheduleOptions.SendNowAwaitResponse,
        };

        var uploadMetadataResponse = await pippinOwnerApiClient.DriveRedux.UploadNewEncryptedMetadata(targetDrive, uploadedFileMetadata, transitOptions);
        var uploadResult = uploadMetadataResponse.response.Content;

        //process inboxes
        Assert.IsTrue(uploadResult.RecipientStatus.TryGetValue(TestIdentities.Frodo.OdinId, out var transferStatus));
        Assert.IsTrue(transferStatus == TransferStatus.DeliveredToInbox);

        await _scaffold.CreateOwnerApiClient(TestIdentities.Frodo).Transit.ProcessInbox(targetDrive); //todo: replace with redux client

        //all recipients should have the file by GlobalTransitId
        var request = new QueryBatchRequest
        {
            QueryParams = new FileQueryParams()
            {
                TargetDrive = targetDrive,
                GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitId.GetValueOrDefault() }
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest()
            {
                MaxRecords = 10,
                IncludeMetadataHeader = true
            }
        };

        var qbResponse = await frodoOwnerClient.DriveRedux.QueryBatch(request);
        Assert.IsTrue(qbResponse.IsSuccessStatusCode);
        var file = qbResponse.Content.SearchResults.SingleOrDefault();
        Assert.IsNotNull(file, $"File with global transitId not found on {TestIdentities.Frodo.OdinId}");

        const string reactionContent1 = ":cake:";
        var addGroupReactionResponse = await pippinOwnerApiClient.Reactions.AddGroupReaction(
            uploadResult.File,
            reactionContent1,
            [TestIdentities.Frodo.OdinId]);

        Assert.IsTrue(addGroupReactionResponse.IsSuccessStatusCode);
        foreach (var response in addGroupReactionResponse.Content.Responses)
        {
            Assert.IsTrue(response.Status == AddDeleteRemoteReactionStatusCode.Success, $"failed to add reaction for {response.Recipient}");
        }

        // The reactions should be applied to the files 

        // All recipients must have the reaction AND the corresponding file must have the preview
        var getRecipientHeaderResponse = await frodoOwnerClient.DriveRedux.GetFileHeaderByUniqueId(clientUniqueId);
        Assert.IsTrue(getRecipientHeaderResponse.IsSuccessStatusCode, $"failed to get header for {TestIdentities.Frodo.OdinId}");
        Assert.IsNotNull(getRecipientHeaderResponse.Content.FileMetadata.ReactionPreview?.Reactions
                .SingleOrDefault(r => r.Value.ReactionContent == reactionContent1), $"reaction preview missing for {TestIdentities.Frodo.OdinId}");

        var frodosReactionsViaPippin =
            await pippinOwnerApiClient.PeerReactions.GetAllReactions(TestIdentities.Frodo.OdinId, uploadResult.GlobalTransitIdFileIdentifier);
        Assert.IsTrue(frodosReactionsViaPippin.IsSuccessStatusCode);
        Assert.IsNotNull(
            frodosReactionsViaPippin.Content.Reactions.SingleOrDefault(r =>
                r.ReactionContent == reactionContent1 && r.GlobalTransitIdFileIdentifier == uploadResult.GlobalTransitIdFileIdentifier),
            "Frodo should have one reaction");
    }

    [Test]
    public async Task CanDeleteRemoteReactionsWhenFileIsDeletedOverTransit()
    {
        var pippinOwnerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);
        var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        await pippinOwnerApiClient.Connections.SendConnectionRequest(TestIdentities.Frodo.OdinId);
        await frodoOwnerClient.Connections.AcceptConnectionRequest(TestIdentities.Pippin.OdinId);

        var targetDrive = SystemDriveConstants.ChatDrive;

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
        uploadedFileMetadata.AppData.Content = "ping ping pong";

        var transitOptions = new TransitOptions()
        {
            Recipients = [TestIdentities.Frodo.OdinId],
            UseGlobalTransitId = true,
            Schedule = ScheduleOptions.SendNowAwaitResponse,
        };

        var uploadMetadataResponse = await pippinOwnerApiClient.DriveRedux.UploadNewEncryptedMetadata(targetDrive, uploadedFileMetadata, transitOptions);
        var uploadResult = uploadMetadataResponse.response.Content;

        //process inboxes
        Assert.IsTrue(uploadResult.RecipientStatus.TryGetValue(TestIdentities.Frodo.OdinId, out var transferStatus));
        Assert.IsTrue(transferStatus == TransferStatus.DeliveredToInbox);

        await _scaffold.CreateOwnerApiClient(TestIdentities.Frodo).Transit.ProcessInbox(targetDrive); //todo: replace with redux client

        //all recipients should have the file by GlobalTransitId
        var request = new QueryBatchRequest
        {
            QueryParams = new FileQueryParams()
            {
                TargetDrive = targetDrive,
                GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitId.GetValueOrDefault() }
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest()
            {
                MaxRecords = 10,
                IncludeMetadataHeader = true
            }
        };

        var qbResponse = await frodoOwnerClient.DriveRedux.QueryBatch(request);
        Assert.IsTrue(qbResponse.IsSuccessStatusCode);
        var file = qbResponse.Content.SearchResults.SingleOrDefault();
        Assert.IsNotNull(file, $"File with global transitId not found on {TestIdentities.Frodo.OdinId}");

        const string reactionContent1 = ":cake:";
        var addGroupReactionResponse = await pippinOwnerApiClient.Reactions.AddGroupReaction(
            uploadResult.File,
            reactionContent1,
            [TestIdentities.Frodo.OdinId]);

        Assert.IsTrue(addGroupReactionResponse.IsSuccessStatusCode);
        foreach (var response in addGroupReactionResponse.Content.Responses)
        {
            Assert.IsTrue(response.Status == AddDeleteRemoteReactionStatusCode.Success, $"failed to add reaction for {response.Recipient}");
        }

        // The reactions should be applied to the files 

        // All recipients must have the reaction AND the corresponding file must have the preview
        var getRecipientHeaderResponse = await frodoOwnerClient.DriveRedux.GetFileHeaderByUniqueId(clientUniqueId);
        Assert.IsTrue(getRecipientHeaderResponse.IsSuccessStatusCode, $"failed to get header for {TestIdentities.Frodo.OdinId}");
        Assert.IsNotNull(getRecipientHeaderResponse.Content.FileMetadata.ReactionPreview?.Reactions
                .SingleOrDefault(r => r.Value.ReactionContent == reactionContent1), $"reaction preview missing for {TestIdentities.Frodo.OdinId}");

        var frodosReactionsViaPippin =
            await pippinOwnerApiClient.PeerReactions.GetAllReactions(TestIdentities.Frodo.OdinId, uploadResult.GlobalTransitIdFileIdentifier);
        Assert.IsTrue(frodosReactionsViaPippin.IsSuccessStatusCode);
        Assert.IsNotNull(
            frodosReactionsViaPippin.Content.Reactions.SingleOrDefault(r =>
                r.ReactionContent == reactionContent1 && r.GlobalTransitIdFileIdentifier == uploadResult.GlobalTransitIdFileIdentifier),
            "Frodo should have one reaction");

        // Delete the file on frodo's
        var deleteFileResponse = await pippinOwnerApiClient.DriveRedux.DeleteFile(uploadResult.File, [TestIdentities.Frodo.OdinId]);
        Assert.IsTrue(deleteFileResponse.IsSuccessStatusCode);
        Assert.IsTrue(deleteFileResponse.Content.RecipientStatus[TestIdentities.Frodo.OdinId] == DeleteLinkedFileStatus.RequestAccepted);

        await _scaffold.CreateOwnerApiClient(TestIdentities.Frodo).Transit.ProcessInbox(targetDrive); //todo: replace with redux client

        // All recipients must have the reaction AND the corresponding file must have the preview
        var getRecipientHeaderResponse2 = await frodoOwnerClient.DriveRedux.GetFileHeaderByUniqueId(clientUniqueId);
        Assert.IsTrue(getRecipientHeaderResponse2.StatusCode == HttpStatusCode.NotFound, $"Header should not exist on {TestIdentities.Frodo.OdinId}");

        var frodosReactionsViaPippin2 =
            await pippinOwnerApiClient.PeerReactions.GetAllReactions(TestIdentities.Frodo.OdinId, uploadResult.GlobalTransitIdFileIdentifier);
        Assert.IsTrue(frodosReactionsViaPippin2.IsSuccessStatusCode);
        Assert.IsNull(
            frodosReactionsViaPippin2.Content.Reactions.SingleOrDefault(r =>
                r.ReactionContent == reactionContent1 && r.GlobalTransitIdFileIdentifier == uploadResult.GlobalTransitIdFileIdentifier),
            "Frodo should not have the reaction");
    }

    
    // [Test]
    // public async Task CanSendMultipleReactionsUsingEncryptedFileEvenWhenTargetFileIsInInbox()
    // {
    //     
    // }
}