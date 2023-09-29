using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.DataSubscription.Follower;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Peer;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Hosting.Controllers;
using Odin.Hosting.Tests.OwnerApi.ApiClient;

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

    [Test]
    public async Task EncryptedFile_UploadedByTheOwner_IsOnlyDistributedTo_ConnectedFollowers()
    {
        const int fileType = 2112;
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);
        var merryOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Merry);
        var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

        //
        // Frodo, Sam, Merry, and Pippin are connected
        //
        var scenarioContext = await _scaffold.Scenarios.CreateConnectedHobbits(TargetDrive.NewTargetDrive());

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

        await frodoOwnerClient.Drive.CreateDrive(frodoSecureChannel, "A Secured channel Drive", "", allowAnonymousReads: false, ownerOnly: false, allowSubscriptions: true);

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
        var (firstUploadResult, encryptedJsonContent64, encryptedPayloadContent64) =
            await UploadStandardEncryptedFileToChannel(frodoOwnerClient, frodoSecureChannel, headerContent, payloadContent, fileType);

        // Process the outbox since we're sending an encrypted file
        await frodoOwnerClient.Transit.ProcessOutbox(1);
        // await frodoOwnerClient.Cron.DistributeFeedFiles();

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
        await AssertFeedDrive_Does_Note_HavHeader(pippinOwnerClient, firstUploadResult, encryptedJsonContent64);
        await AssertCan_Not_GetPayload(pippinOwnerClient, TestIdentities.Frodo, firstUploadResult);

        await merryOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);
        await AssertFeedDrive_Does_Note_HavHeader(merryOwnerClient, firstUploadResult, encryptedJsonContent64);
        await AssertCan_Not_GetPayload(merryOwnerClient, TestIdentities.Frodo, firstUploadResult);

        //

        //All done
        await samOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
        await merryOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
        await pippinOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
        await _scaffold.Scenarios.DisconnectHobbits();
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
        Assert.IsTrue(originalFile.FileMetadata.AppData.JsonContent == encryptedJsonContent64);
        Assert.IsTrue(originalFile.FileMetadata.GlobalTransitId == uploadResult.GlobalTransitId);
    }

    private async Task AssertCanGetPayload(OwnerApiClient client, TestIdentity identity, UploadResult uploadResult, string encryptedPayloadContent64)
    {
        var payloadResponse = await client.TransitQuery.GetPayload(new TransitExternalFileIdentifier()
        {
            OdinId = identity.OdinId,
            File = uploadResult.File
        });

        Assert.IsTrue(payloadResponse.IsSuccessStatusCode);
        Assert.IsNotNull(payloadResponse.Content);
        var bytes = await payloadResponse.Content.ReadAsByteArrayAsync();
        Assert.IsTrue(bytes.Length > 0);
        Assert.IsTrue(bytes.ToBase64() == encryptedPayloadContent64);
    }

    private async Task AssertFeedDrive_Does_Note_HavHeader(OwnerApiClient client, UploadResult uploadResult, string encryptedJsonContent64)
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
        var payloadResponse = await client.TransitQuery.GetPayload(new TransitExternalFileIdentifier()
        {
            OdinId = identity.OdinId,
            File = uploadResult.File
        });

        Assert.IsTrue(payloadResponse.StatusCode == HttpStatusCode.Forbidden);
    }

    private async Task<(UploadResult uploadResult, string encryptedJsonContent64, string encryptedPayloadContent64)> UploadStandardEncryptedFileToChannel(
        OwnerApiClient client,
        TargetDrive targetDrive,
        string headerContent,
        string payloadContent,
        int fileType)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            AppData = new()
            {
                ContentIsComplete = string.IsNullOrEmpty(payloadContent),
                JsonContent = headerContent,
                FileType = fileType,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.Connected
        };

        return await client.Drive.UploadEncryptedFile(FileSystemType.Standard, targetDrive, fileMetadata, payloadContent);
    }

    private async Task<UploadResult> OverwriteStandardFile(OwnerApiClient client, ExternalFileIdentifier overwriteFile, string uploadedContent, int fileType,
        Guid versionTag)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            VersionTag = versionTag,
            AppData = new()
            {
                ContentIsComplete = true,
                JsonContent = uploadedContent,
                FileType = fileType,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.OwnerOnly
        };

        return await client.Drive.UploadFile(FileSystemType.Standard, overwriteFile.TargetDrive, fileMetadata, overwriteFileId: overwriteFile.FileId);
    }
}