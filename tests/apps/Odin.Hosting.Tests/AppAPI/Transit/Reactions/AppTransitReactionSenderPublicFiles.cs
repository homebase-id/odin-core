using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
using Odin.Core.Storage;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests.AppAPI.ApiClient;
using Odin.Core.Storage.Database.Identity.Abstractions;

namespace Odin.Hosting.Tests.AppAPI.Transit.Reactions
{
    public class AppTransitReactionSenderPublicFiles
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var folder = GetType().Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Merry, TestIdentities.Pippin });
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
        public async Task AppCan_SendAndGet_Public_ReactionContent()
        {
            // Prep
            var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitRead);

            var remoteDrive = await pippinOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some target drive", "", allowAnonymousReads: true);

            // Pippin uploads file
            var targetFile = await UploadStandardRandomPublicFileHeader(pippinOwnerClient.Identity, remoteDrive.TargetDriveInfo);

            const string reactionContent = ":k:";

            var request = new PeerAddReactionRequest()
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
            ClassicAssert.IsTrue(addReactionResponse.IsSuccessStatusCode);

            //
            // Validate reaction exists
            //
            var getReactionsResponse = await merryAppClient.TransitReactionSender.GetAllReactions(new PeerGetReactionsRequest()
            {
                OdinId = pippinOwnerClient.Identity.OdinId,
                Request = new GetRemoteReactionsRequest()
                {
                    File = targetFile.uploadResult.GlobalTransitIdFileIdentifier,
                    Cursor = default,
                    MaxRecords = 100,
                }
            });


            ClassicAssert.IsTrue(getReactionsResponse.IsSuccessStatusCode, $"status code was {getReactionsResponse.StatusCode}");
            ClassicAssert.IsNotNull(getReactionsResponse.Content);
            var theReaction = getReactionsResponse.Content.Reactions.SingleOrDefault(sr =>
                sr.GlobalTransitIdFileIdentifier == targetFile.uploadResult.GlobalTransitIdFileIdentifier);

            ClassicAssert.IsNotNull(theReaction);
            ClassicAssert.IsTrue(theReaction.ReactionContent == reactionContent);
        }

        [Test]
        public async Task AppFails_SendReactionContent_ToAnonymousDriveWithout_ReactPermission()
        {
            // Prep
            var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitRead);

            var remoteDrive = await pippinOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some target drive", "", allowAnonymousReads: true);

            // Pippin uploads file
            var targetFile = await UploadStandardRandomPublicFileHeader(pippinOwnerClient.Identity, remoteDrive.TargetDriveInfo);

            //
            // Turn off the flag that allows authenticated identities to react
            //
            await pippinOwnerClient.Configuration.UpdateTenantSettingsFlag(TenantConfigFlagNames.AuthenticatedIdentitiesCanReactOnAnonymousDrives,
                false.ToString());

            const string reactionContent = ":k:";
            var request = new PeerAddReactionRequest()
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
            ClassicAssert.IsTrue(addReactionResponse.StatusCode == HttpStatusCode.Forbidden, $"Status code was {addReactionResponse.StatusCode}");
        }

        [Test]
        public async Task AppCan_SendReactionContent_ToAnonymousDrive_With_ReactPermission()
        {
            // Prep
            var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitRead);

            var remoteDrive = await pippinOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some target drive", "", allowAnonymousReads: true);

            // Pippin uploads file
            var targetFile = await UploadStandardRandomPublicFileHeader(pippinOwnerClient.Identity, remoteDrive.TargetDriveInfo);

            //
            // Ensure the flag that allows authenticated identities to react is true
            //
            await pippinOwnerClient.Configuration.UpdateTenantSettingsFlag(TenantConfigFlagNames.AuthenticatedIdentitiesCanReactOnAnonymousDrives,
                true.ToString());

            const string reactionContent = ":k:";
            var request = new PeerAddReactionRequest()
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
            ClassicAssert.IsTrue(addReactionResponse.IsSuccessStatusCode, $"Status code was {addReactionResponse.StatusCode}");
        }
        //

        [Test]
        [Ignore("this test cannot be finalized until we decide to support allowing authenticated identities to send data over transit")]
        public async Task AppCan_AddCommentOn_AnonymousDrive_With_CommentPermission()
        {
            // Prep
            var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitWrite, PermissionKeys.UseTransitRead);

            var remoteDrive = await pippinOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some target drive", "", allowAnonymousReads: true);


            // Pippin uploads file
            var targetFile = await UploadStandardRandomPublicFileHeader(pippinOwnerClient.Identity, remoteDrive.TargetDriveInfo);

            //
            // Ensure the flag that allows authenticated identities to comment is true
            //
            await pippinOwnerClient.Configuration.UpdateTenantSettingsFlag(TenantConfigFlagNames.AuthenticatedIdentitiesCanCommentOnAnonymousDrives,
                true.ToString());

            var recipients = new List<string>() { pippinOwnerClient.Identity.OdinId };
            var commentFileMetadata = new UploadFileMetadata()
            {
                ReferencedFile = targetFile.uploadResult.GlobalTransitIdFileIdentifier,
                IsEncrypted = false,
                AppData = new()
                {
                    FileType = 777,
                    Content = "This is a Comment",
                    UniqueId = Guid.NewGuid(),
                },
                AccessControlList = AccessControlList.Anonymous
            };

            //
            // Upload the comment via transit
            //

            var response = await merryAppClient.TransitFileSender.TransferFile(commentFileMetadata, recipients, targetFile.uploadResult.File.TargetDrive,
                fileSystemType: FileSystemType.Comment);
            ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Status code was {response.StatusCode}");

            var c = _scaffold.CreateOwnerApiClientRedux(merryAppClient.Identity);
            await c.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);
            
            //
            // Get the comment on pippin's identity and test it
            //

            var remoteFile = new TransitExternalFileIdentifier()
            {
                OdinId = pippinOwnerClient.Identity.OdinId,
                File = targetFile.uploadResult.File
            };

            var getTransitFileHeaderResponse = await merryAppClient.TransitQuery.GetFileHeader(remoteFile, FileSystemType.Comment);
            ClassicAssert.IsTrue(getTransitFileHeaderResponse.IsSuccessStatusCode, $"Status code was {response.StatusCode}");
            ClassicAssert.IsTrue(getTransitFileHeaderResponse.Content.FileMetadata.AppData.Content == commentFileMetadata.AppData.Content);
        }


        [Test]
        public async Task AppCan_AddCommentOn_AnonymousDrive_With_CommentPermission_and_ConnectedIdentity()
        {
            // Prep
            var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
            var merryOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Merry);

            //Notice: no circles since we're only testing what can be done by connected identities on an anonymous drive
            await pippinOwnerClient.Network.SendConnectionRequestTo(TestIdentities.Merry, new List<GuidId>() { });
            await merryOwnerClient.Network.AcceptConnectionRequest(TestIdentities.Pippin, new List<GuidId>() { });

            var remoteDrive = await pippinOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some target drive", "", allowAnonymousReads: true);

            // Pippin uploads file
            var targetFile = await UploadStandardRandomPublicFileHeader(pippinOwnerClient.Identity, remoteDrive.TargetDriveInfo);

            //
            // Ensure the flag that allows authenticated identities to comment is true
            //
            await pippinOwnerClient.Configuration.UpdateTenantSettingsFlag(TenantConfigFlagNames.AuthenticatedIdentitiesCanCommentOnAnonymousDrives,
                true.ToString());

            var recipients = new List<string>() { pippinOwnerClient.Identity.OdinId };
            var commentFileMetadata = new UploadFileMetadata()
            {
                ReferencedFile = targetFile.uploadResult.GlobalTransitIdFileIdentifier,
                AllowDistribution = true,
                IsEncrypted = false,
                AppData = new()
                {
                    FileType = 777,
                    Content = "This is a Comment",
                    UniqueId = Guid.NewGuid(),
                },
                AccessControlList = AccessControlList.Anonymous
            };

            //
            // Upload the comment via transit
            //
            var remoteTargetDrive = targetFile.uploadResult.File.TargetDrive;
            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitWrite, PermissionKeys.UseTransitRead);
            var response = await merryAppClient.TransitFileSender.TransferFile(commentFileMetadata, recipients, remoteTargetDrive,
                fileSystemType: FileSystemType.Comment);
            ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Status code was {response.StatusCode}");
            var transitResult = response.Content;
            ClassicAssert.IsNotNull(transitResult);
            ClassicAssert.IsTrue(transitResult.RecipientStatus[pippinOwnerClient.Identity.OdinId] == TransferStatus.Enqueued);

            var c = _scaffold.CreateOwnerApiClientRedux(merryAppClient.Identity);
            await c.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);

            //
            // Merry uses transit query to get all files of that file type
            //
            var request = new PeerQueryBatchRequest()
            {
                OdinId = pippinOwnerClient.Identity.OdinId,
                QueryParams = new()
                {
                    TargetDrive = remoteTargetDrive,
                    ClientUniqueIdAtLeastOne = new[] { commentFileMetadata.AppData.UniqueId.GetValueOrDefault() }
                },
                ResultOptionsRequest = new()
                {
                    IncludeMetadataHeader = true,
                    MaxRecords = 10,
                    Ordering = QueryBatchSortOrder.NewestFirst,
                    Sorting = QueryBatchSortField.CreatedDate
                }
            };

            var getTransitBatchResponse = await merryAppClient.TransitQuery.GetBatch(request, FileSystemType.Comment);
            ClassicAssert.IsTrue(getTransitBatchResponse.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(getTransitBatchResponse.Content);

            var theRemoteComment = getTransitBatchResponse.Content.SearchResults.SingleOrDefault();
            ClassicAssert.IsNotNull(theRemoteComment);
            ClassicAssert.IsTrue(theRemoteComment.FileMetadata.AppData.Content == commentFileMetadata.AppData.Content);

            await pippinOwnerClient.Network.DisconnectFrom(merryOwnerClient.Identity);
        }


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
            TargetDrive targetDrive, string payload = null, ThumbnailContent thumbnail = null)
        {
            var client = _scaffold.CreateOwnerApiClient(identity);
            var fileMetadata = new UploadFileMetadata()
            {
                IsEncrypted = false,
                AppData = new()
                {
                    FileType = 777,
                    Content = $"some json content {Guid.NewGuid()}",
                    UniqueId = Guid.NewGuid()
                },
                AccessControlList = AccessControlList.Anonymous
            };

            var result = await client.Drive.UploadFile(FileSystemType.Standard, targetDrive, fileMetadata,
                payloadData: payload ?? "",
                thumbnail: thumbnail);
            return (result, fileMetadata);
        }
    }
}