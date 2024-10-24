// using System;
// using System.Reflection;
// using System.Threading.Tasks;
// using NUnit.Framework;
// using Odin.Services.Authorization.Acl;
// using Odin.Services.DataSubscription.Follower;
// using Odin.Services.Drive;
// using Odin.Services.Transit;
// using Odin.Services.Transit.Upload;
// using Odin.Hosting.Tests.OwnerApi.ApiClient;
//
// namespace Odin.Hosting.Tests.OwnerApi.Reactions;
//
// public class ReactionDistributionTests
// {
//     private WebScaffold _scaffold;
//
//     [OneTimeSetUp]
//     public void OneTimeSetUp()
//     {
//         var folder = GetType().Name;
//         _scaffold = new WebScaffold(folder);
//         _scaffold.RunBeforeAnyTests();
//     }
//
//     [OneTimeTearDown]
//     public void OneTimeTearDown()
//     {
//         _scaffold.RunAfterAnyTests();
//     }
//
//     [Test]
//     public async Task CanPostReactionAndDistributeViaTransitUsingOwnerClient()
//     {
//         Assert.Inconclusive("WIP");
//         var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
//         var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);
//
//         //create a channel drive
//         var frodoChannelDrive = new TargetDrive()
//         {
//             Alias = Guid.NewGuid(),
//             Type = SystemDriveConstants.ChannelDriveType
//         };
//
//         await frodoOwnerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", false, false);
//
//         //
//         // Sam to follow frodo
//         //
//         await samOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);
//
//         // Frodo uploads content to channel drive
//         var uploadedContent = "I'm Mr. Underhill";
//         var uploadResult = await UploadToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent);
//
//         //
//         // Frodo posts feedback to his post
//         //
//         var feedbackContent = "Indeed, Indeed I am Mr. Underhill";
//         var targetReferenceFile = uploadResult.File;
//         var reactionUploadResult = await UploadFeedback(frodoOwnerClient, frodoChannelDrive, targetReferenceFile, feedbackContent, false);
//
//         Assert.IsTrue(uploadResult.File.TargetDrive == reactionUploadResult.File.TargetDrive);
//
//         //Sam posts reaction from his ownerclient, which should send file to frodo over transit
//         // samOwnerClient
//     }
//
//     [Test]
//     public async Task CanPostFeedbackAndDistributeViaTransitUsingYouAuthClient()
//     {
//         Assert.Inconclusive("WIP");
//
//         var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
//         var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);
//
//         //create a channel drive
//         var frodoChannelDrive = new TargetDrive()
//         {
//             Alias = Guid.NewGuid(),
//             Type = SystemDriveConstants.ChannelDriveType
//         };
//
//         await frodoOwnerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", false, false);
//
//         //
//         // Sam to follow frodo
//         //
//         await samOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);
//
//         // Frodo uploads content to channel drive
//         var uploadedContent = "I'm Mr. Underhill";
//         var uploadResult = await UploadToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent);
//
//         //
//         // Frodo posts feedback to his post
//         //
//         var reactionContent = "Indeed, Indeed I am Mr. Underhill";
//         var targetReferenceFile = uploadResult.File;
//         var feedbackUploadResult = await UploadFeedback(frodoOwnerClient, frodoChannelDrive, targetReferenceFile, reactionContent, false);
//
//         Assert.IsTrue(uploadResult.File.TargetDrive == feedbackUploadResult.File.TargetDrive);
//
//         // Sam posts feedback using YouAuh on frodo's drive
//         //TODO: Build a youauth API client
//         
//     }
//
//     private async Task<UploadResult> UploadToChannel(OwnerApiClient client, TargetDrive targetDrive, string uploadedContent, bool allowDistribution = true)
//     {
//         var fileMetadata = new UploadFileMetadata()
//         {
//             AllowDistribution = allowDistribution,
//             ContentType = "application/json",
//             PayloadIsEncrypted = false,
//             AppData = new()
//             {
//                 ContentIsComplete = true,
//                 JsonContent = uploadedContent,
//                 FileType = default,
//                 GroupId = default,
//                 Tags = default
//             },
//             AccessControlList = AccessControlList.OwnerOnly
//         };
//
//         return await client.Drive.UploadMetadataFile(targetDrive, fileMetadata);
//     }
//
//     private async Task<UploadResult> UploadFeedback(OwnerApiClient client, TargetDrive targetDrive, ExternalFileIdentifier targetFile, string reactionContent, bool allowDistribution)
//     {
//         var fileMetadata = new UploadFileMetadata()
//         {
//             AllowDistribution = allowDistribution,
//             ContentType = "application/json",
//             PayloadIsEncrypted = false,
//
//             //indicates the file about which this file is giving feed back
//             ReferencedFile = targetFile,
//
//             AppData = new()
//             {
//                 ContentIsComplete = true,
//                 JsonContent = reactionContent,
//                 FileType = default,
//                 GroupId = default,
//                 Tags = default
//             },
//             AccessControlList = AccessControlList.OwnerOnly
//         };
//
//         return await client.Drive.UploadReactionFile(targetDrive, fileMetadata);
//     }
// }