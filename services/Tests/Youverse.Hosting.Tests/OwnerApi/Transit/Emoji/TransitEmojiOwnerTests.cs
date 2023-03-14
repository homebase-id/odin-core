using System;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Core.Storage;
using Youverse.Hosting.Tests.OwnerApi.ApiClient;

namespace Youverse.Hosting.Tests.OwnerApi.Transit.Emoji
{
    public class TransitEmojiOwnerTests
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
        public async Task CanSendAndGetEmojisOverTransit()
        {
            var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
            var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

            //create a channel drive
            var pippinChannelDrive = new TargetDrive()
            {
                Alias = Guid.NewGuid(),
                Type = SystemDriveConstants.ChannelDriveType
            };

            await pippinOwnerClient.Drive.CreateDrive(pippinChannelDrive, "A Channel Drive", "", false, ownerOnly: false);

            await samOwnerClient.Follower.FollowIdentity(pippinOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);
            var samFollowingPippinDefinition = await pippinOwnerClient.Follower.GetFollower(samOwnerClient.Identity);
            
            Assert.IsNotNull(samFollowingPippinDefinition);
            //
            // Pippin uploads a post
            //
            var uploadedContent = "I'm Hungry";
            var uploadResult = await UploadToChannel(pippinOwnerClient, pippinChannelDrive, uploadedContent);

            //
            // Sam adds reaction from Sam's feed to Pippin's channel
            //
            await samOwnerClient.Transit.AddReaction(pippinOwnerClient.Identity,
                uploadResult.GlobalTransitIdFileIdentifier,
                ":smile:");

            var response = await samOwnerClient.Transit.GetAllReactions(pippinOwnerClient.Identity, new GetRemoteReactionsRequest()
            {
                File = uploadResult.GlobalTransitIdFileIdentifier,
                Cursor = 0,
                MaxRecords = 100
            });

            Assert.IsTrue(response.Reactions.Count == 1);
        }

        private async Task<UploadResult> UploadToChannel(OwnerApiClient client, TargetDrive targetDrive, string uploadedContent, bool allowDistribution = true)
        {
            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = allowDistribution,
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

            return await client.Drive.UploadFile(FileSystemType.Standard, targetDrive, fileMetadata);
        }

        private async Task<UploadResult> UploadComment(OwnerApiClient client, TargetDrive targetDrive, GlobalTransitIdFileIdentifier referencedFile,
            string commentContent, bool allowDistribution)
        {
            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = allowDistribution,
                ContentType = "application/json",
                PayloadIsEncrypted = false,

                //indicates the file about which this file is giving feed back
                ReferencedFile = referencedFile,

                AppData = new()
                {
                    ContentIsComplete = true,
                    JsonContent = commentContent,
                    FileType = default,
                    GroupId = default,
                    Tags = default
                },
                AccessControlList = AccessControlList.OwnerOnly
            };

            return await client.Drive.UploadFile(FileSystemType.Comment, targetDrive, fileMetadata);
        }
    }
}