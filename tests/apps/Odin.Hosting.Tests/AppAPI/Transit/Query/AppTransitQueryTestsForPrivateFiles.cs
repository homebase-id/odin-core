using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Storage;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests.AppAPI.ApiClient;

namespace Odin.Hosting.Tests.AppAPI.Transit.Query
{
    public class AppTransitQueryTestsForPrivateFiles
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var folder = GetType().Name;
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
        public async Task AppCan_Query_Secured_Batch_OverTransitQuery()
        {
            var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
            var remoteDrive = TargetDrive.NewTargetDrive();
            // var remoteDrive = await pippinOwnerClient.Drive.CreateDrive(remoteDrive, "Some target drive", "", allowAnonymousReads: false);

            //Connected merry and pippin; also grant RW to the remote drive
            await _scaffold.Scenarios.CreateConnectedHobbits(remoteDrive);

            var thumbnail = new ThumbnailContent()
            {
                PixelHeight = 300,
                PixelWidth = 300,
                ContentType = "image/jpeg",
                Content = TestMedia.ThumbnailBytes300
            };

            // Pippin uploads file
            var randomFile =
                await UploadStandardRandomSecureConnectedFile(pippinOwnerClient.Identity, remoteDrive, payload: "far and wide", thumbnail: thumbnail);

            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitRead);

            //
            // Merry uses transit query to get all files of that file type
            //
            var request = new PeerQueryBatchRequest()
            {
                OdinId = pippinOwnerClient.Identity.OdinId,
                QueryParams = new()
                {
                    TargetDrive = randomFile.uploadResult.File.TargetDrive,
                    ClientUniqueIdAtLeastOne = new[] { randomFile.uploadedMetadata.AppData.UniqueId.GetValueOrDefault() }
                },
                ResultOptionsRequest = new()
                {
                    IncludeMetadataHeader = true,
                    MaxRecords = 10,
                    Ordering = Ordering.NewestFirst,
                    Sorting = Sorting.FileId
                }
            };

            var getBatchResponse = await merryAppClient.TransitQuery.GetBatch(request);
            ClassicAssert.IsTrue(getBatchResponse.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(getBatchResponse.Content);
            ClassicAssert.IsNotNull(getBatchResponse.Content.SearchResults.SingleOrDefault(sr => sr.FileId == randomFile.uploadResult.File.FileId));

            await _scaffold.Scenarios.DisconnectHobbits();
        }

        [Test]
        public async Task AppCan_Query_Secured_BatchCollection_OverTransitQuery()
        {
            var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
            var remoteDrive = TargetDrive.NewTargetDrive();

            //Connected merry and pippin; also grant RW to the remote drive
            await _scaffold.Scenarios.CreateConnectedHobbits(remoteDrive);

            var thumbnail = new ThumbnailContent()
            {
                PixelHeight = 300,
                PixelWidth = 300,
                ContentType = "image/jpeg",
                Content = TestMedia.ThumbnailBytes300
            };

            const string payloadData = "far and wide";

            // Pippin uploads file
            var randomFile1 =
                await UploadStandardRandomSecureConnectedFile(pippinOwnerClient.Identity, remoteDrive, payload: payloadData, thumbnail: thumbnail);
            var randomFile2 =
                await UploadStandardRandomSecureConnectedFile(pippinOwnerClient.Identity, remoteDrive, payload: payloadData, thumbnail: thumbnail);

            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitRead);


