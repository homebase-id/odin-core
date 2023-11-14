using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.DataSubscription.Follower;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Core.Storage;
using Odin.Hosting.Tests.OwnerApi.ApiClient;

namespace Odin.Hosting.Tests.OwnerApi.Transit.ReactionContent
{
    public class TransitReactionContentOwnerTestsConnectedReactions
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
        public async Task ConnectedIdentity_CanSendAndGetAllReactions_OverTransit_ForPublicChannel_WithNoCircles()
        {
            const string reactionContent = ":cake:";

            var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
            var pippinChannelDrive = new TargetDrive()
            {
                Alias = Guid.NewGuid(),
                Type = SystemDriveConstants.ChannelDriveType
            };

            await pippinOwnerClient.Drive.CreateDrive(pippinChannelDrive, "A Channel Drive", "", false, ownerOnly: false, allowSubscriptions: true);

            var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);
            await samOwnerClient.OwnerFollower.FollowIdentity(pippinOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

            var targetCircle = await pippinOwnerClient.Membership.CreateCircle("Garden channel circle", new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = pippinChannelDrive,
                            Permission = DrivePermission.ReadWrite
                        }
                    }
                }
            });

            await samOwnerClient.Network.SendConnectionRequestTo(pippinOwnerClient.Identity, new List<GuidId>() { });
            await pippinOwnerClient.Network.AcceptConnectionRequest(samOwnerClient.Identity, new List<GuidId>() { targetCircle.Id });

            // var samFollowingPippinDefinition = await pippinOwnerClient.Follower.GetFollower(samOwnerClient.Identity);
            // Assert.IsNotNull(samFollowingPippinDefinition);

            //
            // Pippin uploads a post
            //
            var uploadedContent = "I'm Hungry!";
            var uploadResult = await UploadUnencryptedContentToChannel(pippinOwnerClient, pippinChannelDrive, uploadedContent);

            //
            // Sam adds reaction from Sam's feed to Pippin's channel
            //
            await samOwnerClient.Transit.AddReaction(pippinOwnerClient.Identity,
                uploadResult.GlobalTransitIdFileIdentifier,
                reactionContent);

            var response = await samOwnerClient.Transit.GetAllReactions(pippinOwnerClient.Identity, new GetRemoteReactionsRequest()
            {
                File = uploadResult.GlobalTransitIdFileIdentifier,
                Cursor = 0,
                MaxRecords = 100
            });

            Assert.IsTrue(response.Reactions.Count == 1);
            var theReaction = response.Reactions.SingleOrDefault();
            Assert.IsTrue(theReaction!.ReactionContent == reactionContent);
            Assert.IsTrue(theReaction!.GlobalTransitIdFileIdentifier == uploadResult.GlobalTransitIdFileIdentifier);

            await pippinOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
            await samOwnerClient.Network.DisconnectFrom(pippinOwnerClient.Identity);
        }

        [Test]
        public async Task ConnectedIdentity_CanSendAndDeleteReactionsOverTransit_ForPublicChannel_WithNoCircles()
        {
            const string reactionContent = ":cake:";

            var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
            var pippinChannelDrive = new TargetDrive()
            {
                Alias = Guid.NewGuid(),
                Type = SystemDriveConstants.ChannelDriveType
            };

            await pippinOwnerClient.Drive.CreateDrive(pippinChannelDrive, "A Channel Drive", "", false, ownerOnly: false, allowSubscriptions: true);

            var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);
            await samOwnerClient.OwnerFollower.FollowIdentity(pippinOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

            var targetCircle = await pippinOwnerClient.Membership.CreateCircle("Garden channel circle", new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = pippinChannelDrive,
                            Permission = DrivePermission.ReadWrite
                        }
                    }
                }
            });

            await samOwnerClient.Network.SendConnectionRequestTo(pippinOwnerClient.Identity, new List<GuidId>() { });
            await pippinOwnerClient.Network.AcceptConnectionRequest(samOwnerClient.Identity, new List<GuidId>() { targetCircle.Id });

            // var samFollowingPippinDefinition = await pippinOwnerClient.Follower.GetFollower(samOwnerClient.Identity);
            // Assert.IsNotNull(samFollowingPippinDefinition);

            //
            // Pippin uploads a post
            //
            var uploadedContent = "I'm Hungry!";
            var uploadResult = await UploadUnencryptedContentToChannel(pippinOwnerClient, pippinChannelDrive, uploadedContent);

            
            //
            // Sam adds reaction from Sam's feed to Pippin's channel
            //
            await samOwnerClient.Transit.AddReaction(pippinOwnerClient.Identity,
                uploadResult.GlobalTransitIdFileIdentifier,
                reactionContent);

            var response = await samOwnerClient.Transit.GetAllReactions(pippinOwnerClient.Identity, new GetRemoteReactionsRequest()
            {
                File = uploadResult.GlobalTransitIdFileIdentifier,
                Cursor = 0,
                MaxRecords = 100
            });

            Assert.IsTrue(response.Reactions.Count == 1);
            var theReaction = response.Reactions.SingleOrDefault();
            Assert.IsTrue(theReaction!.ReactionContent == reactionContent);

            // now delete it
            await samOwnerClient.Transit.DeleteReaction(pippinOwnerClient.Identity, reactionContent, uploadResult.GlobalTransitIdFileIdentifier);

            var shouldBeDeletedResponse = await samOwnerClient.Transit.GetAllReactions(pippinOwnerClient.Identity, new GetRemoteReactionsRequest()
            {
                File = uploadResult.GlobalTransitIdFileIdentifier,
                Cursor = 0,
                MaxRecords = 100
            });

            Assert.IsTrue(shouldBeDeletedResponse.Reactions.Count == 0);

            await pippinOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
            await samOwnerClient.Network.DisconnectFrom(pippinOwnerClient.Identity);
        }

        [Test]
        public Task ConnectedIdentity_CanSendAndDeleteAllReactionsOnFile_ReactionsOverTransit_ForPublicChannel_WithNoCircles()
        {
            Assert.Inconclusive("TODO DeleteAllReactionsOnFile");
            return Task.CompletedTask;
        }

        [Test]
        public Task ConnectedIdentity_CanSendAndGetReactionCountsByFile_ReactionsOverTransit_ForPublicChannel_WithNoCircles()
        {
            Assert.Inconclusive("TODO GetReactionCountsByFile");
            return Task.CompletedTask;
        }

        [Test]
        public Task ConnectedIdentity_CanSendAnd_GetReactionsByIdentity_ReactionsOverTransit_ForPublicChannel_WithNoCircles()
        {
            Assert.Inconclusive("TODO GetReactionsByIdentity");
            return Task.CompletedTask;
        }

        private async Task<UploadResult> UploadUnencryptedContentToChannel(OwnerApiClient client, TargetDrive targetDrive, string uploadedContent, bool allowDistribution = true,
            AccessControlList acl = null)
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
                AccessControlList = acl ?? AccessControlList.Connected
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