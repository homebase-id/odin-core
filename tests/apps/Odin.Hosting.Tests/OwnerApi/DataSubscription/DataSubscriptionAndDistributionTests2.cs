using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Services.Authorization.Acl;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Storage;
using Odin.Hosting.Controllers;
using Odin.Hosting.Tests.OwnerApi.ApiClient;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;

namespace Odin.Hosting.Tests.OwnerApi.DataSubscription;

[TestFixture]
public class DataSubscriptionAndDistributionTests2
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


    [Test]
    public async Task EncryptedFile_UploadedByTheOwner_IsOnlyDistributedTo_ConnectedFollowers_WithAccessInFileAcl_Of_A_SingleCircle()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);
        var merryOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Merry);
        var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

        //
        // Frodo, Sam, Merry, and Pippin are connected
        //
        await _scaffold.Scenarios.CreateConnectedHobbits(TargetDrive.NewTargetDrive());

        //
        // Sam, Merry, and Pippin follow Frodo
        // 
        await samOwnerClient.OwnerFollower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);
        await merryOwnerClient.OwnerFollower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);
        await pippinOwnerClient.OwnerFollower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        //scenarioContext.AppContexts[TestIdentities.Frodo.OdinId]

        //create a channel drive
        var frodoSecureChannel = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await frodoOwnerClient.Drive.CreateDrive(frodoSecureChannel, "A Secured channel Drive", "", allowAnonymousReads: false, ownerOnly: false,
            allowSubscriptions: true);

        //
        // Frodo creates a circle named Mordor and puts Sam in it
        //
        var circle = await frodoOwnerClient.Membership.CreateCircle("Mordor", frodoSecureChannel, DrivePermission.Read);
        await frodoOwnerClient.Network.GrantCircle(circle.Id, TestIdentities.Samwise);

        //
        // Frodo Uploads a video to his feed for the Mordor circle
        //
        const string headerContent = "I'm Mr. Underhill; I think";
        const string payloadContent = "this could be a photo of me";
        var (firstUploadResult, encryptedJsonContent64, encryptedPayloadContent64) =
            await UploadStandardEncryptedFileToChannel(frodoOwnerClient, frodoSecureChannel, headerContent, payloadContent, circle.Id);

        // Process the outbox since we're sending an encrypted file
        await frodoOwnerClient.Transit.WaitForEmptyOutbox(frodoSecureChannel);

        //
        // The header is distributed to the feed drive of Sam
        // Sam can get the payload via transit query
        // 

        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);
        await AssertFeedDriveHasHeader(samOwnerClient, firstUploadResult, encryptedJsonContent64);
        await AssertCanGetPayload(samOwnerClient, TestIdentities.Frodo, firstUploadResult, encryptedPayloadContent64);

        //
        // The header is NOT distributed to the feed drive of Merry and Pippin
        // Merry and Pippin Cannot get the payload via transit query
        //
        await pippinOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);
        await AssertFeedDrive_Does_Not_HaveHeader(pippinOwnerClient, firstUploadResult, encryptedJsonContent64);
        await AssertCan_Not_GetPayload(pippinOwnerClient, TestIdentities.Frodo, firstUploadResult);

        await merryOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);
        await AssertFeedDrive_Does_Not_HaveHeader(merryOwnerClient, firstUploadResult, encryptedJsonContent64);
        await AssertCan_Not_GetPayload(merryOwnerClient, TestIdentities.Frodo, firstUploadResult);

        //

        //All done
        await samOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
        await merryOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
        await pippinOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
        await _scaffold.Scenarios.DisconnectHobbits();
    }

    [Test]
    public async Task
        EncryptedFile_UploadedByTheOwner_IsOnlyDistributedTo_ConnectedFollowers_WithAccessInFileAcl_Of_A_SingleCircle_And_Deleted_When_Owner_Deletes_File()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);
        var merryOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Merry);
        var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

        //
        // Frodo, Sam, Merry, and Pippin are connected
        //
        await _scaffold.Scenarios.CreateConnectedHobbits(TargetDrive.NewTargetDrive());

        //
        // Sam follows Frodo
        // 
        await samOwnerClient.OwnerFollower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        //scenarioContext.AppContexts[TestIdentities.Frodo.OdinId]

        //create a channel drive
        var frodoSecureChannel = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await frodoOwnerClient.Drive.CreateDrive(frodoSecureChannel, "A Secured channel Drive", "", allowAnonymousReads: false, ownerOnly: false,
            allowSubscriptions: true);

        //
        // Frodo creates a circle named Mordor and puts Sam in it
        //
        var circle = await frodoOwnerClient.Membership.CreateCircle("Mordor", frodoSecureChannel, DrivePermission.All);
        await frodoOwnerClient.Network.GrantCircle(circle.Id, TestIdentities.Samwise);

        //
        // Frodo Uploads a video to his feed for the Mordor circle
        //
        const string headerContent = "I'm Mr. Underhill; I think";
        const string payloadContent = "this could be a photo of me";
        var (uploadResult, encryptedJsonContent64, encryptedPayloadContent64) =
            await UploadStandardEncryptedFileToChannel(frodoOwnerClient, frodoSecureChannel, headerContent, payloadContent, circle.Id);

        // Process the outbox since we're sending an encrypted file
        await frodoOwnerClient.Transit.WaitForEmptyOutbox(frodoSecureChannel);

        //
        // The header is distributed to the feed drive of Sam
        // Sam can get the payload via transit query
        // 
        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);
        await AssertFeedDriveHasHeader(samOwnerClient, uploadResult, encryptedJsonContent64);
        await AssertCanGetPayload(samOwnerClient, TestIdentities.Frodo, uploadResult, encryptedPayloadContent64);

        //
        // The owner deletes the file
        //
        await frodoOwnerClient.Drive.DeleteFile(uploadResult.File);
        await frodoOwnerClient.Transit.WaitForEmptyOutbox(uploadResult.File.TargetDrive);
        await frodoOwnerClient.Transit.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive); // just in case
        await frodoOwnerClient.Transit.WaitForEmptyOutbox(SystemDriveConstants.FeedDrive); // just in case

        //
        // Sam's feed drive no longer has the header
        // Sam can not get the payload via transit query
        // 
        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);
        await AssertFeedDrive_HasDeletedFile(samOwnerClient, uploadResult);
        await AssertPayloadIs404(samOwnerClient, TestIdentities.Frodo, uploadResult);

        //

        //All done
        await samOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
        await merryOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
        await pippinOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
        await _scaffold.Scenarios.DisconnectHobbits();
    }
    

    private async Task AssertPayloadIs404(OwnerApiClient client, TestIdentity identity, UploadResult uploadResult)
    {
        var payloadResponse = await client.TransitQuery.GetPayload(new TransitGetPayloadRequest()
        {
            OdinId = identity.OdinId,
            File = uploadResult.File,
            Key = WebScaffold.PAYLOAD_KEY
        });

        Assert.IsTrue(payloadResponse.StatusCode == HttpStatusCode.NotFound);
    }

    private async Task AssertFeedDriveHasHeader(OwnerApiClient client, UploadResult uploadResult, string encryptedJsonContent64)
    {
        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        var batch = await client.Drive.QueryBatch(FileSystemType.Standard, qp);
        Assert.IsTrue(batch.SearchResults.Count() == 1, $"Batch size should be 1 but was {batch.SearchResults.Count()}");
        var originalFile = batch.SearchResults.First();
        Assert.IsTrue(originalFile.FileState == FileState.Active);
        Assert.IsTrue(originalFile.FileMetadata.AppData.Content == encryptedJsonContent64);
        Assert.IsTrue(originalFile.FileMetadata.GlobalTransitId == uploadResult.GlobalTransitId);
    }

    private async Task AssertCanGetPayload(OwnerApiClient client, TestIdentity identity, UploadResult uploadResult, string encryptedPayloadContent64)
    {
        var payloadResponse = await client.TransitQuery.GetPayload(new TransitGetPayloadRequest()
        {
            OdinId = identity.OdinId,
            File = uploadResult.File,
            Key = WebScaffold.PAYLOAD_KEY
        });

        Assert.IsTrue(payloadResponse.IsSuccessStatusCode);
        Assert.IsNotNull(payloadResponse.Content, "payload content is null");
        var bytes = await payloadResponse.Content.ReadAsByteArrayAsync();
        Assert.IsTrue(bytes.Length > 0);
        Assert.IsTrue(bytes.ToBase64() == encryptedPayloadContent64);
    }

    private async Task AssertFeedDrive_Does_Not_HaveHeader(OwnerApiClient client, UploadResult uploadResult, string encryptedJsonContent64)
    {
        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        var batch = await client.Drive.QueryBatch(FileSystemType.Standard, qp);
        Assert.IsFalse(batch.SearchResults.Any(), $"Batch size should be 0 but was {batch.SearchResults.Count()}");
    }

    private async Task AssertCan_Not_GetPayload(OwnerApiClient client, TestIdentity identity, UploadResult uploadResult)
    {
        var payloadResponse = await client.TransitQuery.GetPayload(new TransitGetPayloadRequest()
        {
            OdinId = identity.OdinId,
            File = uploadResult.File,
            Key = WebScaffold.PAYLOAD_KEY
        });

        Assert.IsTrue(payloadResponse.StatusCode == HttpStatusCode.Forbidden);
    }

    private async Task AssertFeedDrive_HasDeletedFile(OwnerApiClient client, UploadResult uploadResult)
    {
        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        var batch = await client.Drive.QueryBatch(FileSystemType.Standard, qp);
        Assert.IsNotNull(batch.SearchResults.SingleOrDefault(c => c.FileState == FileState.Deleted));
    }

    private async Task<(UploadResult uploadResult, string encryptedJsonContent64, string encryptedPayloadContent64)> UploadStandardEncryptedFileToChannel(
        OwnerApiClient client,
        TargetDrive targetDrive,
        string headerContent,
        string payloadContent,
        Guid aclCircleId)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            IsEncrypted = false,
            AppData = new()
            {
                Content = headerContent,
                GroupId = default,
                Tags = default
            },
            AccessControlList = new AccessControlList()
            {
                CircleIdList = new List<Guid>() { aclCircleId },
                RequiredSecurityGroup = SecurityGroupType.ConfirmConnected
            }
        };

        var testPayloads = new List<TestPayloadDefinition>()
        {
            new()
            {
                Iv = ByteArrayUtil.GetRndByteArray(16),
                Key = WebScaffold.PAYLOAD_KEY,
                ContentType = "text/plain",
                Content = payloadContent.ToUtf8ByteArray(),
                Thumbnails = new List<ThumbnailContent>() { }
            }
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var uploadResponse = await client.DriveRedux.UploadNewEncryptedFile(targetDrive, fileMetadata, uploadManifest, testPayloads);
        var uploadResult = uploadResponse.response.Content;
        return (uploadResult, uploadResponse.encryptedJsonContent64, uploadResponse.uploadedPayloads.First().EncryptedContent64);
    }

    private async Task<UploadResult> OverwriteStandardFile(OwnerApiClient client, ExternalFileIdentifier overwriteFile, string uploadedContent, int fileType,
        Guid versionTag)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            IsEncrypted = false,
            VersionTag = versionTag,
            AppData = new()
            {
                Content = uploadedContent,
                FileType = fileType,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.OwnerOnly
        };

        return await client.Drive.UploadFile(FileSystemType.Standard, overwriteFile.TargetDrive, fileMetadata, overwriteFileId: overwriteFile.FileId);
    }
}