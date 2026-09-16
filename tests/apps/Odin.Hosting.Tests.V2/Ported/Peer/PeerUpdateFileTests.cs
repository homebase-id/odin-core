using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Hosting.Tests._Universal.ApiClient.Follower;
using Odin.Hosting.Tests._Universal.ApiClient.Peer.Direct;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Configuration;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Port of <c>_Universal/Peer/DirectSend/PeerUpdateFileTests.cs</c>. Covers updating a file that
/// lives on <i>another</i> identity's drive (<c>FileUpdateInstructionSet.Locale = Peer</c>): header
/// rewrite plus a payload delete and a payload add, and — for a collaborative channel drive — the
/// resulting change reaching a follower's feed drive.
/// </summary>
/// <remarks>
/// Checked port.
/// <list type="bullet">
/// <item>The original stacked three <c>[TestCaseSource]</c> attributes per test, two of them
/// commented out (<c>AppWithOnlyUseTransitWrite</c>, <c>GuestNotAllowed</c>). Only the live
/// <c>OwnerAllowed</c> row is ported; the other two are kept as comments on <see cref="UpdateCases"/>.</item>
/// <item>The original's <c>OwnerClientContext(TargetDrive.NewTargetDrive())</c> carried a drive it
/// never used — the caller is the owner, who needs no grant. <see cref="V2Fixture.SetupCallerWithOwner"/>
/// does create that drive; it is inert here for the same reason, since every drive the tests read or
/// write is created explicitly below.</item>
/// <item>Every <c>WaitForEmptyOutbox</c> / <c>WaitForEmptyInbox</c> becomes an explicit
/// <c>Sync.DrainOutboxAsync()</c> / <c>Sync.ProcessInboxAsync(drive)</c>: the V1 calls are passive
/// polls on the outbox background service, which the fast host registers but never starts.</item>
/// <item>Trailing disconnect / unfollow calls were cleanup only and are dropped — per-test reset
/// covers them.</item>
/// </list>
/// </remarks>
[TestFixture]
public class PeerUpdateFileTests : V2Fixture
{
    private static readonly Dictionary<string, string> IsCollaborativeChannelAttributes = new()
        { { BuiltInDriveAttributes.IsCollaborativeChannel, bool.TrueString } };

