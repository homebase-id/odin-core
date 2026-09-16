#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.FileSystem.Base.Upload.Attachments;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Concepts;

/// <summary>
/// Port of <c>_Universal/Concepts/CollaborationChannel/CollaborationChannelTests</c>.
///
/// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints).
/// Does not test security but rather drive features.
/// </summary>
/// <remarks>
/// Checked port.
/// <list type="bullet">
/// <item>Unlike its <see cref="CollaborationChatPushNotificationTests"/> sibling, this fixture really
/// does drive the call through the caller: both tests call <c>callerContext.Initialize(...)</c> and
/// then issue the update through that caller's factory. The matrix is therefore live, and all three
/// rows (owner, app-with-UseTransitWrite-only, connected-identity-on-guest-api) are live too.</item>
/// <item>The caller shapes come from <see cref="CollabCallerSpec"/> rather than
/// <see cref="CallerSpec"/> — see the type's doc for why. The identity each caller is built against
/// is Pippin in both tests, matching the original's
/// <c>ConnectedIdentityLoggedInOnGuestApi(TestIdentities.Pippin.OdinId, …)</c>.</item>
/// <item>Every <c>WaitForEmptyOutbox</c> / <c>WaitForFeedOutboxDistribution</c> / <c>WaitForEmptyInbox</c>
/// / <c>ProcessInbox</c> becomes an explicit <c>Sync.DrainOutboxAsync()</c> or
/// <c>Sync.ProcessInboxAsync(drive)</c>. <c>WaitForFeedOutboxDistribution</c> is literally
/// <c>WaitForEmptyOutbox</c> under another name (see <c>UniversalDriveApiClient</c>), so it is a drain
/// on the collaboration channel.</item>
/// <item><b>Added, not in the original:</b> <c>Sync.ProcessInboxAsync(collabChannelDrive)</c> on the
/// collaboration channel after each peer send/update. V1 leaned on the inbox background service to
/// take the file off the inbox before it queried for it and before it redistributed to followers; the
/// fast host registers that service but never starts it, so without this the channel never has the
/// file at all.</item>
/// <item><c>FileMetadata.OriginalAuthor</c> is an <c>OdinId</c> and <c>SenderOdinId</c> is a
/// <c>string</c> — compared accordingly (see the README note).</item>
/// <item>Trailing disconnect / unfollow calls were cleanup only and are dropped — with one exception:
/// the original's <c>CleanupScenario</c> <i>asserted</i> on both unfollow responses. Those assertions
/// were about the tear-down succeeding rather than about the behaviour under test, and the README's
/// cleanup rule ("a delete whose response is checked is a test") is about a delete the test is
/// exercising; these two are dropped with the rest of the tear-down. Noted here so a reviewer can see
/// it was a decision rather than an oversight.</item>
/// </list>
/// </remarks>
[TestFixture]
public class CollaborationChannelTests : V2Fixture
{
    protected override string[] HostIdentities =>
        [Identities.Collab, Identities.Merry, Identities.Pippin, Identities.Sam];

    public static IEnumerable<object[]> CollaborationCases()
    {
        yield return [CollabCallerSpec.Owner(), HttpStatusCode.OK];
        yield return [CollabCallerSpec.AppWithOnlyUseTransitWrite(), HttpStatusCode.OK];
        yield return [CollabCallerSpec.ConnectedIdentityLoggedInOnGuestApi(), HttpStatusCode.Forbidden];
    }

    [Test, TestCaseSource(nameof(CollaborationCases))]
    public async Task CanViewAndEditCollaborativePostFromFeed(
        CollabCallerSpec callerContext, HttpStatusCode expected)
    {
        var collabChannel = await LoginAsOwner(Identities.Collab);
        var member1 = await LoginAsOwner(Identities.Merry);
        var member2 = await LoginAsOwner(Identities.Pippin);

        var collabChannelDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);

        await SetupScenario(collabChannel, member1, member2, collabChannelDrive);

        var keyHeader = KeyHeader.NewRandom16();

        var (response, firstFileUploadMetadata, payload1) =
            await AwaitPostNewEncryptedFileOverPeerDirect(member1, collabChannelDrive, collabChannel, keyHeader);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // The collab channel gets the file then will redistribute to its followers' feeds
        await collabChannel.Sync.ProcessInboxAsync(collabChannelDrive);
        await collabChannel.Sync.DrainOutboxAsync();

        var remoteTargetFile = response.Content!.RemoteGlobalTransitIdFileIdentifier.ToFileIdentifier();

