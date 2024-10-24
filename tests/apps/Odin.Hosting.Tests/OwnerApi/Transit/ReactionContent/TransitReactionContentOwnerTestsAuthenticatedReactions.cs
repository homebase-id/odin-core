using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
using Odin.Core.Storage;
using Odin.Hosting.Tests.OwnerApi.ApiClient;

namespace Odin.Hosting.Tests.OwnerApi.Transit.ReactionContent;

public class TransitReactionContentOwnerTestsAuthenticatedReactions
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
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
    public async Task WhenOwnerFollows_AnonymousDrive_OwnerCanSendReactionAndDelete_FromFeed()
    {
        //Scenario: when sam follows Pippin, content shows in Sam's feed from Pippin
        //Sam can react and delete the reaction because he follows and can send over transit

        //Note: not sure if that's a good thing BUT technically sam is authenticated

        const string reactionContent = ":cake:";

        var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
        var pippinChannelDrive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await pippinOwnerClient.Drive.CreateDrive(pippinChannelDrive, "A Channel Drive", "", allowAnonymousReads: true, ownerOnly: false,
            allowSubscriptions: true);

        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);
        await samOwnerClient.OwnerFollower.FollowIdentity(pippinOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        //
        // Validate Pippin knows Sam follows him
        //
        var samFollowingPippinDefinition = await pippinOwnerClient.OwnerFollower.GetFollower(samOwnerClient.Identity);
        Assert.IsNotNull(samFollowingPippinDefinition);

        //
        // Pippin uploads a post
        //
        var uploadedContent = "I'm Hungry!";
        var uploadResult = await UploadUnencryptedContentToChannel(pippinOwnerClient, pippinChannelDrive, uploadedContent, acl: AccessControlList.Anonymous);

        await pippinOwnerClient.Transit.WaitForEmptyOutbox(pippinChannelDrive);

        //
        // Get the post from Sam's feed drive, validate we got it
        //
        var headerOnSamsFeed = await GetHeaderFromFeedDrive(samOwnerClient, uploadResult);
        Assert.IsTrue(headerOnSamsFeed.FileMetadata.AppData.Content == uploadedContent);

        //
        // Sam adds reaction from Sam's feed to Pippin's channel
        //
        await samOwnerClient.Transit.AddReaction(pippinOwnerClient.Identity,
            uploadResult.GlobalTransitIdFileIdentifier,
            reactionContent);

 
        await pippinOwnerClient.Transit.WaitForEmptyOutbox(pippinChannelDrive);

        //
        // Sam queries across Transit to get all reactions
        //
        var response = await samOwnerClient.Transit.GetAllReactions(pippinOwnerClient.Identity, new GetRemoteReactionsRequest()
        {
            File = uploadResult.GlobalTransitIdFileIdentifier,
            Cursor = 0,
            MaxRecords = 100
        });

        Assert.IsTrue(response.Reactions.Count == 1);
        var theReaction = response.Reactions.SingleOrDefault();
        Assert.IsTrue(theReaction!.ReactionContent == reactionContent);
        Assert.IsTrue(theReaction!.GlobalTransitIdFileIdentifier == uploadResult.GlobalTransitIdFileIdentifier);

        //
        // Get the post from Sam's feed drive, validate we got it
        //
        var headerOnSamsFeedWithReaction = await GetHeaderFromFeedDrive(samOwnerClient, uploadResult);
        Assert.IsTrue(headerOnSamsFeedWithReaction.FileMetadata.AppData.Content == uploadedContent);
        var reactionSummaryValue =
            headerOnSamsFeedWithReaction.FileMetadata.ReactionPreview.Reactions.Values.SingleOrDefault(r => r.ReactionContent == reactionContent);
        Assert.IsNotNull(reactionSummaryValue, "could not find reaction on Sam's feed");

        // Now, Sam deletes the reactions
        var deleteReactionResponse =
            await samOwnerClient.Transit.DeleteReaction(pippinOwnerClient.Identity, reactionContent, uploadResult.GlobalTransitIdFileIdentifier);
        Assert.IsTrue(deleteReactionResponse.IsSuccessStatusCode);

        await pippinOwnerClient.Transit.WaitForEmptyOutbox(pippinChannelDrive);

        //
        // Get the post from sam's feed drive again, it should have the header updated
        //
        var headerOnSamsFeedWithAfterReactionWasDeleted = await GetHeaderFromFeedDrive(samOwnerClient, uploadResult);
        Assert.IsFalse(headerOnSamsFeedWithAfterReactionWasDeleted.FileMetadata.ReactionPreview.Reactions.Any(),
            "There should be no reactions in the summary but there was at least one");
    }


    private async Task<UploadResult> UploadUnencryptedContentToChannel(OwnerApiClient client, TargetDrive targetDrive, string uploadedContent,
        bool allowDistribution = true,
        AccessControlList acl = null)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = allowDistribution,
            IsEncrypted = false,
            AppData = new()
            {
                Content = uploadedContent,
                FileType = default,
                GroupId = default,
                Tags = default
            },
            AccessControlList = acl ?? AccessControlList.Connected
        };

        return await client.Drive.UploadFile(FileSystemType.Standard, targetDrive, fileMetadata);
    }


    private async Task<SharedSecretEncryptedFileHeader> GetHeaderFromFeedDrive(OwnerApiClient client, UploadResult uploadResult)
    {
        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        var batch = await client.Drive.QueryBatch(FileSystemType.Standard, qp);
        Assert.IsTrue(batch.SearchResults.Count() == 1, $"Batch size should be 1 but was {batch.SearchResults.Count()}");
        var header = batch.SearchResults.First();
        Assert.IsTrue(header.FileState == FileState.Active);
        Assert.IsTrue(header.FileMetadata.GlobalTransitId == uploadResult.GlobalTransitId);

        return header;
    }
}