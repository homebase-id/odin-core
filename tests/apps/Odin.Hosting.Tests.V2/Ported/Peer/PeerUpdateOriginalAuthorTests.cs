using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.ApiClient.Follower;
using Odin.Hosting.Tests._Universal.ApiClient.Peer.Direct;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Configuration;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Port of <c>_Universal/Peer/DirectSend/PeerUpdateOriginalAuthorTests.cs</c>. A collaborative
/// channel post is updated by an identity that is <i>not</i> its original author; the copy that
/// reaches a follower's feed drive must carry the channel as <c>SenderOdinId</c> and the original
/// author — not the editor — as <c>OriginalAuthor</c>.
/// </summary>
/// <remarks>
/// Checked port.
/// <list type="bullet">
/// <item>The original stacked three <c>[TestCaseSource]</c> attributes per test, two commented out
/// (<c>AppWithOnlyUseTransitWrite</c>, <c>GuestNotAllowed</c>). Only the live <c>OwnerAllowed</c> row
/// is ported; the others are kept as comments on <see cref="UpdateCases"/>.</item>
/// <item>Identities are remapped onto the four this framework exposes. The originals used
/// <c>Collab</c> / <c>TomBombadil</c>, which are structurally identical to the rest — the V2 README's
/// identity rule says the names are arbitrary. Each test's <i>acting</i> identity is named explicitly
/// via <see cref="V2Fixture.SetupCallerWithOwner"/> rather than conveyed by <c>HostIdentities</c>
/// ordering.</item>
/// <item>The original's <c>OwnerClientContext(TargetDrive.NewTargetDrive())</c> carried a drive it
/// never used; <c>SetupCallerWithOwner</c> creates it, inert, for the same reason.</item>
/// <item>Every <c>Task.Delay</c> / <c>WaitForEmptyOutbox</c> / <c>WaitForEmptyInbox</c> becomes an
/// explicit <c>Sync.DrainOutboxAsync()</c> / <c>Sync.ProcessInboxAsync(drive)</c>; the V1 forms poll
/// an outbox background service the fast host registers but never starts.</item>
/// <item>Trailing disconnect / unfollow calls were cleanup only and are dropped.</item>
/// </list>
/// Carried defects, left alone: in both tests the <c>OriginalAuthor</c> assertion's failure message
/// printed <c>SenderOdinId</c>, not the original author it was about (in the second test it was
/// labelled "sender was" outright). Those messages restated the comparison, so per the README's
/// assertion rule they are dropped here rather than corrected — NUnit prints both sides.
/// </remarks>
[TestFixture]
public class PeerUpdateOriginalAuthorTests : V2Fixture
{
    private static readonly Dictionary<string, string> IsCollaborativeChannelAttributes = new()
        { { BuiltInDriveAttributes.IsCollaborativeChannel, bool.TrueString } };

