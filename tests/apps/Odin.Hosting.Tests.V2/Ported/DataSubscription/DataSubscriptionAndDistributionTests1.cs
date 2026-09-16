using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;

namespace Odin.Hosting.Tests.V2.Ported.DataSubscription;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/OwnerApi/DataSubscription/DataSubscriptionAndDistributionTests1.cs
///
/// Feed distribution from a channel drive: a post reaches the feed of connected and unconnected
/// followers, edits and deletions follow it there, comments themselves are never distributed but the
/// reaction summary they produce is, and an encrypted post reaches connected followers only.
/// </summary>
/// <remarks>
/// Checked port. No caller matrix in the original and none here — every call is made as an owner.
/// <list type="bullet">
/// <item>Every <c>Transit.WaitForEmptyOutbox(drive)</c> becomes <c>Sync.DrainOutboxAsync()</c> and every
/// <c>Transit.ProcessInbox(FeedDrive)</c> becomes <c>Sync.ProcessInboxAsync(FeedDrive)</c>: the V1 calls
/// are passive polls on the outbox background service, which the fast host registers but never starts.
/// The drain is per-tenant rather than per-drive, so the originals' drive argument is dropped; where an
/// original drained twice in a row (<c>…_NotConnected</c> does) both calls are kept, since a second
/// drain is a no-op either way.</item>
/// <item>The reaction-summary tests query the follower's feed <i>without</i> processing the follower's
/// inbox after the sender's drain, exactly as the originals did — feed distribution writes the updated
/// header straight onto the follower's feed drive rather than routing it through the inbox.</item>
/// <item>The private upload / overwrite / assert helpers are
/// <see cref="DataSubscriptionScenario"/>; they were duplicated across all four fixtures in this
/// folder.</item>
/// <item>The connection and follow calls are asserted here. The V1 owner clients asserted success
/// inside the client (<c>CircleNetworkApiClient.SendConnectionRequestTo</c> and friends), so this keeps
/// the coverage rather than adding to it. Follow answers <b>204 NoContent</b>, not 200.</item>
/// <item><c>ReactionSummaryIsDistributedWhenCommentAdded_ByAnotherConnectedIdentity_ToStandardEncryptedFile_AndJsonContentIsEmpty</c>
/// ran on Merry and Pippin in the original — under variables still named <c>frodoOwnerClient</c> /
/// <c>samOwnerClient</c> — because WebScaffold shared identities process-wide and the test needed a
/// pair no other test had dirtied. Per-test reset removes that reason, so it runs on Frodo and Sam like
/// its siblings; Merry and Pippin are still booted for the two four-identity tests.</item>
/// <item>Trailing disconnect / unfollow calls were cleanup only and are dropped — per-test reset covers
/// them.</item>
/// <item>Carried defect: <c>CommentingOn_EncryptedStandardFile_Updates_ReactionPreview</c> does not
/// comment on anything and never looks at a reaction preview. It uploads an encrypted post and asserts
/// it reached Sam's feed, which makes it a duplicate of
/// <c>EncryptedStandardFile_UploadedByOwner_DistributedToConnectedIdentities_ThatFollowOwner</c> with a
/// different file type. Carried as written.</item>
/// <item>Carried defect: <c>CommentsAreNotDistributed</c> never drains after the comment upload, so the
/// "Sam has no comment" assertion would hold even if comments <i>were</i> distributed. The sibling
/// reaction-summary tests have the same gap for their own comment step.</item>
/// <item>Carried, unasserted: the originals captured <c>originalCommentUploadResult</c> and
/// <c>uploadResult2</c> and never read them. Those are dropped rather than carried as unused
/// locals — the upload calls themselves stay.</item>
/// </list>
/// </remarks>
[TestFixture]
public class DataSubscriptionAndDistributionTests1 : V2Fixture
{
    protected override string[] HostIdentities =>
        [Identities.Frodo, Identities.Sam, Identities.Pippin, Identities.Merry];