            const string testResult1 = "test01";
            const string testResult2 = "test02";
            var request = new PeerQueryBatchCollectionRequest()
            {
                OdinId = pippinOwnerClient.Identity.OdinId,
                Queries = new List<CollectionQueryParamSection>()
                {
                    new()
                    {
                        Name = testResult1,
                        QueryParams = new FileQueryParams()
                        {
                            TargetDrive = randomFile1.uploadResult.File.TargetDrive,
                            ClientUniqueIdAtLeastOne = new[] { randomFile1.uploadedMetadata.AppData.UniqueId.GetValueOrDefault() }
                        },
                        ResultOptionsRequest = new QueryBatchResultOptionsRequest()
                        {
                            MaxRecords = 100,
                            IncludeMetadataHeader = true
                        }
                    },
                    new()
                    {
                        Name = testResult2,
                        QueryParams = new FileQueryParams()
                        {
                            TargetDrive = randomFile2.uploadResult.File.TargetDrive,
                            ClientUniqueIdAtLeastOne = new[] { randomFile2.uploadedMetadata.AppData.UniqueId.GetValueOrDefault() }
                        },
                        ResultOptionsRequest = new QueryBatchResultOptionsRequest()
                        {
                            MaxRecords = 100,
                            IncludeMetadataHeader = true
                        }
                    }
                }
            };

            var collectionResponse = await merryAppClient.TransitQuery.GetBatchCollection(request);

            ClassicAssert.IsTrue(collectionResponse.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(collectionResponse.Content);
            ClassicAssert.IsTrue(collectionResponse.Content.Results.Count == 2);

            var set1 = collectionResponse.Content.Results.SingleOrDefault(r => r.Name.ToLower() == testResult1.ToLower());
            ClassicAssert.IsNotNull(set1);

            var set1File1 = set1.SearchResults.SingleOrDefault(f => f.FileId == randomFile1.uploadResult.File.FileId);
            ClassicAssert.IsNotNull(set1File1);

            var set2 = collectionResponse.Content.Results.SingleOrDefault(r => r.Name.ToLower() == testResult2.ToLower());
            ClassicAssert.IsNotNull(set2);

            var set2File1 = set2.SearchResults.SingleOrDefault(f => f.FileId == randomFile2.uploadResult.File.FileId);
            ClassicAssert.IsNotNull(set2File1);

            await _scaffold.Scenarios.DisconnectHobbits();
        }

        [Test]
        public async Task AppCan_Query_Secured_Modified_OverTransitQuery()
        {
            var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
            var remoteDrive = TargetDrive.NewTargetDrive();

            //Connected merry and pippin; also grant RW to the remote drive
            await _scaffold.Scenarios.CreateConnectedHobbits(remoteDrive);

            var thumbnail = new ThumbnailContent()
            {
                PixelHeight = 300,
                PixelWidth = 300,
                ContentType = "image/jpeg",
                Content = TestMedia.ThumbnailBytes300
            };

            const string payloadData = "yea, another payload";
            // Pippin uploads file
            var randomFile = await UploadStandardRandomSecureConnectedFile(pippinOwnerClient.Identity, remoteDrive,payload:payloadData, thumbnail: thumbnail);

            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitRead);

            // Pippin now modifies that file
            var modifiedResult = await ModifyFile(pippinOwnerClient.Identity, randomFile.uploadResult.File);
            ClassicAssert.IsTrue(randomFile.uploadResult.File == modifiedResult.uploadResult.File);
            ClassicAssert.IsFalse(randomFile.uploadedMetadata.AppData.Content == modifiedResult.modifiedMetadata.AppData.Content, "file was not modified");

            //
            // Merry uses transit query to get modified files (deleted files show up as modified)
            //
            var request = new PeerQueryModifiedRequest()
            {
                OdinId = pippinOwnerClient.Identity.OdinId,
                QueryParams = new()
                {
                    TargetDrive = randomFile.uploadResult.File.TargetDrive,
                    FileType = new[] { randomFile.uploadedMetadata.AppData.FileType }
                },
                ResultOptions = new QueryModifiedResultOptions()
                {
                    IncludeHeaderContent = true,
                    MaxRecords = 100
                }
            };

