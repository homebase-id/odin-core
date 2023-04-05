using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.DriveCore.Query;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Workers.DefaultCron;
using Youverse.Core.Services.Workers.FeedDistributionApp;
using Youverse.Core.Storage;
using Youverse.Hosting.Tests.OwnerApi.ApiClient;

namespace Youverse.Hosting.Tests.OwnerApi.DataSubscription;

public class DataSubscriptionTestsWhenFileUploadByOwner
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
    public async Task UnencryptedStandardFile_UploadedByOwner_DistributedToConnectedIdentities_ThatFollowOwner()
    {
        const int fileType = 10144;

        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        //create a channel drive
        var frodoChannelDrive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await frodoOwnerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", allowAnonymousReads: false, ownerOnly: false,
            allowSubscriptions: true);

        await frodoOwnerClient.Network.SendConnectionRequest(samOwnerClient.Identity, new List<GuidId>() { });
        await samOwnerClient.Network.AcceptConnectionRequest(frodoOwnerClient.Identity, new List<GuidId>() { });

        // Sam to follow everything from frodo
        await samOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var uploadResult = await UploadStandardUnencryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        await frodoOwnerClient.Cron.DistributeFeedFiles();

        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            // FileType = new List<int>() { fileType }
            GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
        Assert.IsTrue(batch.SearchResults.Count() == 1, $"Count should be 1 but was {batch.SearchResults.Count()}");
        var theFile = batch.SearchResults.First();
        Assert.IsTrue(theFile.FileState == FileState.Active);
        Assert.IsTrue(theFile.FileMetadata.AppData.JsonContent == uploadedContent);
        Assert.IsTrue(theFile.FileMetadata.GlobalTransitId == uploadResult.GlobalTransitId);

        //All done

        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);
        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task UnencryptedStandardFile_UploadedByOwner_Distributed_ToFollowers_That_AreNotConnected()
    {
        const int fileType = 10144;

        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        //create a channel drive
        var frodoChannelDrive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await frodoOwnerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", allowAnonymousReads: false, ownerOnly: false,
            allowSubscriptions: true);

        // Sam to follow everything from frodo
        await samOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var uploadResult = await UploadStandardUnencryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        //Tell Frodo's identity to process the feed outbox
        // await frodoOwnerClient.Cron.DistributeFeedFiles();

        //It should be direct write
        // Sam should have the same content on his feed drive
        // await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);

        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
        Assert.IsTrue(batch.SearchResults.Count() == 1, $"Count should be 1 but was {batch.SearchResults.Count()}");
        var theFile = batch.SearchResults.First();
        Assert.IsTrue(theFile.FileState == FileState.Active);
        Assert.IsTrue(theFile.FileMetadata.AppData.JsonContent == uploadedContent);
        Assert.IsTrue(theFile.FileMetadata.GlobalTransitId == uploadResult.GlobalTransitId);

        //All done
        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task EncryptedStandardFile_UploadedByOwner_DistributedToConnectedIdentities_ThatFollowOwner()
    {
        const int fileType = 11344;

        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        //create a channel drive
        var frodoChannelDrive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await frodoOwnerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", allowAnonymousReads: false, ownerOnly: false,
            allowSubscriptions: true);

        await frodoOwnerClient.Network.SendConnectionRequest(samOwnerClient.Identity, new List<GuidId>() { });
        await samOwnerClient.Network.AcceptConnectionRequest(frodoOwnerClient.Identity, new List<GuidId>() { });

        // Sam to follow everything from frodo
        await samOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var (uploadResult, encryptedJsonContent64) = await UploadStandardEncryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        //Process the outbox since we're sending an encrypted file
        await frodoOwnerClient.Transit.ProcessOutbox(1);


        await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);

        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            // FileType = new List<int>() { fileType }
            GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
        Assert.IsTrue(batch.SearchResults.Count() == 1, $"Count should be 1 but was {batch.SearchResults.Count()}");
        var theFile = batch.SearchResults.First();
        Assert.IsTrue(theFile.FileState == FileState.Active);
        Assert.IsTrue(theFile.FileMetadata.AppData.JsonContent == encryptedJsonContent64);
        Assert.IsTrue(theFile.FileMetadata.GlobalTransitId == uploadResult.GlobalTransitId);

        //All done

        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);
        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task EncryptedStandardFile_UploadedByOwner_Distributed_ToFollowers_That_AreNotConnected_Fails()
    {
        const int fileType = 11355;

        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        //create a channel drive
        var frodoChannelDrive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await frodoOwnerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", allowAnonymousReads: false, ownerOnly: false,
            allowSubscriptions: true);
        
        // Sam to follow everything from frodo
        await samOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var (uploadResult, encryptedJsonContent64) = await UploadStandardEncryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        //Process the outbox since we're sending an encrypted file
        await frodoOwnerClient.Transit.ProcessOutbox(1);
        
        await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);

        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            // FileType = new List<int>() { fileType }
            GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
        Assert.IsTrue(!batch.SearchResults.Any(), $"Count should be 0 but was {batch.SearchResults.Count()}");

        //All done

        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    private async Task<UploadResult> UploadStandardUnencryptedFileToChannel(OwnerApiClient client, TargetDrive targetDrive, string uploadedContent,
        int fileType)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            AppData = new()
            {
                ContentIsComplete = true,
                JsonContent = uploadedContent,
                FileType = fileType,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.Connected
        };

        return await client.Drive.UploadFile(FileSystemType.Standard, targetDrive, fileMetadata, "");
    }

    private async Task<(UploadResult uploadResult, string encryptedJsonContent64)> UploadStandardEncryptedFileToChannel(OwnerApiClient client,
        TargetDrive targetDrive, string uploadedContent,
        int fileType)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            AppData = new()
            {
                ContentIsComplete = true,
                JsonContent = uploadedContent,
                FileType = fileType,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.Connected
        };

        return await client.Drive.UploadEncryptedFile(FileSystemType.Standard, targetDrive, fileMetadata, "");
    }

    private async Task<UploadResult> OverwriteStandardFile(OwnerApiClient client, ExternalFileIdentifier overwriteFile, string uploadedContent, int fileType)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            ContentType = "application/json",
            PayloadIsEncrypted = false,
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