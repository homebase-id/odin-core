using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Core.Storage;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests.AppAPI.ApiClient;

namespace Odin.Hosting.Tests.AppAPI.Transit.Reactions
{
    public class AppTransitReactionSenderPublicFiles
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
        public async Task AppCan_SendAndGet_Public_ReactionContent()
        {
            // Prep
            var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
            var remoteDrive = await pippinOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some target drive", "", allowAnonymousReads: true);

            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitRead);

            // Pippin uploads file
            var targetFile = await UploadStandardRandomPublicFileHeader(pippinOwnerClient.Identity, remoteDrive.TargetDriveInfo);

            const string reactionContent = "I dunno and stuff";

            var request = new TransitAddReactionRequest()
            {
                OdinId = pippinOwnerClient.Identity.OdinId,
                Request = new AddRemoteReactionRequest()
                {
                    File = targetFile.uploadResult.GlobalTransitIdFileIdentifier,
                    Reaction = reactionContent
                }
            };

            //
            // Send the reaction - TODO: this fails because there's no default access to WriteReactionsAndComments for anonymous drives
            //
            var addReactionResponse = await merryAppClient.TransitReactionSender.AddReaction(request);
            Assert.IsTrue(addReactionResponse.IsSuccessStatusCode);

            //
            // Validate reaction exists
            //
            var getReactionsResponse = await merryAppClient.TransitReactionSender.GetAllReactions(new TransitGetReactionsRequest()
            {
                OdinId = pippinOwnerClient.Identity.OdinId,
                Request = new GetRemoteReactionsRequest()
                {
                    File = targetFile.uploadResult.GlobalTransitIdFileIdentifier,
                    Cursor = default,
                    MaxRecords = 100,
                }
            });


            Assert.IsTrue(getReactionsResponse.IsSuccessStatusCode);
            Assert.IsNotNull(getReactionsResponse.Content);
            var theReaction = getReactionsResponse.Content.Reactions.SingleOrDefault(sr =>
                sr.GlobalTransitIdFileIdentifier == targetFile.uploadResult.GlobalTransitIdFileIdentifier);

            Assert.IsNotNull(theReaction);
            Assert.IsTrue(theReaction.ReactionContent == reactionContent);
        }

        [Test]
        [Description("Shows that we do not allow WriteReactionsAndComments to anonymous drives by default")]
        public async Task AppFails_SendReactionContent_ToAnonymousDriveWithout_WriteReactionsAndComments()
        {
            // Prep
            var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
            var remoteDrive = await pippinOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some target drive", "", allowAnonymousReads: true);

            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitRead);

            // Pippin uploads file
            var targetFile = await UploadStandardRandomPublicFileHeader(pippinOwnerClient.Identity, remoteDrive.TargetDriveInfo);

            const string reactionContent = "I dunno and stuff";

            var request = new TransitAddReactionRequest()
            {
                OdinId = pippinOwnerClient.Identity.OdinId,
                Request = new AddRemoteReactionRequest()
                {
                    File = targetFile.uploadResult.GlobalTransitIdFileIdentifier,
                    Reaction = reactionContent
                }
            };

            //
            // Send the reaction
            //
            var addReactionResponse = await merryAppClient.TransitReactionSender.AddReaction(request);
            Assert.IsTrue(addReactionResponse.StatusCode == HttpStatusCode.Forbidden, $"Status code was {addReactionResponse.StatusCode}");
        }
        //

        private async Task<AppApiClient> CreateAppAndClient(TestIdentity identity, params int[] permissionKeys)
        {
            var appId = Guid.NewGuid();

            var ownerClient = _scaffold.CreateOwnerApiClient(identity);

            var appDrive = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some Drive 1", "", false);

            var appPermissionsGrant = new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = appDrive.TargetDriveInfo,
                            Permission = DrivePermission.All
                        }
                    }
                },
                PermissionSet = new PermissionSet(permissionKeys)
            };

            await ownerClient.Apps.RegisterApp(appId, appPermissionsGrant);

            var client = _scaffold.CreateAppClient(identity, appId);
            return client;
        }

        private async Task<(UploadResult uploadResult, UploadFileMetadata uploadedMetadata)> UploadStandardRandomPublicFileHeader(TestIdentity identity,
            TargetDrive targetDrive, string payload = null, ImageDataContent thumbnail = null)
        {
            var client = _scaffold.CreateOwnerApiClient(identity);
            var fileMetadata = new UploadFileMetadata()
            {
                ContentType = "text/plain",
                PayloadIsEncrypted = false,
                AppData = new()
                {
                    FileType = 777,
                    JsonContent = $"some json content {Guid.NewGuid()}",
                    ContentIsComplete = payload == null,
                    UniqueId = Guid.NewGuid(),
                    AdditionalThumbnails = thumbnail == null ? default : new[] { thumbnail }
                },
                AccessControlList = AccessControlList.Anonymous
            };

            var result = await client.Drive.UploadFile(FileSystemType.Standard, targetDrive, fileMetadata,
                payloadData: payload ?? "",
                thumbnail: thumbnail);
            return (result, fileMetadata);
        }

        private async Task<(UploadResult uploadResult, ClientFileMetadata modifiedMetadata)> ModifyFile(TestIdentity identity, ExternalFileIdentifier file)
        {
            var client = _scaffold.CreateOwnerApiClient(identity);

            var header = await client.Drive.GetFileHeader(FileSystemType.Standard, file);

            var fileMetadata = new UploadFileMetadata()
            {
                ContentType = "text/plain",
                PayloadIsEncrypted = false,
                AppData = new()
                {
                    FileType = 777,
                    JsonContent = header.FileMetadata.AppData.JsonContent + " something i appended",
                    ContentIsComplete = true
                },
                VersionTag = header.FileMetadata.VersionTag,
                AccessControlList = AccessControlList.Anonymous
            };

            var result = await client.Drive.UploadFile(FileSystemType.Standard, file.TargetDrive, fileMetadata, overwriteFileId: file.FileId);

            var modifiedFile = await client.Drive.GetFileHeader(FileSystemType.Standard, file);
            return (result, modifiedFile.FileMetadata);
        }
    }
}