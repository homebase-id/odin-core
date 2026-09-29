using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Hosting.Tests.V2.Ported.DataSubscription;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/OwnerApi/DataSubscription/DataSubscriptionAndGroupChannelDistributionTests1.cs
///
/// Feed distribution where the channel drive is a collaborative ("group") channel a connected member
/// posts to.
/// </summary>
/// <remarks>
/// Checked port. <b>Entirely <c>[Ignore]("return to these after prototyping phase")</c>, carried
/// as-is</b> — this moved without ever having run.
/// <list type="bullet">
/// <item>Only the first test is about group channels at all. The other seven are copies of their
/// namesakes in <see cref="DataSubscriptionAndDistributionTests1"/> — same file types, same content,
/// same assertions, on an ordinary channel drive with no group attributes. They are carried rather
/// than deleted or merged: the ignore reason says the fixture is to be returned to, and what it should
/// become is a decision for whoever un-ignores it.</item>
/// <item>The first test posts as Sam to a <c>TargetDrive</c> that only the group identity created, so
/// the upload targets a drive Sam does not have. Carried verbatim; it is one of the things the ignore
/// note is presumably about.</item>
/// <item>Same mechanical substitutions as its sibling fixture: <c>WaitForEmptyOutbox</c> →
/// <c>Sync.DrainOutboxAsync()</c>, <c>ProcessInbox(FeedDrive)</c> →
/// <c>Sync.ProcessInboxAsync(FeedDrive)</c>, the private drive-create / connect / follow / upload /
/// assert helpers → <see cref="DataSubscriptionScenario"/>, trailing unfollow / disconnect cleanup
/// dropped. The group drive's attribute bag is the framework's <see cref="DriveSpec.CollabAttributes"/>,
/// which is the same single <c>IsCollaborativeChannel</c> entry the original built inline.</item>
/// <item>Since none of it runs, none of those substitutions has been exercised.</item>
/// </list>
/// </remarks>
[TestFixture]
public class DataSubscriptionAndGroupChannelDistributionTests1 : V2Fixture
{
    protected override string[] HostIdentities =>
        [Identities.Frodo, Identities.Sam, Identities.Pippin, Identities.Merry];