            var getBatchResponse = await merryAppClient.TransitQuery.GetModified(request);
            ClassicAssert.IsTrue(getBatchResponse.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(getBatchResponse.Content);
            var theModifiedFile = getBatchResponse.Content.SearchResults.SingleOrDefault(sr => sr.FileId == randomFile.uploadResult.File.FileId);
            ClassicAssert.IsNotNull(theModifiedFile);
            ClassicAssert.IsTrue(theModifiedFile.FileMetadata.AppData.Content == modifiedResult.modifiedMetadata.AppData.Content);

            await _scaffold.Scenarios.DisconnectHobbits();
        }

        [Test]
        public async Task AppCan_Get_Secured_Header_OverTransitQuery()
        {
            // Prep
            var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
            var remoteDrive = TargetDrive.NewTargetDrive();

            //Connected merry and pippin; also grant RW to the remote drive
            await _scaffold.Scenarios.CreateConnectedHobbits(remoteDrive);

            // Pippin uploads file
            var randomFile = await UploadStandardRandomSecureConnectedFile(pippinOwnerClient.Identity, remoteDrive);

            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitRead);

            var response = await merryAppClient.TransitQuery.GetFileHeader(new TransitExternalFileIdentifier()
            {
                OdinId = pippinOwnerClient.Identity.OdinId,
                File = randomFile.uploadResult.File
            });

            ClassicAssert.IsTrue(response.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(response.Content);
            ClassicAssert.IsTrue(response.Content.FileMetadata.AppData.Content == randomFile.uploadedMetadata.AppData.Content);

            await _scaffold.Scenarios.DisconnectHobbits();
        }

        [Test]
        public async Task AppCan_Get_Secured_Payload_OverTransitQuery()
        {
            // Prep
            var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
            var remoteDrive = TargetDrive.NewTargetDrive();
            // var remoteDrive = await pippinOwnerClient.Drive.CreateDrive(remoteDrive, "Some target drive", "", allowAnonymousReads: false);

            //Connected merry and pippin; also grant RW to the remote drive
            await _scaffold.Scenarios.CreateConnectedHobbits(remoteDrive);

            const string uploadedPayload = "some payload of something secured";

            // Pippin uploads file
            var randomFile = await UploadStandardRandomSecureConnectedFile(pippinOwnerClient.Identity, remoteDrive, payload: uploadedPayload);

            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitRead);

            var response = await merryAppClient.TransitQuery.GetPayload(new TransitGetPayloadRequest()
            {
                OdinId = pippinOwnerClient.Identity.OdinId,
                File = randomFile.uploadResult.File,
                Key = WebScaffold.PAYLOAD_KEY
            });

            ClassicAssert.IsTrue(response.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(response.Content);
            var payload = await response.Content.ReadAsStringAsync();
            ClassicAssert.IsTrue(payload == uploadedPayload);

            await _scaffold.Scenarios.DisconnectHobbits();
        }

        [Test]
        public async Task AppCan_Get_Secured_Thumbnail_OverTransitQuery()
        {
            // Prep
            var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
            var remoteDrive = TargetDrive.NewTargetDrive();

            //Connected merry and pippin; also grant RW to the remote drive
            await _scaffold.Scenarios.CreateConnectedHobbits(remoteDrive);

            const string payloadData = "far and wide";
            var thumbnail = new ThumbnailContent()
            {
                PixelHeight = 300,
                PixelWidth = 300,
                ContentType = "image/jpeg",
                Content = TestMedia.ThumbnailBytes300
            };

            // Pippin uploads file
            var randomFile = await UploadStandardRandomSecureConnectedFile(pippinOwnerClient.Identity, remoteDrive, payload: payloadData, thumbnail: thumbnail);

            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitRead);

            var response = await merryAppClient.TransitQuery.GetThumbnail(new TransitGetThumbRequest()
            {
                OdinId = pippinOwnerClient.Identity.OdinId,
                File = randomFile.uploadResult.File,
                Width = thumbnail.PixelWidth,
                Height = thumbnail.PixelHeight,
                PayloadKey = WebScaffold.PAYLOAD_KEY,
                DirectMatchOnly = true
            });