    [Test]
    public async Task CanUpdateStandardFileAndDistributeChangesForAllNotifications()
    {
        const int fileType = 2001;
        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        //create a channel drive
        var frodoChannelDrive = DataSubscriptionScenario.NewChannelDrive();

        await frodoOwnerClient.Admin.CreateDrive(frodoChannelDrive, "A Channel Drive", allowAnonymousReads: false,
            ownerOnly: false, allowSubscriptions: true);

        // Sam to follow everything from frodo
        await FollowAsync(samOwnerClient, frodoOwnerClient);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill; I think";
        var firstUploadResult = await DataSubscriptionScenario.UploadStandardUnencryptedFileToChannelAsync(
            frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        await frodoOwnerClient.Sync.DrainOutboxAsync();

        // Sam should have the same content on his feed drive since it was distributed by the backend
        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var query = DataSubscriptionScenario.FeedQueryByFileType(fileType);

        var batch = await DataSubscriptionScenario.QueryBatchAsync(samOwnerClient, query);
        Assert.That(batch.Count, Is.EqualTo(1));
        var originalFile = batch.First();
        Assert.That(originalFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(originalFile.FileMetadata.AppData.Content, Is.EqualTo(uploadedContent));
        Assert.That(originalFile.FileMetadata.GlobalTransitId, Is.EqualTo(firstUploadResult.GlobalTransitId));

        //Now change the file as if someone edited a post
        var updatedContent = "No really, I'm Frodo Baggins";
        await DataSubscriptionScenario.OverwriteStandardFileAsync(
            owner: frodoOwnerClient,
            overwriteFile: firstUploadResult.File,
            updatedContent,
            fileType,
            versionTag: firstUploadResult.NewVersionTag);

        await frodoOwnerClient.Sync.DrainOutboxAsync();

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
    [Ignore("causes other tests to fail when multiple tests are running, need ot figure out w/ Michael")]
    public async Task CanUploadStandardFileThenDeleteThenDistributeDeletion()
    {
        const int fileType = 1117;
        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        //create a channel drive
        var frodoChannelDrive = DataSubscriptionScenario.NewChannelDrive();

        await frodoOwnerClient.Admin.CreateDrive(frodoChannelDrive, "A Channel Drive", allowAnonymousReads: false,
            ownerOnly: false, allowSubscriptions: true);

        // Sam to follow everything from frodo
        await FollowAsync(samOwnerClient, frodoOwnerClient);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill; I think";
        var standardFileUploadResult = await DataSubscriptionScenario.UploadStandardUnencryptedFileToChannelAsync(
            frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        // Sam should have the same content on his feed drive
        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var batch = await DataSubscriptionScenario.QueryBatchAsync(samOwnerClient,
            DataSubscriptionScenario.FeedQueryByFileType(fileType));
        Assert.That(batch.Count, Is.EqualTo(1));
        var originalFile = batch.First();
        Assert.That(originalFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(originalFile.FileMetadata.AppData.Content, Is.EqualTo(uploadedContent));
        Assert.That(originalFile.FileMetadata.GlobalTransitId, Is.EqualTo(standardFileUploadResult.GlobalTransitId));

        //Frodo now deletes the file
        await frodoOwnerClient.V1.Drive.SoftDeleteFile(standardFileUploadResult.File);

        // Sam should have the same content on his feed drive
        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        //Sam should have the file marked as deleted
        var batch2 = await DataSubscriptionScenario.QueryBatchAsync(samOwnerClient,
            DataSubscriptionScenario.FeedQueryByGlobalTransitId(standardFileUploadResult));
        Assert.That(batch2.Count, Is.EqualTo(1));
        var deletedFile = batch2.First();
        Assert.That(deletedFile.FileState, Is.EqualTo(FileState.Deleted), "File should be deleted");
        Assert.That(deletedFile.FileMetadata.GlobalTransitId, Is.EqualTo(standardFileUploadResult.GlobalTransitId));
    }

    [Test]
    public async Task CommentsAreNotDistributed()
    {
        const int standardFileType = 332;
        const int commentFileType = 1113;

        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        //create a channel drive
        var frodoChannelDrive = DataSubscriptionScenario.NewChannelDrive();

        await frodoOwnerClient.Admin.CreateDrive(frodoChannelDrive, "A Channel Drive", allowAnonymousReads: false,
            ownerOnly: false, allowSubscriptions: true);

        // Sam to follow everything from frodo
        await FollowAsync(samOwnerClient, frodoOwnerClient);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var standardFileUploadResult = await DataSubscriptionScenario.UploadStandardUnencryptedFileToChannelAsync(
            frodoOwnerClient, frodoChannelDrive, uploadedContent, standardFileType);

        //Tell frodo's identity to process the outbox due to feed distribution
        await frodoOwnerClient.Sync.DrainOutboxAsync();

        // Sam should have the same content on his feed drive
        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        // Sam should have the blog post
        var batch = await DataSubscriptionScenario.QueryBatchAsync(samOwnerClient,
            DataSubscriptionScenario.FeedQueryByFileType(standardFileType));
        Assert.That(batch.Count, Is.EqualTo(1));
        var theFile = batch.First();
        Assert.That(theFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(theFile.FileMetadata.AppData.Content, Is.EqualTo(uploadedContent));
        Assert.That(theFile.FileMetadata.GlobalTransitId, Is.EqualTo(standardFileUploadResult.GlobalTransitId));

        var commentFile = CreateCommentMetadata(standardFileUploadResult, commentFileType);

        // Upload a comment by the owner
        await UploadCommentAsync(frodoOwnerClient, frodoChannelDrive, commentFile);

        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        // Sam should not have the comment since they are not distributed
        var commentBatch = await DataSubscriptionScenario.QueryBatchAsync(samOwnerClient,
            DataSubscriptionScenario.FeedQueryByFileType(commentFileType), FileSystemType.Comment);
        Assert.That(commentBatch, Is.Empty);
    }

    [Test]
    public async Task ReactionSummaryIsDistributedWhenCommentAddedByOwnerToStandardUnencryptedFile_WhenConnected()
    {
        const int standardFileType = 1121;
        const int commentFileType = 383;

        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        //create a channel drive
        var frodoChannelDrive = DataSubscriptionScenario.NewChannelDrive();

        await frodoOwnerClient.Admin.CreateDrive(frodoChannelDrive, "A Channel Drive", allowAnonymousReads: false,
            ownerOnly: false, allowSubscriptions: true);

        var securedChannelCircleId = Guid.NewGuid();
        await frodoOwnerClient.Admin.CreateCircle(securedChannelCircleId, "Secured channel content",
            TestUtils.CreatePermissionGrantRequest(frodoChannelDrive, DrivePermission.ReadWrite));

        //
        // Connect sam and frodo; sam gets access to the secured channel
        //
        await ConnectAsync(frodoOwnerClient, samOwnerClient, securedChannelCircleId);

        // Sam to follow everything from frodo
        await FollowAsync(samOwnerClient, frodoOwnerClient);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var standardFileUploadResult = await DataSubscriptionScenario.UploadStandardUnencryptedFileToChannelAsync(
            frodoOwnerClient, frodoChannelDrive, uploadedContent, standardFileType);

        await frodoOwnerClient.Sync.DrainOutboxAsync();

        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var standardFileQuery = DataSubscriptionScenario.FeedQueryByGlobalTransitId(standardFileUploadResult);

        // Sam should have the blog post from frodo in Sam's feed
        var batch = await DataSubscriptionScenario.QueryBatchAsync(samOwnerClient, standardFileQuery);
        Assert.That(batch.Count, Is.EqualTo(1));
        var theFile = batch.First();
        Assert.That(theFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(theFile.FileMetadata.AppData.Content, Is.EqualTo(uploadedContent));
        Assert.That(theFile.FileMetadata.GlobalTransitId, Is.EqualTo(standardFileUploadResult.GlobalTransitId));

        //Now, have sam comment on the file
        var commentFile = CreateCommentMetadata(standardFileUploadResult, commentFileType);

        // Upload a comment from frodo
        await UploadCommentAsync(frodoOwnerClient, frodoChannelDrive, commentFile);

        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        //
        // Sam should not have the comment since they are not distributed
        //
        var commentBatch = await DataSubscriptionScenario.QueryBatchAsync(samOwnerClient,
            DataSubscriptionScenario.FeedQueryByFileType(commentFileType), FileSystemType.Comment);
        Assert.That(commentBatch, Is.Empty);

        await frodoOwnerClient.Sync.DrainOutboxAsync();

        //
        // Sam should, however, have a reaction summary update for that comment on the original file
        //
        await AssertReactionPreviewHasCommentAsync(samOwnerClient, standardFileQuery, standardFileUploadResult,
            uploadedContent, commentFile.AppData.Content);
    }

    [Test]
    public async Task ReactionSummaryIsDistributedWhenCommentAddedByOwnerToStandardUnencryptedFile_NotConnected()
    {
        const int standardFileType = 1121;
        const int commentFileType = 383;

        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        //create a channel drive
        var frodoChannelDrive = DataSubscriptionScenario.NewChannelDrive();

        await frodoOwnerClient.Admin.CreateDrive(frodoChannelDrive, "A Channel Drive", allowAnonymousReads: false,
            ownerOnly: false, allowSubscriptions: true);

        // Sam to follow everything from frodo
        await FollowAsync(samOwnerClient, frodoOwnerClient);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var standardFileUploadResult = await DataSubscriptionScenario.UploadStandardUnencryptedFileToChannelAsync(
            frodoOwnerClient, frodoChannelDrive, uploadedContent, standardFileType);

        await frodoOwnerClient.Sync.DrainOutboxAsync();

        //TODO: should sam have to process transit instructions for feed items?
        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var standardFileQuery = DataSubscriptionScenario.FeedQueryByGlobalTransitId(standardFileUploadResult);

        // Sam should have the blog post from frodo in Sam's feed
        var batch = await DataSubscriptionScenario.QueryBatchAsync(samOwnerClient, standardFileQuery);
        Assert.That(batch.Count, Is.EqualTo(1));
        var theFile = batch.First();
        Assert.That(theFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(theFile.FileMetadata.AppData.Content, Is.EqualTo(uploadedContent));
        Assert.That(theFile.FileMetadata.GlobalTransitId, Is.EqualTo(standardFileUploadResult.GlobalTransitId));

        //Now, have sam comment on the file
        var commentFile = CreateCommentMetadata(standardFileUploadResult, commentFileType);

        // Upload a comment from frodo
        await UploadCommentAsync(frodoOwnerClient, frodoChannelDrive, commentFile);

        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        //
        // Sam should not have the comment since they are not distributed
        //
        var commentBatch = await DataSubscriptionScenario.QueryBatchAsync(samOwnerClient,
            DataSubscriptionScenario.FeedQueryByFileType(commentFileType), FileSystemType.Comment);
        Assert.That(commentBatch, Is.Empty);

        await frodoOwnerClient.Sync.DrainOutboxAsync();

        //Tell frodo's identity to process the outbox due to feed distribution
        await frodoOwnerClient.Sync.DrainOutboxAsync();

        //
        // Sam should, however, have a reaction summary update for that comment on the original file
        //
        await AssertReactionPreviewHasCommentAsync(samOwnerClient, standardFileQuery, standardFileUploadResult,
            uploadedContent, commentFile.AppData.Content);
    }

    [Test]
    public async Task ReactionSummaryIsDistributedWhenCommentAdded_ByAnother_ConnectedIdentity_ToStandardUnencryptedFile()
    {
        const int standardFileType = 441;
        const int commentFileType = 9989;

        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        //create a channel drive
        var frodoChannelDrive = DataSubscriptionScenario.NewChannelDrive();

        await frodoOwnerClient.Admin.CreateDrive(frodoChannelDrive, "A Channel Drive", allowAnonymousReads: false,
            ownerOnly: false, allowSubscriptions: true);

        var securedChannelCircleId = Guid.NewGuid();
        await frodoOwnerClient.Admin.CreateCircle(securedChannelCircleId, "Secured channel content",
            TestUtils.CreatePermissionGrantRequest(frodoChannelDrive, DrivePermission.ReadWrite));

        //
        // Connect sam and frodo; sam gets access to the secured channel
        //
        await ConnectAsync(frodoOwnerClient, samOwnerClient, securedChannelCircleId);

        // Sam to follow everything from frodo
        await FollowAsync(samOwnerClient, frodoOwnerClient);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var standardFileUploadResult = await DataSubscriptionScenario.UploadStandardUnencryptedFileToChannelAsync(
            frodoOwnerClient, frodoChannelDrive, uploadedContent, standardFileType);

        await frodoOwnerClient.Sync.DrainOutboxAsync();

        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var standardFileQuery = DataSubscriptionScenario.FeedQueryByGlobalTransitId(standardFileUploadResult);

        await frodoOwnerClient.Sync.DrainOutboxAsync();

        // Sam should have the blog post from frodo in Sam's feed
        var batch = await DataSubscriptionScenario.QueryBatchAsync(samOwnerClient, standardFileQuery);
        Assert.That(batch.Count, Is.EqualTo(1));
        var theFile = batch.First();
        Assert.That(theFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(theFile.FileMetadata.AppData.Content, Is.EqualTo(uploadedContent));
        Assert.That(theFile.FileMetadata.GlobalTransitId, Is.EqualTo(standardFileUploadResult.GlobalTransitId));

        //Now, have Sam comment on the file
        var commentFileMetadata = CreateCommentMetadata(standardFileUploadResult, commentFileType);
        commentFileMetadata.AccessControlList = AccessControlList.Anonymous;

        // transfer a comment from Sam directly to frodo
        var transitResponse = await samOwnerClient.V1.PeerDirect.TransferMetadata(
            remoteTargetDrive: frodoChannelDrive,
            commentFileMetadata,
            recipients: [frodoOwnerClient.Identity],
            overwriteGlobalTransitFileId: null,
            fileSystemType: FileSystemType.Comment);

        Assert.That(transitResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var transitResult = transitResponse.Content;
        Assert.That(transitResult, Is.Not.Null);

        //comment should have made it directly to the recipient's server
        Assert.That(transitResult!.RecipientStatus.Count, Is.EqualTo(1));
        var s = transitResult.RecipientStatus[frodoOwnerClient.Identity];
        Assert.That(s, Is.EqualTo(TransferStatus.Enqueued));

        // weird
        await samOwnerClient.Sync.DrainOutboxAsync();

        //
        // Sam should not have the comment since they are not distributed
        //
        var commentBatch = await DataSubscriptionScenario.QueryBatchAsync(samOwnerClient,
            DataSubscriptionScenario.FeedQueryByFileType(commentFileType), FileSystemType.Comment);
        Assert.That(commentBatch, Is.Empty);

        await frodoOwnerClient.Sync.DrainOutboxAsync();

        //
        // Sam should, however, have a reaction summary update for that comment on the original file
        //
        await AssertReactionPreviewHasCommentAsync(samOwnerClient, standardFileQuery, standardFileUploadResult,
            uploadedContent, commentFileMetadata.AppData.Content);
        //TODO: test the other file parts here
    }

    [Test]
    public async Task ReactionSummaryIsDistributedWhenCommentAdded_ByAnotherConnectedIdentity_ToStandardEncryptedFile_AndJsonContentIsEmpty()
    {
        const int standardFileType = 9441;
        const int commentFileType = 9999;

        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        //create a channel drive
        var frodoChannelDrive = DataSubscriptionScenario.NewChannelDrive();

        await frodoOwnerClient.Admin.CreateDrive(frodoChannelDrive, "A Channel Drive", allowAnonymousReads: false,
            ownerOnly: false, allowSubscriptions: true);

        var securedChannelCircleId = Guid.NewGuid();
        await frodoOwnerClient.Admin.CreateCircle(securedChannelCircleId, "Secured channel content",
            TestUtils.CreatePermissionGrantRequest(frodoChannelDrive, DrivePermission.ReadWrite));

        //
        // Connect sam and frodo; sam gets access to the secured channel
        //
        await ConnectAsync(frodoOwnerClient, samOwnerClient, securedChannelCircleId);

        // Sam to follow everything from frodo
        await FollowAsync(samOwnerClient, frodoOwnerClient);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var (standardFileUploadResult, encryptedStandardFileJsonContent64) =
            await DataSubscriptionScenario.UploadStandardEncryptedFileToChannelAsync(
                frodoOwnerClient, frodoChannelDrive, uploadedContent, standardFileType);

        //Tell frodo's identity to process the outbox
        await frodoOwnerClient.Sync.DrainOutboxAsync();

        //TODO: should sam have to process transit instructions for feed items?
        // Sam should have the same content on his feed drive
        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var standardFileQuery = DataSubscriptionScenario.FeedQueryByGlobalTransitId(standardFileUploadResult);

        // Sam should have the blog post from frodo in Sam's feed
        var batch = await DataSubscriptionScenario.QueryBatchAsync(samOwnerClient, standardFileQuery);
        Assert.That(batch.Count, Is.EqualTo(1));
        var theFile = batch.First();
        Assert.That(theFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(theFile.FileMetadata.AppData.Content, Is.EqualTo(encryptedStandardFileJsonContent64));
        Assert.That(theFile.FileMetadata.GlobalTransitId, Is.EqualTo(standardFileUploadResult.GlobalTransitId));

        //Now, have Sam comment on the file
        var commentFile = CreateCommentMetadata(standardFileUploadResult, commentFileType);
        commentFile.IsEncrypted = true;
        commentFile.AccessControlList = AccessControlList.Connected;

        // transfer a comment from Sam directly to frodo
        var (transitResponse, _) = await samOwnerClient.V1.PeerDirect.TransferEncryptedMetadata(
            remoteTargetDrive: frodoChannelDrive,
            commentFile,
            recipients: [frodoOwnerClient.Identity],
            overwriteGlobalTransitFileId: null,
            fileSystemType: FileSystemType.Comment);

        Assert.That(transitResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var transitResult = transitResponse.Content;
        Assert.That(transitResult, Is.Not.Null);

        //comment should have made it directly to the recipient's server
        Assert.That(transitResult!.RecipientStatus.Count, Is.EqualTo(1));
        var s = transitResult.RecipientStatus[frodoOwnerClient.Identity];
        Assert.That(s, Is.EqualTo(TransferStatus.Enqueued));

        // weird
        await samOwnerClient.Sync.DrainOutboxAsync();

        //
        // Sam should not have the comment since they are not distributed
        //
        var commentBatch = await DataSubscriptionScenario.QueryBatchAsync(samOwnerClient,
            DataSubscriptionScenario.FeedQueryByFileType(commentFileType), FileSystemType.Comment);
        Assert.That(commentBatch, Is.Empty);

        await frodoOwnerClient.Sync.DrainOutboxAsync();

        // Sam should, however, have a reaction summary update for that comment on the original file
        //
        var batch2 = await DataSubscriptionScenario.QueryBatchAsync(samOwnerClient, standardFileQuery);
        Assert.That(batch2.Count, Is.EqualTo(1));
        var theFile2 = batch2.First();
        Assert.That(theFile2.FileState, Is.EqualTo(FileState.Active));
        Assert.That(theFile2.FileMetadata.AppData.Content, Is.EqualTo(encryptedStandardFileJsonContent64));
        Assert.That(theFile2.FileMetadata.GlobalTransitId, Is.EqualTo(standardFileUploadResult.GlobalTransitId));
        Assert.That(theFile2.FileMetadata.ReactionPreview, Is.Not.Null, "Reaction Preview is null");
        Assert.That(theFile2.FileMetadata.ReactionPreview.TotalCommentCount, Is.EqualTo(1));
        Assert.That(theFile2.FileMetadata.ReactionPreview.Comments, Has.Exactly(1).Matches<CommentPreview>(c => c.IsEncrypted));
        Assert.That(theFile2.FileMetadata.ReactionPreview.Comments, Has.Exactly(1).Matches<CommentPreview>(c => c.Content == ""));
        // Assert.That(theFile2.FileMetadata.ReactionPreview.Comments, Has.Exactly(1).Matches<CommentPreview>(c => c.Content == commentFile.AppData.Content));
        //TODO: test the other file parts here
    }

    [Test]
    public async Task UnencryptedStandardFile_UploadedByOwner_DistributedToConnectedIdentities_ThatFollowOwner()
    {
        const int fileType = 10144;

        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        //create a channel drive
        var frodoChannelDrive = DataSubscriptionScenario.NewChannelDrive();

        await frodoOwnerClient.Admin.CreateDrive(frodoChannelDrive, "A Channel Drive", allowAnonymousReads: false,
            ownerOnly: false, allowSubscriptions: true);

        await ConnectAsync(frodoOwnerClient, samOwnerClient);

        // Sam to follow everything from frodo
        await FollowAsync(samOwnerClient, frodoOwnerClient);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var uploadResult = await DataSubscriptionScenario.UploadStandardUnencryptedFileToChannelAsync(
            frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        await frodoOwnerClient.Sync.DrainOutboxAsync();

        var batch = await DataSubscriptionScenario.QueryBatchAsync(samOwnerClient,
            DataSubscriptionScenario.FeedQueryByGlobalTransitId(uploadResult));
        Assert.That(batch.Count, Is.EqualTo(1));
        var theFile = batch.First();
        Assert.That(theFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(theFile.FileMetadata.AppData.Content, Is.EqualTo(uploadedContent));
        Assert.That(theFile.FileMetadata.GlobalTransitId, Is.EqualTo(uploadResult.GlobalTransitId));
    }

    [Test]
    public async Task UnencryptedStandardFile_UploadedByOwner_Distributed_ToFollowers_That_AreNotConnected()
    {
        const int fileType = 10144;

        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        //create a channel drive
        var frodoChannelDrive = DataSubscriptionScenario.NewChannelDrive();

        await frodoOwnerClient.Admin.CreateDrive(frodoChannelDrive, "A Channel Drive", allowAnonymousReads: false,
            ownerOnly: false, allowSubscriptions: true);

        // Sam to follow everything from frodo
        await FollowAsync(samOwnerClient, frodoOwnerClient);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        Guid? uniqueId = Guid.NewGuid();
        var uploadResult = await DataSubscriptionScenario.UploadStandardUnencryptedFileToChannelAsync(
            frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType, uniqueId);

        await frodoOwnerClient.Sync.DrainOutboxAsync();

        //It should be direct write
        // Sam should have the same content on his feed drive
        // await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var batch = await DataSubscriptionScenario.QueryBatchAsync(samOwnerClient,
            DataSubscriptionScenario.FeedQueryByGlobalTransitId(uploadResult));
        Assert.That(batch.Count, Is.EqualTo(1));
        var theFile = batch.First();
        Assert.That(theFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(theFile.FileMetadata.AppData.Content, Is.EqualTo(uploadedContent));
        Assert.That(theFile.FileMetadata.GlobalTransitId, Is.EqualTo(uploadResult.GlobalTransitId));
        Assert.That(theFile.FileMetadata.AppData.UniqueId, Is.Null, "feed uniqueId should be null");
    }

    [Test]
    public async Task EncryptedStandardFile_UploadedByOwner_DistributedToConnectedIdentities_ThatFollowOwner()
    {
        const int fileType = 11344;

        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        //create a channel drive
        var frodoChannelDrive = DataSubscriptionScenario.NewChannelDrive();

        await frodoOwnerClient.Admin.CreateDrive(frodoChannelDrive, "A Channel Drive", allowAnonymousReads: false,
            ownerOnly: false, allowSubscriptions: true);

        await ConnectAsync(frodoOwnerClient, samOwnerClient);

        // Sam to follow everything from frodo
        await FollowAsync(samOwnerClient, frodoOwnerClient);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var (uploadResult, encryptedJsonContent64) =
            await DataSubscriptionScenario.UploadStandardEncryptedFileToChannelAsync(
                frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType, Guid.NewGuid());

        await frodoOwnerClient.Sync.DrainOutboxAsync();

        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var batch = await DataSubscriptionScenario.QueryBatchAsync(samOwnerClient,
            DataSubscriptionScenario.FeedQueryByGlobalTransitId(uploadResult));
        Assert.That(batch.Count, Is.EqualTo(1));
        var theFile = batch.First();
        Assert.That(theFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(theFile.FileMetadata.AppData.Content, Is.EqualTo(encryptedJsonContent64));
        Assert.That(theFile.FileMetadata.GlobalTransitId, Is.EqualTo(uploadResult.GlobalTransitId));
    }

    [Test]
    public async Task EncryptedStandardFile_UploadedByOwner_Distributed_ToFollowers_That_AreNotConnected_ReceivesNoFiles()
    {
        const int fileType = 11355;

        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        //create a channel drive
        var frodoChannelDrive = DataSubscriptionScenario.NewChannelDrive();

        await frodoOwnerClient.Admin.CreateDrive(frodoChannelDrive, "A Channel Drive", allowAnonymousReads: false,
            ownerOnly: false, allowSubscriptions: true);

        // Sam to follow everything from frodo
        await FollowAsync(samOwnerClient, frodoOwnerClient);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var (uploadResult, _) = await DataSubscriptionScenario.UploadStandardEncryptedFileToChannelAsync(
            frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        //Process the outbox since we're sending an encrypted file
        await frodoOwnerClient.Sync.DrainOutboxAsync();

        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var batch = await DataSubscriptionScenario.QueryBatchAsync(samOwnerClient,
            DataSubscriptionScenario.FeedQueryByGlobalTransitId(uploadResult));
        Assert.That(batch, Is.Empty);
    }

    [Test]
    public async Task UnencryptedStandardFile_UploadedByOwner_DistributeTo_Both_ConnectedAndUnconnected_Followers()
    {
        const int fileType = 1111;

        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);
        var merryOwnerClient = await LoginAsOwner(Identities.Merry);
        var pippinOwnerClient = await LoginAsOwner(Identities.Pippin);

        //create a channel drive
        var frodoChannelDrive = DataSubscriptionScenario.NewChannelDrive();

        await frodoOwnerClient.Admin.CreateDrive(frodoChannelDrive, "A Channel Drive", allowAnonymousReads: true,
            ownerOnly: false, allowSubscriptions: true);

        // Sam is connected to follow everything from frodo
        await ConnectAsync(frodoOwnerClient, samOwnerClient);
        await FollowAsync(samOwnerClient, frodoOwnerClient);

        //Pippin and merry follow a channel
        await FollowAsync(pippinOwnerClient, frodoOwnerClient, FollowerNotificationType.SelectedChannels, [frodoChannelDrive]);
        await FollowAsync(merryOwnerClient, frodoOwnerClient, FollowerNotificationType.SelectedChannels, [frodoChannelDrive]);

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
    }

    [Test]
    public async Task
        UnencryptedStandardFile_UploadedByOwner_DistributeTo_Both_ConnectedAndUnconnected_Followers_And_DeletedFrom_FollowersFeeds_When_Owner_Deletes_File()
    {
        const int fileType = 1117;

        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);
        var merryOwnerClient = await LoginAsOwner(Identities.Merry);
        var pippinOwnerClient = await LoginAsOwner(Identities.Pippin);

        //create a channel drive
        var frodoChannelDrive = DataSubscriptionScenario.NewChannelDrive();

        await frodoOwnerClient.Admin.CreateDrive(frodoChannelDrive, "A Channel Drive", allowAnonymousReads: true,
            ownerOnly: false, allowSubscriptions: true);

        // Sam is connected to follow everything from frodo
        await ConnectAsync(frodoOwnerClient, samOwnerClient);
        await FollowAsync(samOwnerClient, frodoOwnerClient);

        //Pippin and merry follow a channel
        await FollowAsync(pippinOwnerClient, frodoOwnerClient, FollowerNotificationType.SelectedChannels, [frodoChannelDrive]);
        await FollowAsync(merryOwnerClient, frodoOwnerClient, FollowerNotificationType.SelectedChannels, [frodoChannelDrive]);

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
    public async Task CommentingOn_EncryptedStandardFile_Updates_ReactionPreview()
    {
        const int fileType = 11345;

        var frodoOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);

        //create a channel drive
        var frodoChannelDrive = DataSubscriptionScenario.NewChannelDrive();

        await frodoOwnerClient.Admin.CreateDrive(frodoChannelDrive, "A Channel Drive", allowAnonymousReads: false,
            ownerOnly: false, allowSubscriptions: true);

        await ConnectAsync(frodoOwnerClient, samOwnerClient);

        // Sam to follow everything from frodo
        await FollowAsync(samOwnerClient, frodoOwnerClient);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var (uploadResult, encryptedJsonContent64) =
            await DataSubscriptionScenario.UploadStandardEncryptedFileToChannelAsync(
                frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        await frodoOwnerClient.Sync.DrainOutboxAsync();

        await samOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var batch = await DataSubscriptionScenario.QueryBatchAsync(samOwnerClient,
            DataSubscriptionScenario.FeedQueryByGlobalTransitId(uploadResult));
        Assert.That(batch.Count, Is.EqualTo(1));
        var theFile = batch.First();
        Assert.That(theFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(theFile.FileMetadata.AppData.Content, Is.EqualTo(encryptedJsonContent64));
        Assert.That(theFile.FileMetadata.GlobalTransitId, Is.EqualTo(uploadResult.GlobalTransitId));
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>The comment body every test in the fixture posts, referencing the standard file.</summary>
    private static UploadFileMetadata CreateCommentMetadata(UploadResult referencedFile, int commentFileType) => new()
    {
        AllowDistribution = true,
        IsEncrypted = false,
        ReferencedFile = referencedFile.GlobalTransitIdFileIdentifier,
        AppData = new()
        {
            Content = OdinSystemSerializer.Serialize(new { message = "Are you tho?" }),
            FileType = commentFileType,
            DataType = 202,
            UserDate = UnixTimeUtc.ZeroTime,
            Tags = default
        }
    };

    private static async Task UploadCommentAsync(OwnerSession owner, TargetDrive channelDrive, UploadFileMetadata commentFile)
    {
        var response = await owner.V1.Drive.UploadNewMetadata(channelDrive, commentFile, FileSystemType.Comment);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>Sends and accepts a connection request, optionally granting <paramref name="circleId"/>.</summary>
    private static async Task ConnectAsync(OwnerSession sender, OwnerSession recipient, Guid? circleId = null)
    {
        var sendResponse = await sender.Connections.SendConnectionRequest(recipient.Identity,
            circleId.HasValue ? [circleId.Value] : []);
        Assert.That(sendResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var acceptResponse = await recipient.Connections.AcceptConnectionRequest(sender.Identity, []);
        Assert.That(acceptResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static async Task FollowAsync(OwnerSession follower, OwnerSession followee,
        FollowerNotificationType notificationType = FollowerNotificationType.AllNotifications,
        List<TargetDrive> channels = null)
    {
        var response = await follower.V1.Follower.FollowIdentity(followee.Identity, notificationType, channels);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    /// <summary>
    /// The follower's copy of the post is still active and now carries a one-comment reaction preview
    /// holding <paramref name="expectedCommentContent"/>.
    /// </summary>
    private static async Task AssertReactionPreviewHasCommentAsync(
        OwnerSession follower,
        QueryBatchRequest standardFileQuery,
        UploadResult standardFileUploadResult,
        string expectedContent,
        string expectedCommentContent)
    {
        var batch = await DataSubscriptionScenario.QueryBatchAsync(follower, standardFileQuery);
        Assert.That(batch.Count, Is.EqualTo(1));
        var theFile = batch.First();
        Assert.That(theFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(theFile.FileMetadata.AppData.Content, Is.EqualTo(expectedContent));
        Assert.That(theFile.FileMetadata.GlobalTransitId, Is.EqualTo(standardFileUploadResult.GlobalTransitId));
        Assert.That(theFile.FileMetadata.ReactionPreview, Is.Not.Null, "Reaction Preview is null");
        Assert.That(theFile.FileMetadata.ReactionPreview.TotalCommentCount, Is.EqualTo(1));
        Assert.That(theFile.FileMetadata.ReactionPreview.Comments,
            Has.Exactly(1).Matches<CommentPreview>(c => c.Content == expectedCommentContent));
    }
}