    [Test]
    [Ignore("return to these after prototyping phase")]
    public async Task GroupChannelMember_CanUpdateStandardFileAndDistributeChangesForAllNotifications()
    {
        const int fileType = 2001;
        var groupIdentityOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        //create a channel drive
        var groupChannelDrive = await DataSubscriptionScenario.CreateChannelDriveAsync(groupIdentityOwnerClient,
            name: "A Group Channel Drive", attributes: DriveSpec.CollabAttributes);

        var memberCircleId = Guid.NewGuid();
        await groupIdentityOwnerClient.Admin.CreateCircle(memberCircleId, "group members", new PermissionSetGrantRequest
            {
                Drives = new List<DriveGrantRequest>
                {
                    new()
                    {
                        PermissionedDrive = new()
                        {
                            Drive = groupChannelDrive,
                            Permission = DrivePermission.ReadWrite
                        },
                    }
                },
                PermissionSet = default
            }
        );

        await DataSubscriptionScenario.ConnectAsync(groupIdentityOwnerClient, samOwnerClient, memberCircleId);

        // Sam to follow everything from frodo
        await DataSubscriptionScenario.FollowAsync(samOwnerClient, groupIdentityOwnerClient);

        // channel member posts they are here
        var uploadedContent = "Hi all, I'm here; my name is Sam.";
        var firstUploadResult = await DataSubscriptionScenario.UploadStandardUnencryptedFileToChannelAsync(
            samOwnerClient, groupChannelDrive, uploadedContent, fileType);

        await groupIdentityOwnerClient.Sync.DrainOutboxAsync();

        // Sam should have the same content on his feed drive since it was distributed by the backend
        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var query = DataSubscriptionScenario.FeedQueryByFileType(fileType);

        var originalFile = await DataSubscriptionScenario.AssertFeedDriveHasFileAsync(
            samOwnerClient, query, uploadedContent, firstUploadResult);

        //Now change the file as if someone edited a post
        var updatedContent = "No really, I'm Frodo Baggins";
        await DataSubscriptionScenario.OverwriteStandardFileAsync(
            owner: groupIdentityOwnerClient,
            overwriteFile: firstUploadResult.File,
            updatedContent,
            fileType,
            versionTag: firstUploadResult.NewVersionTag);

        await groupIdentityOwnerClient.Sync.DrainOutboxAsync();

        // Sam should have the same content on his feed drive
        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        //Sam should have changes; note - we're using the same query params intentionally
        var batch2 = await DataSubscriptionScenario.QueryBatchAsync(samOwnerClient, query);
        Assert.That(batch2.Count, Is.EqualTo(1));
        var updatedFile = batch2.First();
        Assert.That(updatedFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(updatedFile.FileMetadata.Created.milliseconds, Is.EqualTo(originalFile.FileMetadata.Created.milliseconds));
        Assert.That(updatedFile.FileMetadata.Updated.milliseconds, Is.GreaterThan(originalFile.FileMetadata.Updated.milliseconds));
        Assert.That(updatedFile.FileMetadata.AppData.Content, Is.EqualTo(updatedContent));
        Assert.That(updatedFile.FileMetadata.AppData.Content, Is.Not.EqualTo(originalFile.FileMetadata.AppData.Content));
        Assert.That(updatedFile.FileMetadata.GlobalTransitId, Is.EqualTo(originalFile.FileMetadata.GlobalTransitId));
        Assert.That(updatedFile.FileMetadata.ReactionPreview, Is.Null,
            "ReactionPreview should be null on initial file upload; even tho it was updated");

        // Assert.That(updatedFile.FileMetadata.ReactionPreview.TotalCommentCount, Is.EqualTo(originalFile.FileMetadata.ReactionPreview.TotalCommentCount));
        // CollectionAssert.AreEquivalent(updatedFile.FileMetadata.ReactionPreview.Reactions, originalFile.FileMetadata.ReactionPreview.Reactions);
        // CollectionAssert.AreEquivalent(updatedFile.FileMetadata.ReactionPreview.Comments, originalFile.FileMetadata.ReactionPreview.Comments);
    }

    [Test]
    [Ignore("return to these after prototyping phase")]
    public async Task UnencryptedStandardFile_UploadedByOwner_DistributedToConnectedIdentities_ThatFollowOwner()
    {
        const int fileType = 10144;

        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        //create a channel drive
        var frodoChannelDrive = await DataSubscriptionScenario.CreateChannelDriveAsync(frodoOwnerClient);

        await DataSubscriptionScenario.ConnectAsync(frodoOwnerClient, samOwnerClient);

        // Sam to follow everything from frodo
        await DataSubscriptionScenario.FollowAsync(samOwnerClient, frodoOwnerClient);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var uploadResult = await DataSubscriptionScenario.UploadStandardUnencryptedFileToChannelAsync(
            frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        await frodoOwnerClient.Sync.DrainOutboxAsync();

        await DataSubscriptionScenario.AssertFeedDriveHasFileAsync(samOwnerClient,
            DataSubscriptionScenario.FeedQueryByGlobalTransitId(uploadResult), uploadedContent, uploadResult);
    }

    [Test]
    [Ignore("return to these after prototyping phase")]
    public async Task UnencryptedStandardFile_UploadedByOwner_Distributed_ToFollowers_That_AreNotConnected()
    {
        const int fileType = 10144;

        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        //create a channel drive
        var frodoChannelDrive = await DataSubscriptionScenario.CreateChannelDriveAsync(frodoOwnerClient);

        // Sam to follow everything from frodo
        await DataSubscriptionScenario.FollowAsync(samOwnerClient, frodoOwnerClient);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var uploadResult = await DataSubscriptionScenario.UploadStandardUnencryptedFileToChannelAsync(
            frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        await frodoOwnerClient.Sync.DrainOutboxAsync();

        //It should be direct write
        // Sam should have the same content on his feed drive
        // await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        await DataSubscriptionScenario.AssertFeedDriveHasFileAsync(samOwnerClient,
            DataSubscriptionScenario.FeedQueryByGlobalTransitId(uploadResult), uploadedContent, uploadResult);
    }

    [Test]
    [Ignore("return to these after prototyping phase")]
    public async Task EncryptedStandardFile_UploadedByOwner_DistributedToConnectedIdentities_ThatFollowOwner()
    {
        const int fileType = 11344;

        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        //create a channel drive
        var frodoChannelDrive = await DataSubscriptionScenario.CreateChannelDriveAsync(frodoOwnerClient);

        await DataSubscriptionScenario.ConnectAsync(frodoOwnerClient, samOwnerClient);

        // Sam to follow everything from frodo
        await DataSubscriptionScenario.FollowAsync(samOwnerClient, frodoOwnerClient);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var (uploadResult, encryptedJsonContent64) =
            await DataSubscriptionScenario.UploadStandardEncryptedFileToChannelAsync(
                frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        //Process the outbox since we're sending an encrypted file
        await frodoOwnerClient.Sync.DrainOutboxAsync();

        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        await DataSubscriptionScenario.AssertFeedDriveHasFileAsync(samOwnerClient,
            DataSubscriptionScenario.FeedQueryByGlobalTransitId(uploadResult), encryptedJsonContent64, uploadResult);
    }

    [Test]
    [Ignore("return to these after prototyping phase")]
    public async Task EncryptedStandardFile_UploadedByOwner_Distributed_ToFollowers_That_AreNotConnected_ReceivesNoFiles()
    {
        const int fileType = 11355;

        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        //create a channel drive
        var frodoChannelDrive = await DataSubscriptionScenario.CreateChannelDriveAsync(frodoOwnerClient);

        // Sam to follow everything from frodo
        await DataSubscriptionScenario.FollowAsync(samOwnerClient, frodoOwnerClient);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var (uploadResult, _) = await DataSubscriptionScenario.UploadStandardEncryptedFileToChannelAsync(
            frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        //Process the outbox since we're sending an encrypted file
        await frodoOwnerClient.Sync.DrainOutboxAsync();

        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        await DataSubscriptionScenario.AssertFeedDriveDoesNotHaveHeaderAsync(samOwnerClient, uploadResult);
    }

    [Test]
    [Ignore("return to these after prototyping phase")]
    public async Task UnencryptedStandardFile_UploadedByOwner_DistributeTo_Both_ConnectedAndUnconnected_Followers()
    {
        const int fileType = 1111;

        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);
        var merryOwnerClient = await LoginAsOwner(Identities.Merry);
        var pippinOwnerClient = await LoginAsOwner(Identities.Pippin);

        //create a channel drive
        var frodoChannelDrive = await DataSubscriptionScenario.CreateChannelDriveAsync(frodoOwnerClient,
            allowAnonymousReads: true);

        // Sam is connected to follow everything from frodo
        await DataSubscriptionScenario.ConnectAsync(frodoOwnerClient, samOwnerClient);
        await DataSubscriptionScenario.FollowAsync(samOwnerClient, frodoOwnerClient);

        //Pippin and merry follow a channel
        await DataSubscriptionScenario.FollowAsync(pippinOwnerClient, frodoOwnerClient,
            FollowerNotificationType.SelectedChannels, [frodoChannelDrive]);
        await DataSubscriptionScenario.FollowAsync(merryOwnerClient, frodoOwnerClient,
            FollowerNotificationType.SelectedChannels, [frodoChannelDrive]);

        // Frodo uploads content to channel drive
        const string uploadedContent = "I'm Mr. Underhill";
        var uploadResult = await DataSubscriptionScenario.UploadStandardUnencryptedFileToChannelAsync(
            frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        //Process the outbox since we're sending an encrypted file
        await frodoOwnerClient.Sync.DrainOutboxAsync();

        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);
        await pippinOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);
        await merryOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var query = DataSubscriptionScenario.FeedQueryByGlobalTransitId(uploadResult);

        //All should have the file
        await DataSubscriptionScenario.AssertFeedDriveHasFileAsync(samOwnerClient, query, uploadedContent, uploadResult);
        await DataSubscriptionScenario.AssertFeedDriveHasFileAsync(pippinOwnerClient, query, uploadedContent, uploadResult);
        await DataSubscriptionScenario.AssertFeedDriveHasFileAsync(merryOwnerClient, query, uploadedContent, uploadResult);
    }

    [Test]
    [Ignore("return to these after prototyping phase")]
    public async Task
        UnencryptedStandardFile_UploadedByOwner_DistributeTo_Both_ConnectedAndUnconnected_Followers_And_DeletedFrom_FollowersFeeds_When_Owner_Deletes_File()
    {
        const int fileType = 1117;

        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);
        var merryOwnerClient = await LoginAsOwner(Identities.Merry);
        var pippinOwnerClient = await LoginAsOwner(Identities.Pippin);

        //create a channel drive
        var frodoChannelDrive = await DataSubscriptionScenario.CreateChannelDriveAsync(frodoOwnerClient,
            allowAnonymousReads: true);

        // Sam is connected to follow everything from frodo
        await DataSubscriptionScenario.ConnectAsync(frodoOwnerClient, samOwnerClient);
        await DataSubscriptionScenario.FollowAsync(samOwnerClient, frodoOwnerClient);

        //Pippin and merry follow a channel
        await DataSubscriptionScenario.FollowAsync(pippinOwnerClient, frodoOwnerClient,
            FollowerNotificationType.SelectedChannels, [frodoChannelDrive]);
        await DataSubscriptionScenario.FollowAsync(merryOwnerClient, frodoOwnerClient,
            FollowerNotificationType.SelectedChannels, [frodoChannelDrive]);

        // Frodo uploads content to channel drive
        const string uploadedContent = "I'm Mr. Underhill";
        var uploadResult = await DataSubscriptionScenario.UploadStandardUnencryptedFileToChannelAsync(
            frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        await frodoOwnerClient.Sync.DrainOutboxAsync();

        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);
        await pippinOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);
        await merryOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var query = DataSubscriptionScenario.FeedQueryByGlobalTransitId(uploadResult);

        //All should have the file
        await DataSubscriptionScenario.AssertFeedDriveHasFileAsync(samOwnerClient, query, uploadedContent, uploadResult);
        await DataSubscriptionScenario.AssertFeedDriveHasFileAsync(pippinOwnerClient, query, uploadedContent, uploadResult);
        await DataSubscriptionScenario.AssertFeedDriveHasFileAsync(merryOwnerClient, query, uploadedContent, uploadResult);

        //
        // The Frodo deletes the file
        //
        var deleteResponse = await frodoOwnerClient.V1.Drive.SoftDeleteFile(uploadResult.File);
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Validate Frodo no longer has it
        var getDeletedFileResponse = await frodoOwnerClient.V1.Drive.GetFileHeader(uploadResult.File);
        Assert.That(getDeletedFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getDeletedFileResponse.Content!.FileState, Is.EqualTo(FileState.Deleted),
            "frodo's file should be marked deleted");

        await frodoOwnerClient.Sync.DrainOutboxAsync();

        //
        // Sam's feed drive no longer has the header
        //
        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);
        await DataSubscriptionScenario.AssertFeedDriveHasDeletedFileAsync(samOwnerClient, uploadResult);