    // Other Tests
    // Bad Requests - fail when missing payload operation type, invalid upload manifest

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Pippin];

    public static IEnumerable<object[]> UpdateCases()
    {
        // The original also declared, commented out:
        //   AppPermissionKeysOnly(UseTransitWrite)                     -> HttpStatusCode.OK
        //   ConnectedIdentityLoggedInOnGuestApi(ReadWhoIFollow)        -> HttpStatusCode.MethodNotAllowed
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(UpdateCases))]
    public async Task CanUpdateRemoteFileFileUpdateHeaderDeletePayloadAndAddNewPayload_PeerOnly(
        CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, senderOwnerClient) = await SetupCallerWithOwner(spec, Identities.Pippin);
        var recipientOwnerClient = await LoginAsOwner(Identities.Frodo);

        var sender = senderOwnerClient.Identity;
        var recipient = recipientOwnerClient.Identity;

        var remoteTargetDrive = TargetDrive.NewTargetDrive();
        await recipientOwnerClient.Admin.CreateDrive(remoteTargetDrive, "Test Drive 001", allowAnonymousReads: true,
            allowSubscriptions: true,
            attributes: IsCollaborativeChannelAttributes);

        var cid = Guid.NewGuid();
        var permissions = TestUtils.CreatePermissionGrantRequest(remoteTargetDrive, DrivePermission.Write);
        await recipientOwnerClient.Admin.CreateCircle(cid, "circle with some access", permissions);

        await senderOwnerClient.Connections.SendConnectionRequest(recipient);
        await recipientOwnerClient.Connections.AcceptConnectionRequest(sender, [cid]);

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AllowDistribution = true;
        uploadedFileMetadata.AppData.DataType = 555;
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
        var response = await senderOwnerClient.V1.PeerDirect.TransferNewFile(remoteTargetDrive, uploadedFileMetadata, [recipient], null,
            uploadManifest,
            testPayloads);
        await senderOwnerClient.Sync.DrainOutboxAsync();
        Assert.That(response.IsSuccessStatusCode, Is.True);

        await recipientOwnerClient.Sync.ProcessInboxAsync(remoteTargetDrive, int.MaxValue);

        //
        // Update the file via pippin's identity
        //

        var remoteTargetFile = response.Content.RemoteGlobalTransitIdFileIdentifier.ToFileIdentifier();

        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here";
        updatedFileMetadata.AppData.DataType = 777;

        var payloadToAdd = SamplePayloadDefinitions.GetPayloadDefinition1();
        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Peer,

            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = remoteTargetFile,
            Recipients = [recipient],
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
        await senderOwnerClient.Sync.DrainOutboxAsync();
        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(expected));

        await recipientOwnerClient.Sync.ProcessInboxAsync(remoteTargetDrive, int.MaxValue);

        // Let's test more
        if (expected != HttpStatusCode.OK)
        {
            return;
        }

        var uploadResult = updateFileResponse.Content;
        Assert.That(uploadResult, Is.Not.Null);

        //
        // Recipient should have the updated file
        //
        var getHeaderResponse =
            await recipientOwnerClient.V1.Drive.QueryByGlobalTransitId(remoteTargetFile.ToGlobalTransitIdFileIdentifier());
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

        var recipientFile = new ExternalFileIdentifier()
        {
            FileId = header.FileId,
            TargetDrive = header.TargetDrive
        };

        //
        // Ensure payloadToAdd add is added
        //
        var getPayloadToAddResponse = await recipientOwnerClient.V1.Drive.GetPayload(recipientFile, payloadToAdd.Key);
        Assert.That(getPayloadToAddResponse.IsSuccessStatusCode, Is.True);
        Assert.That(getPayloadToAddResponse.ContentHeaders!.LastModified.HasValue, Is.True);
        Assert.That(getPayloadToAddResponse.ContentHeaders.LastModified.GetValueOrDefault(),
            Is.LessThan(DateTimeOffset.Now.AddSeconds(10)));

        var content = (await getPayloadToAddResponse.Content.ReadAsStreamAsync()).ToByteArray();
        Assert.That(content, Is.EqualTo(payloadToAdd.Content));

        // Check all the thumbnails
        foreach (var thumbnail in payloadToAdd.Thumbnails)
        {
            var getThumbnailResponse = await recipientOwnerClient.V1.Drive.GetThumbnail(recipientFile,
                thumbnail.PixelWidth, thumbnail.PixelHeight, payloadToAdd.Key);

            Assert.That(getThumbnailResponse.IsSuccessStatusCode, Is.True);
            Assert.That(getThumbnailResponse.ContentHeaders!.LastModified.HasValue, Is.True);
            Assert.That(getThumbnailResponse.ContentHeaders.LastModified.GetValueOrDefault(),
                Is.LessThan(DateTimeOffset.Now.AddSeconds(10)));

            var thumbContent = (await getThumbnailResponse.Content.ReadAsStreamAsync()).ToByteArray();
            Assert.That(thumbContent, Is.EqualTo(thumbnail.Content),
                $"thumbnail {thumbnail.PixelWidth}x{thumbnail.PixelHeight}");
        }

        //
        // Ensure we get payload2 for the payload1
        //
        var getPayload2Response = await recipientOwnerClient.V1.Drive.GetPayload(recipientFile, payload2.Key);
        Assert.That(getPayload2Response.IsSuccessStatusCode, Is.True);

        //
        // Ensure we get 404 for the payload1
        //
        var getPayload1Response = await recipientOwnerClient.V1.Drive.GetPayload(recipientFile, payload1.Key);
        Assert.That(getPayload1Response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        //
        // Ensure we find the file on the recipient
        //
        var searchResponse = await recipientOwnerClient.V1.Drive.QueryBatch(new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1()
            {
                TargetDrive = recipientFile.TargetDrive,
                DataType = [updatedFileMetadata.AppData.DataType]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        Assert.That(searchResponse.IsSuccessStatusCode, Is.True);
        var theFileSearchResult = searchResponse.Content.SearchResults.SingleOrDefault();
        Assert.That(theFileSearchResult, Is.Not.Null);
        Assert.That(theFileSearchResult.FileId, Is.EqualTo(recipientFile.FileId));
    }

    [Test, TestCaseSource(nameof(UpdateCases))]
    public async Task CanUpdateRemoteEncryptedFile_AndSeeChangesDistributedToFeed(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, member1_OwnerClient) = await SetupCallerWithOwner(spec, Identities.Pippin);
        var collabChannelOwnerClient = await LoginAsOwner(Identities.Frodo);
        var member2_OwnerClient = await LoginAsOwner(Identities.Sam);

        await DisableAutoAcceptIntroductions(collabChannelOwnerClient);
        await DisableAutoAcceptIntroductions(member1_OwnerClient);
        await DisableAutoAcceptIntroductions(member2_OwnerClient);

        var member1 = member1_OwnerClient.Identity;
        var collabChannel = collabChannelOwnerClient.Identity;
        var member2 = member2_OwnerClient.Identity;

        var collabChannelDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        await collabChannelOwnerClient.Admin.CreateDrive(collabChannelDrive, "Test channel drive 001", allowAnonymousReads: true,
            allowSubscriptions: true,
            attributes: IsCollaborativeChannelAttributes);

        var collabChannelId = Guid.NewGuid();
        var permissions = TestUtils.CreatePermissionGrantRequest(collabChannelDrive, DrivePermission.Write);
        await collabChannelOwnerClient.Admin.CreateCircle(collabChannelId, "circle with some access", permissions);

        await member1_OwnerClient.Connections.SendConnectionRequest(collabChannel);
        await collabChannelOwnerClient.Connections.AcceptConnectionRequest(member1, [collabChannelId]);

        await member2_OwnerClient.Connections.SendConnectionRequest(collabChannel);
        await collabChannelOwnerClient.Connections.AcceptConnectionRequest(member2, [collabChannelId]);
        await member2_OwnerClient.V1.Follower.FollowIdentity(collabChannel, FollowerNotificationType.AllNotifications);

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.Content = "some content here";
        uploadedFileMetadata.AllowDistribution = true;
        uploadedFileMetadata.AppData.DataType = 1234;
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
        var (response, _) = await member1_OwnerClient.V1.PeerDirect.TransferNewEncryptedFile(collabChannelDrive,
            uploadedFileMetadata, [collabChannel], null, uploadManifest,
            testPayloads, keyHeader: keyHeader);
        await member1_OwnerClient.Sync.DrainOutboxAsync();
        Assert.That(response.IsSuccessStatusCode, Is.True);

        //
        // Update the file via pippin's identity
        //

        await collabChannelOwnerClient.Sync.ProcessInboxAsync(collabChannelDrive, int.MaxValue);
        await collabChannelOwnerClient.Sync.DrainOutboxAsync();

        await member1_OwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);
        await member2_OwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var remoteTargetFile = response.Content.RemoteGlobalTransitIdFileIdentifier.ToFileIdentifier();

        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here";
        updatedFileMetadata.AppData.DataType = 5678;

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
        var (updateFileResponse, updatedEncryptedMetadataContent64, _, _) =
            await caller.V1.Drive.UpdateEncryptedFile(updateInstructionSet, updatedFileMetadata, [payloadToAdd], keyHeader);
        await member1_OwnerClient.Sync.DrainOutboxAsync();
        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(expected));

        // Let's test more
        if (expected != HttpStatusCode.OK)
        {
            return;
        }

        var uploadResult = updateFileResponse.Content;
        Assert.That(uploadResult, Is.Not.Null);

        await collabChannelOwnerClient.Sync.ProcessInboxAsync(collabChannelDrive, int.MaxValue);
        await collabChannelOwnerClient.Sync.DrainOutboxAsync(); //waiting for distribution to occur

        // file should be on the feed of those connected
        var globalTransitIdFileIdentifier = new GlobalTransitIdFileIdentifier()
        {
            GlobalTransitId = remoteTargetFile.GlobalTransitId.GetValueOrDefault(),
            TargetDrive = WellKnownAppDrives.FeedDrive
        };

        await member2_OwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var channelOnMembersFeedDrive = await member2_OwnerClient.V1.Drive.QueryByGlobalTransitId(globalTransitIdFileIdentifier);
        Assert.That(channelOnMembersFeedDrive.IsSuccessStatusCode, Is.True);
        var theFileOnFeedDrive = channelOnMembersFeedDrive.Content.SearchResults.SingleOrDefault();
        Assert.That(theFileOnFeedDrive, Is.Not.Null);

        Assert.That(theFileOnFeedDrive.FileMetadata.AppData.DataType, Is.EqualTo(updatedFileMetadata.AppData.DataType));
        Assert.That(theFileOnFeedDrive.FileMetadata.AppData.Content, Is.EqualTo(updatedEncryptedMetadataContent64));
        Assert.That(theFileOnFeedDrive.FileMetadata.SenderOdinId, Is.EqualTo((string)collabChannel));
        Assert.That(theFileOnFeedDrive.FileMetadata.OriginalAuthor, Is.EqualTo(member1));
    }

    [Test, TestCaseSource(nameof(UpdateCases))]
    public async Task CanUpdateRemoteFile_AndSeeChangesDistributedToFeed(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, member1_OwnerClient) = await SetupCallerWithOwner(spec, Identities.Pippin);
        var collabChannelOwnerClient = await LoginAsOwner(Identities.Frodo);
        var member2_OwnerClient = await LoginAsOwner(Identities.Sam);

        await DisableAutoAcceptIntroductions(collabChannelOwnerClient);
        await DisableAutoAcceptIntroductions(member1_OwnerClient);
        await DisableAutoAcceptIntroductions(member2_OwnerClient);

        var member1 = member1_OwnerClient.Identity;
        var collabChannel = collabChannelOwnerClient.Identity;
        var member2 = member2_OwnerClient.Identity;

        var collabChannelDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        await collabChannelOwnerClient.Admin.CreateDrive(collabChannelDrive, "Test channel drive 001", allowAnonymousReads: true,
            allowSubscriptions: true,
            attributes: IsCollaborativeChannelAttributes);

        var collabChannelId = Guid.NewGuid();
        var permissions = TestUtils.CreatePermissionGrantRequest(collabChannelDrive, DrivePermission.Write);
        await collabChannelOwnerClient.Admin.CreateCircle(collabChannelId, "circle with some access", permissions);

        await member1_OwnerClient.Connections.SendConnectionRequest(collabChannel);
        await collabChannelOwnerClient.Connections.AcceptConnectionRequest(member1, [collabChannelId]);

        await member2_OwnerClient.Connections.SendConnectionRequest(collabChannel);
        await collabChannelOwnerClient.Connections.AcceptConnectionRequest(member2, [collabChannelId]);
        await member2_OwnerClient.V1.Follower.FollowIdentity(collabChannel, FollowerNotificationType.AllNotifications);

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
        var response = await member1_OwnerClient.V1.PeerDirect.TransferNewFile(collabChannelDrive, uploadedFileMetadata, [collabChannel], null,
            uploadManifest,
            testPayloads);
        await member1_OwnerClient.Sync.DrainOutboxAsync();
        Assert.That(response.IsSuccessStatusCode, Is.True);

        // the collab channel we get the file from the TransferNewFile and we need to wait for it to send it out to all followers
        await collabChannelOwnerClient.Sync.ProcessInboxAsync(collabChannelDrive, int.MaxValue);
        await collabChannelOwnerClient.Sync.DrainOutboxAsync(); //waiting for distribution to occur

        //
        // Update the file via pippin's identity
        //

        await member1_OwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);
        await member2_OwnerClient.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var remoteTargetFile = response.Content.RemoteGlobalTransitIdFileIdentifier.ToFileIdentifier();

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

        var updateFileResponse = await caller.V1.Drive.UpdateFile(updateInstructionSet, updatedFileMetadata, [payloadToAdd]);
        await member1_OwnerClient.Sync.DrainOutboxAsync();
        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(expected));

        // the collab channel we get the file from the UpdateFile and we need to wait for it to send it out to all followers
        await collabChannelOwnerClient.Sync.ProcessInboxAsync(collabChannelDrive, int.MaxValue);
        await collabChannelOwnerClient.Sync.DrainOutboxAsync(); //waiting for distribution to occur

        // Let's test more
        if (expected != HttpStatusCode.OK)
        {
            return;
        }

        var uploadResult = updateFileResponse.Content;
        Assert.That(uploadResult, Is.Not.Null);

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
        Assert.That(theFileOnFeedDrive.FileMetadata.OriginalAuthor, Is.EqualTo(member1));
    }

    private static Task DisableAutoAcceptIntroductions(OwnerSession owner)
        => owner.Admin.UpdateTenantSettingsFlag(
            TenantConfigFlagNames.DisableAutoAcceptIntroductionsForTests, bool.TrueString);

    /// <summary>
    /// V1 clients not yet bundled on <see cref="V1Handles"/>. Built from one session's own
    /// identity+factory pair so a caller's identity can never be paired with another's factory.
    /// </summary>

}