            ClassicAssert.IsTrue(response.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(response.Content);
            var thumbnailContent = await response.Content.ReadAsStringAsync();
            ClassicAssert.True(thumbnail.Content.Length == thumbnailContent.Length);

            await _scaffold.Scenarios.DisconnectHobbits();
        }

        [Test]
        public async Task AppCan_Get_Secured_Metadata_Type_OverTransitQuery()
        {
            // Connected merry and pippin; also grant RW to the remote drive
            var driveType = Guid.Parse("11111111-2222-3333-a88f-e2475560bced");

            var remoteDrive1GrantedViaCircle = new TargetDrive()
            {
                Alias = Guid.NewGuid(),
                Type = driveType
            };

            await _scaffold.Scenarios.CreateConnectedHobbits(remoteDrive1GrantedViaCircle);

            var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

            var remoteDrive2AnonymousDrive = await pippinOwnerClient.Drive.CreateDrive(new TargetDrive() { Alias = Guid.NewGuid(), Type = driveType },
                "Some target drive allow anonymous=true", "", allowAnonymousReads: true);
            var remoteDrive3NeverGrantedToMerry = await pippinOwnerClient.Drive.CreateDrive(new TargetDrive() { Alias = Guid.NewGuid(), Type = driveType },
                "Some target drive 2", "", allowAnonymousReads: false);

            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitRead);

            var getTransitDrives = await merryAppClient.TransitQuery.GetDrives(new TransitGetDrivesByTypeRequest()
            {
                OdinId = pippinOwnerClient.Identity.OdinId,
                DriveType = driveType,
                PageSize = 10,
                PageNumber = 1
            });

            ClassicAssert.IsTrue(getTransitDrives.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(getTransitDrives.Content);

            var drivesOnRecipientIdentityAccessibleToSender = getTransitDrives.Content.Results;

            ClassicAssert.IsTrue(drivesOnRecipientIdentityAccessibleToSender.All(d => d.TargetDrive.Type == driveType));
            ClassicAssert.IsNotNull(drivesOnRecipientIdentityAccessibleToSender.SingleOrDefault(d => d.TargetDrive == remoteDrive1GrantedViaCircle));
            ClassicAssert.IsNotNull(drivesOnRecipientIdentityAccessibleToSender.SingleOrDefault(d => d.TargetDrive == remoteDrive2AnonymousDrive.TargetDriveInfo));

            ClassicAssert.IsNull(drivesOnRecipientIdentityAccessibleToSender.SingleOrDefault(d => d.TargetDrive == remoteDrive3NeverGrantedToMerry.TargetDriveInfo));

            await _scaffold.Scenarios.DisconnectHobbits();
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

        private async Task<(UploadResult uploadResult, UploadFileMetadata uploadedMetadata)> UploadStandardRandomSecureConnectedFile(TestIdentity identity,
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
                    UniqueId = Guid.NewGuid(),
                },
                AccessControlList = AccessControlList.Connected
            };

            var result = await client.Drive.UploadFile(FileSystemType.Standard, targetDrive, fileMetadata,
                payloadData: payload ?? "",
                thumbnail: thumbnail,
                payloadKey: payload == null ? "" : WebScaffold.PAYLOAD_KEY);
            return (result, fileMetadata);
        }

        private async Task<(UploadResult uploadResult, ClientFileMetadata modifiedMetadata)> ModifyFile(TestIdentity identity, ExternalFileIdentifier file)
        {
            var client = _scaffold.CreateOwnerApiClient(identity);

            var header = await client.Drive.GetFileHeader(FileSystemType.Standard, file);

            var fileMetadata = new UploadFileMetadata()
            {
                IsEncrypted = false,
                AppData = new()
                {
                    FileType = 777,
                    Content = header.FileMetadata.AppData.Content + " something i appended"
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