        //
        // Pippin's feed drive no longer has the header
        //
        await pippinOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);
        await DataSubscriptionScenario.AssertFeedDriveHasDeletedFileAsync(pippinOwnerClient, uploadResult);

        //
        // Merry's feed drive no longer has the header
        //
        await merryOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);
        await DataSubscriptionScenario.AssertFeedDriveHasDeletedFileAsync(merryOwnerClient, uploadResult);
    }

    [Test]
    [Ignore("return to these after prototyping phase")]
    public async Task CommentingOn_EncryptedStandardFile_Updates_ReactionPreview()
    {
        const int fileType = 11345;

        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        //create a channel drive
        var frodoChannelDrive = await DataSubscriptionScenario.CreateChannelDriveAsync(frodoOwnerClient);

        await DataSubscriptionScenario.ConnectAsync(frodoOwnerClient, samOwnerClient);

        // Sam to follow everything from frodo
        await DataSubscriptionScenario.FollowAsync(samOwnerClient, frodoOwnerClient);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var (uploadResult, encryptedJsonContent64) =
            await DataSubscriptionScenario.UploadStandardEncryptedFileToChannelAsync(
                frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        //Process the outbox since we're sending an encrypted file
        await frodoOwnerClient.Sync.DrainOutboxAsync();

        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        await DataSubscriptionScenario.AssertFeedDriveHasFileAsync(samOwnerClient,
            DataSubscriptionScenario.FeedQueryByGlobalTransitId(uploadResult), encryptedJsonContent64, uploadResult);
    }
}
