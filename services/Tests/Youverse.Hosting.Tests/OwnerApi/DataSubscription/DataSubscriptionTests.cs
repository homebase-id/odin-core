using System;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Core.Query;
using Youverse.Core.Services.Drive.Core.Storage;
using Youverse.Core.Services.Drives.Base.Upload;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Tests.AppAPI.ChatStructure.Api;
using Youverse.Hosting.Tests.OwnerApi.ApiClient;

namespace Youverse.Hosting.Tests.OwnerApi.DataSubscription;

public class DataSubscriptionTests
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
    public async Task CanUploadToDriveAndDistributeToFollower()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        //create a channel drive
        var frodoChannelDrive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await frodoOwnerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", false, false);

        // Sam to follow everything from frodo
        await samOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var uploadResult = await UploadToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent);

        // Sam should have the same content on his feed drive
        await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);

        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
        };
        
        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
        Assert.IsTrue(batch.SearchResults.Count() == 1);
        var theFile = batch.SearchResults.First();
        Assert.IsTrue(theFile.FileState == FileState.Active);
        Assert.IsTrue(theFile.FileMetadata.AppData.JsonContent == uploadedContent);
        Assert.IsTrue(theFile.FileMetadata.GlobalTransitId == uploadResult.GlobalTransitId);
        
        //All done
        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    private async Task<UploadResult> UploadToChannel(OwnerApiClient client, TargetDrive targetDrive, string uploadedContent)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            AppData = new()
            {
                ContentIsComplete = true,
                JsonContent = uploadedContent,
                FileType = default,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.OwnerOnly
        };

        return await client.Drive.UploadStandardFileMetadata(targetDrive, fileMetadata);
    }
}