    // Other Tests
    // Bad Requests - fail when missing payload operation type, invalid upload manifest

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Merry, Identities.Pippin];

    public static IEnumerable<object[]> UpdateCases()
    {
        // The original also declared, commented out:
        //   AppPermissionKeysOnly(UseTransitWrite)                -> HttpStatusCode.OK
        //   ConnectedIdentityLoggedInOnGuestApi(ReadWhoIFollow)   -> HttpStatusCode.MethodNotAllowed
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(UpdateCases))]
    public async Task CanUpdateRemoteEncryptedFile_FromIdentityOtherThanOriginalAuthor_AndSeeChangesDistributedToFeed(
        CallerSpec spec, HttpStatusCode expected)
    {
        // The caller (the acting identity) is the secondary author.
        var (caller, secondaryAuthor_OwnerClient) = await SetupCallerWithOwner(spec, Identities.Merry);
        var collabChannelOwnerClient = await LoginAsOwner(Identities.Frodo);
        var originalAuthor_OwnerClient = await LoginAsOwner(Identities.Pippin);
        var member2_OwnerClient = await LoginAsOwner(Identities.Sam);

        await DisableAutoAcceptIntroductions(collabChannelOwnerClient);
        await DisableAutoAcceptIntroductions(originalAuthor_OwnerClient);
        await DisableAutoAcceptIntroductions(member2_OwnerClient);
        await DisableAutoAcceptIntroductions(secondaryAuthor_OwnerClient);

        var originalAuthor = originalAuthor_OwnerClient.Identity;
        var collabChannel = collabChannelOwnerClient.Identity;
        var secondaryAuthor = secondaryAuthor_OwnerClient.Identity;
        var member2 = member2_OwnerClient.Identity;

        var collabChannelDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        await collabChannelOwnerClient.Admin.CreateDrive(collabChannelDrive, "Test channel drive 001", allowAnonymousReads: true,
            allowSubscriptions: true,
            attributes: IsCollaborativeChannelAttributes);

        //
        // get everyone connected and in a circle for the collab channel
        //
        var collabChannelId = Guid.NewGuid();
        var permissions = TestUtils.CreatePermissionGrantRequest(collabChannelDrive, DrivePermission.Write);
        await collabChannelOwnerClient.Admin.CreateCircle(collabChannelId, "circle with some access", permissions);

        Assert.That((await originalAuthor_OwnerClient.Connections.SendConnectionRequest(collabChannel)).IsSuccessStatusCode, Is.True);
        Assert.That((await collabChannelOwnerClient.Connections.AcceptConnectionRequest(originalAuthor, [collabChannelId]))
            .IsSuccessStatusCode, Is.True);

        Assert.That((await secondaryAuthor_OwnerClient.Connections.SendConnectionRequest(collabChannel)).IsSuccessStatusCode, Is.True);
        Assert.That((await collabChannelOwnerClient.Connections.AcceptConnectionRequest(secondaryAuthor, [collabChannelId]))
            .IsSuccessStatusCode, Is.True);

        Assert.That((await member2_OwnerClient.Connections.SendConnectionRequest(collabChannel)).IsSuccessStatusCode, Is.True);
        Assert.That((await collabChannelOwnerClient.Connections.AcceptConnectionRequest(member2, [collabChannelId]))
            .IsSuccessStatusCode, Is.True);

        await member2_OwnerClient.V1.Follower.FollowIdentity(collabChannel, FollowerNotificationType.AllNotifications);

        //
        // original author makes a post (upload metadata)
        //
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.DataType = 333;
        uploadedFileMetadata.AppData.Content = "some content here";
        uploadedFileMetadata.AppData.FileType = 100;
        uploadedFileMetadata.AllowDistribution = true;
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

        var keyHeader = KeyHeader.NewRandom16();

        //Pippin sends a file to the recipient
        var (originalFileUpload, _) = await originalAuthor_OwnerClient.V1.PeerDirect.TransferNewEncryptedFile(collabChannelDrive,
            uploadedFileMetadata, [collabChannel], null, uploadManifest,
            testPayloads, keyHeader: keyHeader);

        await originalAuthor_OwnerClient.Sync.DrainOutboxAsync();
        Assert.That(originalFileUpload.IsSuccessStatusCode, Is.True);

        await collabChannelOwnerClient.Sync.ProcessInboxAsync(collabChannelDrive, int.MaxValue);

        // When the collab channel gets the file, we need to wait for feed distribution to occur
        await collabChannelOwnerClient.Sync.DrainOutboxAsync();

        //
        // Update the file via pippin's identity
        //

        await originalAuthor_OwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);
        await secondaryAuthor_OwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);
        await member2_OwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var remoteTargetFile = originalFileUpload.Content.RemoteGlobalTransitIdFileIdentifier.ToFileIdentifier();

        var globalTransitIdFileIdentifierOnFeed = new GlobalTransitIdFileIdentifier()
        {
            GlobalTransitId = remoteTargetFile.GlobalTransitId.GetValueOrDefault(),
            TargetDrive = WellKnownAppDrives.FeedDrive
        };

        //
        // validate member2 got the file before we update it
        //

        await collabChannelOwnerClient.Sync.DrainOutboxAsync();
        await member2_OwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var member2FileOnFeedBeforeUpdateResponse =
            await member2_OwnerClient.V1.Drive.QueryByGlobalTransitId(globalTransitIdFileIdentifierOnFeed);
        Assert.That(member2FileOnFeedBeforeUpdateResponse.IsSuccessStatusCode, Is.True);
        var theFileOnFeedDriveBeforeUpdate = member2FileOnFeedBeforeUpdateResponse.Content.SearchResults.SingleOrDefault();
        Assert.That(theFileOnFeedDriveBeforeUpdate, Is.Not.Null);

        //
        //
        //

        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here";
        updatedFileMetadata.AppData.DataType = 444;
        updatedFileMetadata.AppData.UniqueId = Guid.Parse("00000000-0000-0000-0000-111111111111");

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
        var (updateFileResponse, updatedEncryptedMetadataContent64, _, _) = await caller.V1.Drive.UpdateEncryptedFile(
            updateInstructionSet,
            updatedFileMetadata,
            [payloadToAdd],
            keyHeader);

        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(expected));
        await secondaryAuthor_OwnerClient.Sync.DrainOutboxAsync();

        // Let's test more
        if (expected != HttpStatusCode.OK)
        {
            return;
        }

        var uploadResult = updateFileResponse.Content;
        Assert.That(uploadResult, Is.Not.Null);

        await collabChannelOwnerClient.Sync.ProcessInboxAsync(collabChannelDrive, int.MaxValue);
        await collabChannelOwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);
        //waiting for distribution to occur
        await collabChannelOwnerClient.Sync.DrainOutboxAsync();

        await member2_OwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var tempTempDriveStatus = await member2_OwnerClient.V1.Drive.GetDriveStatus(SystemDriveConstants.TransientTempDrive);
        Assert.That(tempTempDriveStatus.Content, Is.Not.Null);
        Assert.That(tempTempDriveStatus.Content.Outbox.TotalItems, Is.EqualTo(0));
        Assert.That(tempTempDriveStatus.Content.Inbox.TotalItems, Is.EqualTo(0));

        var feedDriveStatus = await member2_OwnerClient.V1.Drive.GetDriveStatus(WellKnownAppDrives.FeedDrive);
        Assert.That(feedDriveStatus.Content, Is.Not.Null);
        Assert.That(feedDriveStatus.Content.Outbox.TotalItems, Is.EqualTo(0));
        Assert.That(feedDriveStatus.Content.Inbox.TotalItems, Is.EqualTo(0));

        var channelOnMembersFeedDrive =
            await member2_OwnerClient.V1.Drive.QueryByGlobalTransitId(globalTransitIdFileIdentifierOnFeed);
        Assert.That(channelOnMembersFeedDrive.IsSuccessStatusCode, Is.True);
        var theFileOnFeedDrive = channelOnMembersFeedDrive.Content.SearchResults.SingleOrDefault();
        Assert.That(theFileOnFeedDrive, Is.Not.Null);

        Assert.That(theFileOnFeedDrive.FileMetadata.AppData.DataType, Is.EqualTo(updatedFileMetadata.AppData.DataType));
        Assert.That(theFileOnFeedDrive.FileMetadata.AppData.Content, Is.EqualTo(updatedEncryptedMetadataContent64));
        Assert.That(theFileOnFeedDrive.FileMetadata.SenderOdinId, Is.EqualTo((string)collabChannel));
        Assert.That(theFileOnFeedDrive.FileMetadata.OriginalAuthor, Is.EqualTo(originalAuthor));
    }

    [Test, TestCaseSource(nameof(UpdateCases))]
    public async Task CanUpdateRemoteFile_FromIdentityOtherThanOriginalAuthor_AndSeeChangesDistributedToFeed(
        CallerSpec spec, HttpStatusCode expected)
    {
        // The caller (the acting identity) is the original author in this variant.
        var (caller, originalAuthor_OwnerClient) = await SetupCallerWithOwner(spec, Identities.Pippin);
        var secondaryAuthor_OwnerClient = await LoginAsOwner(Identities.Merry);
        var collabChannelOwnerClient = await LoginAsOwner(Identities.Frodo);
        var member2_OwnerClient = await LoginAsOwner(Identities.Sam);

        await DisableAutoAcceptIntroductions(collabChannelOwnerClient);
        await DisableAutoAcceptIntroductions(originalAuthor_OwnerClient);
        await DisableAutoAcceptIntroductions(member2_OwnerClient);
        await DisableAutoAcceptIntroductions(secondaryAuthor_OwnerClient);

        var originalAuthor = originalAuthor_OwnerClient.Identity;
        var collabChannel = collabChannelOwnerClient.Identity;
        var secondaryAuthor = secondaryAuthor_OwnerClient.Identity;
        var member2 = member2_OwnerClient.Identity;

        var collabChannelDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        await collabChannelOwnerClient.Admin.CreateDrive(collabChannelDrive, "Test channel drive 001", allowAnonymousReads: true,
            allowSubscriptions: true,
            attributes: IsCollaborativeChannelAttributes);

        var collabChannelId = Guid.NewGuid();
        var permissions = TestUtils.CreatePermissionGrantRequest(collabChannelDrive, DrivePermission.Write);
        await collabChannelOwnerClient.Admin.CreateCircle(collabChannelId, "circle with some access", permissions);

        await originalAuthor_OwnerClient.Connections.SendConnectionRequest(collabChannel);
        await collabChannelOwnerClient.Connections.AcceptConnectionRequest(originalAuthor, [collabChannelId]);

        await secondaryAuthor_OwnerClient.Connections.SendConnectionRequest(collabChannel);
        await collabChannelOwnerClient.Connections.AcceptConnectionRequest(secondaryAuthor, [collabChannelId]);

        await member2_OwnerClient.Connections.SendConnectionRequest(collabChannel);
        await collabChannelOwnerClient.Connections.AcceptConnectionRequest(member2, [collabChannelId]);
        await member2_OwnerClient.V1.Follower.FollowIdentity(collabChannel, FollowerNotificationType.AllNotifications);

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.DataType = 111;
        uploadedFileMetadata.AllowDistribution = true;
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
        var response = await originalAuthor_OwnerClient.V1.PeerDirect.TransferNewFile(collabChannelDrive, uploadedFileMetadata,
            [collabChannel], null,
            uploadManifest,
            testPayloads);
        await originalAuthor_OwnerClient.Sync.DrainOutboxAsync();
        Assert.That(response.IsSuccessStatusCode, Is.True);

        // wait for the collab channel to distribute feed
        await collabChannelOwnerClient.Sync.ProcessInboxAsync(collabChannelDrive, int.MaxValue);
        await collabChannelOwnerClient.Sync.DrainOutboxAsync();

        //
        // Update the file via pippin's identity
        //

        await originalAuthor_OwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive, int.MaxValue);
        await secondaryAuthor_OwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive, int.MaxValue);
        await member2_OwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive, int.MaxValue);

        var remoteTargetFile = response.Content.RemoteGlobalTransitIdFileIdentifier.ToFileIdentifier();

        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here";
        updatedFileMetadata.AppData.DataType = 222;

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

        var updateFileResponse = await caller.V1.Drive.UpdateFile(updateInstructionSet, updatedFileMetadata, [payloadToAdd]);
        await originalAuthor_OwnerClient.Sync.DrainOutboxAsync();
        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(expected));

        // Let's test more
        if (expected != HttpStatusCode.OK)
        {
            return;
        }

        var uploadResult = updateFileResponse.Content;
        Assert.That(uploadResult, Is.Not.Null);

        // handle any incoming feed items
        await collabChannelOwnerClient.Sync.ProcessInboxAsync(remoteTargetFile.TargetDrive, int.MaxValue);

        //
        // Recipient should have the updated file
        //
        var getHeaderResponse =
            await collabChannelOwnerClient.V1.Drive.QueryByGlobalTransitId(remoteTargetFile.ToGlobalTransitIdFileIdentifier());
        Assert.That(getHeaderResponse.IsSuccessStatusCode, Is.True);
        var header = getHeaderResponse.Content.SearchResults.SingleOrDefault();
        Assert.That(header, Is.Not.Null);
        Assert.That(header.FileMetadata.AppData.Content, Is.EqualTo(updatedFileMetadata.AppData.Content));
        Assert.That(header.FileMetadata.AppData.DataType, Is.EqualTo(updatedFileMetadata.AppData.DataType));
        Assert.That(header.FileMetadata.Payloads.Count(), Is.EqualTo(2));
        Assert.That(header.FileMetadata.Payloads.Select(pd => pd.Key), Does.Not.Contain(payload1.Key),
            "payload 1 should have been removed");
        Assert.That(header.FileMetadata.Payloads.Select(pd => pd.Key), Does.Contain(payload2.Key), "payload 2 should remain");
        Assert.That(header.FileMetadata.Payloads.Select(pd => pd.Key), Does.Contain(payloadToAdd.Key),
            "payloadToAdd should have been, well, added :)");

        // file should be on the feed of those connected
        var globalTransitIdFileIdentifier = new GlobalTransitIdFileIdentifier()
        {
            GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
            TargetDrive = WellKnownAppDrives.FeedDrive
        };

        await collabChannelOwnerClient.Sync.DrainOutboxAsync(); //waiting for distribution to occur
        await member2_OwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive, int.MaxValue);

        var channelOnMembersFeedDrive = await member2_OwnerClient.V1.Drive.QueryByGlobalTransitId(globalTransitIdFileIdentifier);
        Assert.That(channelOnMembersFeedDrive.IsSuccessStatusCode, Is.True);
        var theFileOnFeedDrive = channelOnMembersFeedDrive.Content.SearchResults.SingleOrDefault();
        Assert.That(theFileOnFeedDrive, Is.Not.Null);

        Assert.That(theFileOnFeedDrive.FileMetadata.AppData.Content, Is.EqualTo(updatedFileMetadata.AppData.Content));
        Assert.That(theFileOnFeedDrive.FileMetadata.AppData.DataType, Is.EqualTo(updatedFileMetadata.AppData.DataType));
        Assert.That(theFileOnFeedDrive.FileMetadata.SenderOdinId, Is.EqualTo((string)collabChannel));
        Assert.That(theFileOnFeedDrive.FileMetadata.OriginalAuthor, Is.EqualTo(originalAuthor));
    }

    private static Task DisableAutoAcceptIntroductions(OwnerSession owner)
        => owner.Admin.UpdateTenantSettingsFlag(
            TenantConfigFlagNames.DisableAutoAcceptIntroductionsForTests, bool.TrueString);

    /// <summary>
    /// V1 clients not yet bundled on <see cref="V1Handles"/>. Built from one session's own
    /// identity+factory pair so a caller's identity can never be paired with another's factory.
    /// </summary>

}