        //
        // Assert member1 and member2 have the files in their feed
        //
        await AssertHasFileInFeed(member1, remoteTargetFile.GlobalTransitId.GetValueOrDefault(), firstFileUploadMetadata);
        await AssertHasFileInFeed(member2, remoteTargetFile.GlobalTransitId.GetValueOrDefault(), firstFileUploadMetadata);

        //
        // Update the file from Pippin's feed app (then wait for the outbox to process)
        //
        var (updateFileResponse, updatedFileMetadata) =
            await AwaitUpdateFile(callerContext, member2, firstFileUploadMetadata, remoteTargetFile, collabChannel,
                payload1, keyHeader);
        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(expected));

        // Let's test more
        if (expected != HttpStatusCode.OK)
        {
            return;
        }

        //
        // Assert collab channel has the updated file
        //
        await collabChannel.Sync.ProcessInboxAsync(collabChannelDrive);

        var updatedFileInCollabChannelResponse =
            await collabChannel.V1.Drive.QueryByGlobalTransitId(remoteTargetFile.ToGlobalTransitIdFileIdentifier());
        var updatedFileInCollabChannel = updatedFileInCollabChannelResponse.Content!.SearchResults.SingleOrDefault();
        Assert.That(updatedFileInCollabChannel, Is.Not.Null);
        Assert.That(updatedFileInCollabChannel!.FileMetadata.AppData.DataType,
            Is.EqualTo(updatedFileMetadata.AppData.DataType));
        Assert.That(updatedFileInCollabChannel.FileMetadata.OriginalAuthor, Is.EqualTo(member1.Identity));

        //
        // The collab channel gets the file then will redistribute to its followers' feeds
        //
        await collabChannel.Sync.DrainOutboxAsync();

        var uploadResult = updateFileResponse.Content;
        Assert.That(uploadResult, Is.Not.Null);

        //
        // Assert: member1 and member 2 have the updated file in their feed
        //
        await AssertHasFileInFeed(member1, remoteTargetFile.GlobalTransitId.GetValueOrDefault(), updatedFileMetadata);
        await AssertHasFileInFeed(member2, remoteTargetFile.GlobalTransitId.GetValueOrDefault(), updatedFileMetadata);
    }

    [Test, TestCaseSource(nameof(CollaborationCases))]
    public async Task CanUpdateRemoteFile_AndSeeChangesDistributedToFeed(
        CollabCallerSpec callerContext, HttpStatusCode expected)
    {
        var collabChannelOwnerClient = await LoginAsOwner(Identities.Collab);
        var member1OwnerClient = await LoginAsOwner(Identities.Pippin);
        var member2OwnerClient = await LoginAsOwner(Identities.Sam);

        var member1 = member1OwnerClient.Identity;
        var collabChannel = collabChannelOwnerClient.Identity;
        var member2 = member2OwnerClient.Identity;

        var collabChannelDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);

        await CollabScenario.PrepareScenarioAsync(
            collabChannelOwnerClient,
            [member1OwnerClient, member2OwnerClient],
            collabChannelDrive,
            DrivePermission.Write,
            "Test channel drive 001",
            allowAnonymousReads: true);

        var followResponse = await member2OwnerClient.V1.Follower.FollowIdentity(
            collabChannel, FollowerNotificationType.AllNotifications);
        Assert.That(followResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AllowDistribution = true;
        uploadedFileMetadata.AppData.DataType = 888;
        uploadedFileMetadata.AccessControlList = AccessControlList.Connected;
        var payload1 = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var payload2 = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2();

        var testPayloads = new List<TestPayloadDefinition>()
        {
            payload1,
            payload2
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        //Pippin sends a file to the recipient
        var response = await member1OwnerClient.V1.PeerDirect.TransferNewFile(collabChannelDrive, uploadedFileMetadata,
            [collabChannel], null,
            uploadManifest,
            testPayloads);
        await member1OwnerClient.Sync.DrainOutboxAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // the collab channel we get the file from the TransferNewFile and we need to wait for it to send it out to all followers
        await collabChannelOwnerClient.Sync.ProcessInboxAsync(collabChannelDrive);
        await collabChannelOwnerClient.Sync.DrainOutboxAsync(); //waiting for distribution to occur

        //
        // Update the file via pippin's identity
        //

        await member1OwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);
        await member2OwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var remoteTargetFile = response.Content!.RemoteGlobalTransitIdFileIdentifier.ToFileIdentifier();
        var caller = await callerContext.Build(member1OwnerClient);
        var callerDriveClient = caller.V1.Drive;

        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here";
        updatedFileMetadata.AppData.DataType = 999;

        var payloadToAdd = SamplePayloadDefinitions.GetPayloadDefinition1();
        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Peer,

            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = remoteTargetFile,
            Recipients = [collabChannel],
            Manifest = new UploadManifest
            {
                PayloadDescriptors =
                [
                    new UploadManifestPayloadDescriptor
                    {
                        PayloadUpdateOperationType = PayloadUpdateOperationType.AppendOrOverwrite,
                        Iv = Guid.Empty.ToByteArray(),
                        PayloadKey = payloadToAdd.Key,
                        DescriptorContent = null,
                        ContentType = payloadToAdd.ContentType,
                        PreviewThumbnail = default,
                        Thumbnails = new List<UploadedManifestThumbnailDescriptor>(),
                    },
                    new UploadManifestPayloadDescriptor()
                    {
                        PayloadUpdateOperationType = PayloadUpdateOperationType.DeletePayload,
                        PayloadKey = payload1.Key
                    }
                ]
            }
        };

        var updateFileResponse = await callerDriveClient.UpdateFile(updateInstructionSet, updatedFileMetadata, [payloadToAdd]);
        await member1OwnerClient.Sync.DrainOutboxAsync();
        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(expected));

        // the collab channel we get the file from the UpdateFile and we need to wait for it to send it out to all followers
        await collabChannelOwnerClient.Sync.ProcessInboxAsync(collabChannelDrive);
        await collabChannelOwnerClient.Sync.DrainOutboxAsync(); //waiting for distribution to occur

        // Let's test more
        if (expected != HttpStatusCode.OK)
        {
            return;
        }

        var uploadResult = updateFileResponse.Content;
        Assert.That(uploadResult, Is.Not.Null);

        // handle any incoming feed items
        await collabChannelOwnerClient.Sync.ProcessInboxAsync(remoteTargetFile.TargetDrive);

        //
        // Recipient should have the updated file
        //
        var getHeaderResponse =
            await collabChannelOwnerClient.V1.Drive.QueryByGlobalTransitId(remoteTargetFile.ToGlobalTransitIdFileIdentifier());
        Assert.That(getHeaderResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var header = getHeaderResponse.Content!.SearchResults.SingleOrDefault();
        Assert.That(header, Is.Not.Null);
        Assert.That(header!.FileMetadata.AppData.Content, Is.EqualTo(updatedFileMetadata.AppData.Content));
        Assert.That(header.FileMetadata.AppData.DataType, Is.EqualTo(updatedFileMetadata.AppData.DataType));
        Assert.That(header.FileMetadata.Payloads.Count(), Is.EqualTo(2));
        Assert.That(header.FileMetadata.Payloads.Select(pd => pd.Key), Does.Not.Contain(payload1.Key),
            "payload 1 should have been removed");
        Assert.That(header.FileMetadata.Payloads.Select(pd => pd.Key), Does.Contain(payload2.Key),
            "payload 2 should remain");
        Assert.That(header.FileMetadata.Payloads.Select(pd => pd.Key), Does.Contain(payloadToAdd.Key),
            "payloadToAdd should have been, well, added :)");

        // file should be on the feed of those connected
        var globalTransitIdFileIdentifier = new GlobalTransitIdFileIdentifier()
        {
            GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
            TargetDrive = WellKnownAppDrives.FeedDrive
        };

        await collabChannelOwnerClient.Sync.DrainOutboxAsync(); //waiting for distribution to occur
        await member2OwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var channelOnMembersFeedDrive = await member2OwnerClient.V1.Drive.QueryByGlobalTransitId(globalTransitIdFileIdentifier);
        Assert.That(channelOnMembersFeedDrive.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var theFileOnFeedDrive = channelOnMembersFeedDrive.Content!.SearchResults.SingleOrDefault();
        Assert.That(theFileOnFeedDrive, Is.Not.Null);

        Assert.That(theFileOnFeedDrive!.FileMetadata.AppData.Content, Is.EqualTo(updatedFileMetadata.AppData.Content));
        Assert.That(theFileOnFeedDrive.FileMetadata.AppData.DataType, Is.EqualTo(updatedFileMetadata.AppData.DataType));
        Assert.That(theFileOnFeedDrive.FileMetadata.SenderOdinId, Is.EqualTo(collabChannel.DomainName));
        Assert.That(theFileOnFeedDrive.FileMetadata.OriginalAuthor, Is.EqualTo(member1));
    }

    private static async Task<(ApiResponse<UploadPayloadResult> updateFileResponse, UploadFileMetadata updatedFile)>
        AwaitUpdateFile(
            CollabCallerSpec callerContext,
            OwnerSession sender,
            UploadFileMetadata uploadedFileMetadata,
            FileIdentifier remoteTargetFile,
            OwnerSession collabChannel,
            TestPayloadDefinition payload1,
            KeyHeader keyHeader)
    {
        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here";
        updatedFileMetadata.AppData.DataType = 5678;

        var payloadToAdd = SamplePayloadDefinitions.GetPayloadDefinition1();
        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Peer,

            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = remoteTargetFile,
            Recipients = [collabChannel.Identity],
            Manifest = new UploadManifest
            {
                PayloadDescriptors =
                [
                    new UploadManifestPayloadDescriptor
                    {
                        PayloadUpdateOperationType = PayloadUpdateOperationType.AppendOrOverwrite,
                        Iv = ByteArrayUtil.GetRndByteArray(16),
                        PayloadKey = payloadToAdd.Key,
                        DescriptorContent = null,
                        ContentType = payloadToAdd.ContentType,
                        PreviewThumbnail = default,
                        Thumbnails = new List<UploadedManifestThumbnailDescriptor>(),
                    },
                    new UploadManifestPayloadDescriptor()
                    {
                        PayloadUpdateOperationType = PayloadUpdateOperationType.DeletePayload,
                        PayloadKey = payload1.Key
                    }
                ]
            }
        };

        keyHeader.Iv = ByteArrayUtil.GetRndByteArray(16);
        var caller = await callerContext.Build(sender);
        var callerDriveClient = caller.V1.Drive;
        var (updateFileResponse, _, _, _) =
            await callerDriveClient.UpdateEncryptedFile(
                updateInstructionSet, updatedFileMetadata, [payloadToAdd], keyHeader);

        if (updateFileResponse.IsSuccessStatusCode)
        {
            await sender.Sync.DrainOutboxAsync();
        }

        return (updateFileResponse, updatedFileMetadata);
    }

    private static async Task AssertHasFileInFeed(
        OwnerSession recipient, Guid globalTransitId, UploadFileMetadata expectedFileMetadata)
    {
        await recipient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);
        var fileOnFeed = new FileIdentifier()
        {
            GlobalTransitId = globalTransitId,
            TargetDrive = WellKnownAppDrives.FeedDrive
        };

        var getFileByGtidResponse = await recipient.V1.Drive.QueryByGlobalTransitId(fileOnFeed.ToGlobalTransitIdFileIdentifier());
        var theFile = getFileByGtidResponse.Content!.SearchResults.SingleOrDefault();

        Assert.That(theFile, Is.Not.Null, $"{recipient.Identity} is missing file in the feed");
        Assert.That(theFile!.FileMetadata.AppData.DataType, Is.EqualTo(expectedFileMetadata.AppData.DataType));
        // Assert.That(theFile.FileMetadata.AppData.Content, Is.EqualTo(updatedEncryptedMetadataContent64));
        // Assert.That(theFile.FileMetadata.SenderOdinId, Is.EqualTo(collabChannel));
        // Assert.That(theFile.FileMetadata.OriginalAuthor, Is.EqualTo(member1));
    }

    private static async Task<(ApiResponse<TransitResult> response, UploadFileMetadata uploadedMetadata, TestPayloadDefinition payload1)>
        AwaitPostNewEncryptedFileOverPeerDirect(
            OwnerSession sender,
            TargetDrive collabChannelDrive,
            OwnerSession collabChannel,
            KeyHeader keyHeader)
    {
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.Content = "some content here";
        uploadedFileMetadata.AllowDistribution = true;
        uploadedFileMetadata.AppData.DataType = 7779;
        uploadedFileMetadata.AccessControlList = AccessControlList.Connected;
        var payload1 = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();

        payload1.Iv = ByteArrayUtil.GetRndByteArray(16);
        var payload2 = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2();
        payload2.Iv = ByteArrayUtil.GetRndByteArray(16);

        var testPayloads = new List<TestPayloadDefinition>()
        {
            payload1,
            payload2
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        //member1 sends a file to the recipient
        var (response, _) = await sender.V1.PeerDirect.TransferNewEncryptedFile(collabChannelDrive,
            uploadedFileMetadata, [collabChannel.Identity], null, uploadManifest,
            testPayloads, keyHeader: keyHeader);

        await sender.Sync.DrainOutboxAsync();

        return (response, uploadedFileMetadata, payload1);
    }

    private static async Task SetupScenario(
        OwnerSession collabChannel, OwnerSession member1, OwnerSession member2, TargetDrive collabChannelDrive)
    {
        await CollabScenario.PrepareScenarioAsync(
            collabChannel,
            [member1, member2],
            collabChannelDrive,
            DrivePermission.Write,
            "Test channel drive 001",
            allowAnonymousReads: true);

        foreach (var member in new[] { member1, member2 })
        {
            var follow = await member.V1.Follower.FollowIdentity(
                collabChannel.Identity, FollowerNotificationType.AllNotifications);
            Assert.That(follow.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        }
    }
}
