using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Membership.Circles;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Services.Peer.ReceivingHost;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests.AppAPI.ApiClient;
using Odin.Hosting.Tests.AppAPI.Drive;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.Transit.Query
{
    public class AppTransitQueryTestsForPublicFiles
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
        public async Task AppCan_Query_Public_Batch_OverTransitQuery()
        {
            // Prep
            var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
            var remoteDrive = await pippinOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some target drive", "", allowAnonymousReads: true);

            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitRead);

            // Pippin uploads file
            var randomFile = await UploadStandardRandomPublicFileHeader(pippinOwnerClient.Identity, remoteDrive.TargetDriveInfo);

            //
            // Merry uses transit query to get all files of that file type
            //
            var request = new TransitQueryBatchRequest()
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
            Assert.IsTrue(getBatchResponse.IsSuccessStatusCode);
            Assert.IsNotNull(getBatchResponse.Content);
            Assert.IsNotNull(getBatchResponse.Content.SearchResults.SingleOrDefault(sr => sr.FileId == randomFile.uploadResult.File.FileId));
        }

        [Test]
        public async Task AppCan_Query_Public_BatchCollection_OverTransitQuery()
        {
            // Prep
            var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
            var remoteDrive = await pippinOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some target drive", "", allowAnonymousReads: true);

            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitRead);

            // Pippin uploads file
            var randomFile1 = await UploadStandardRandomPublicFileHeader(pippinOwnerClient.Identity, remoteDrive.TargetDriveInfo);
            var randomFile2 = await UploadStandardRandomPublicFileHeader(pippinOwnerClient.Identity, remoteDrive.TargetDriveInfo);

            const string testResult1 = "test02";
            const string testResult2 = "test02";
            var request = new TransitQueryBatchCollectionRequest()
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
                        ResultOptionsRequest = default
                    },
                    new()
                    {
                        Name = testResult2,
                        QueryParams = new FileQueryParams()
                        {
                            TargetDrive = randomFile2.uploadResult.File.TargetDrive,
                            ClientUniqueIdAtLeastOne = new[] { randomFile2.uploadedMetadata.AppData.UniqueId.GetValueOrDefault() }
                        },
                        ResultOptionsRequest = default
                    }
                }
            };

            var collectionResponse = await merryAppClient.TransitQuery.GetBatchCollection(request);

            Assert.IsTrue(collectionResponse.IsSuccessStatusCode);
            Assert.IsNotNull(collectionResponse.Content);
            Assert.IsTrue(collectionResponse.Content.Results.Count == 2);

            var set1 = collectionResponse.Content.Results.SingleOrDefault(r => r.Name.ToLower() == testResult1.ToLower());
            Assert.IsNotNull(set1);

            var set1File1 = set1.SearchResults.SingleOrDefault(f => f.FileId == randomFile1.uploadResult.File.FileId);
            Assert.IsNotNull(set1File1);
            
            var set2 = collectionResponse.Content.Results.SingleOrDefault(r => r.Name.ToLower() == testResult2.ToLower());
            Assert.IsNotNull(set2);

            var set2File1 = set2.SearchResults.SingleOrDefault(f => f.FileId == randomFile2.uploadResult.File.FileId);
            Assert.IsNotNull(set2File1);
        }

        [Test]
        public async Task AppCan_Query_Public_Modified_OverTransitQuery()
        {
            // Prep
            var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
            var remoteDrive = await pippinOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some target drive", "", allowAnonymousReads: true);

            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitRead);

            // Pippin uploads file
            var randomFile = await UploadStandardRandomPublicFileHeader(pippinOwnerClient.Identity, remoteDrive.TargetDriveInfo);

            // Pippin now modifies that file
            var modifiedResult = await ModifyFile(pippinOwnerClient.Identity, randomFile.uploadResult.File);
            Assert.IsTrue(modifiedResult.uploadResult.File == randomFile.uploadResult.File);
            Assert.IsFalse(modifiedResult.modifiedMetadata.AppData.JsonContent == randomFile.uploadedMetadata.AppData.JsonContent, "file was not modified");
            // await pippinOwnerClient.Drive.DeleteFile(randomFile.uploadResult.File);

            //
            // Merry uses transit query to get modified files (deleted files show up as modified)
            //
            var request = new TransitQueryModifiedRequest()
            {
                OdinId = pippinOwnerClient.Identity.OdinId,
                QueryParams = new()
                {
                    TargetDrive = randomFile.uploadResult.File.TargetDrive,
                    FileType = new[] { randomFile.uploadedMetadata.AppData.FileType }
                },
                ResultOptions = new QueryModifiedResultOptions()
                {
                    MaxRecords = 100
                }
            };

            var getBatchResponse = await merryAppClient.TransitQuery.GetModified(request);
            Assert.IsTrue(getBatchResponse.IsSuccessStatusCode);
            Assert.IsNotNull(getBatchResponse.Content);
            var theDeletedFile = getBatchResponse.Content.SearchResults.SingleOrDefault(sr => sr.FileId == randomFile.uploadResult.File.FileId);
            Assert.IsNotNull(theDeletedFile);
            Assert.IsTrue(theDeletedFile.FileState == FileState.Deleted);
        }

        [Test]
        public async Task AppCan_Get_Public_Header_OverTransitQuery()
        {
            Assert.Fail("TODO");
        }

        [Test]
        public async Task AppCan_Get_Public_Payload_OverTransitQuery()
        {
            Assert.Fail("TODO");
        }

        [Test]
        public async Task AppCan_Get_Public_Thumbnail_OverTransitQuery()
        {
            Assert.Fail("TODO");
        }

        [Test]
        public async Task AppCan_Get_Public_Metadata_Type_OverTransitQuery()
        {
            Assert.Fail("TODO");
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
            TargetDrive targetDrive)
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
                    ContentIsComplete = true,
                    UniqueId = Guid.NewGuid()
                },
                AccessControlList = AccessControlList.Anonymous
            };

            var result = await client.Drive.UploadFile(FileSystemType.Standard, targetDrive, fileMetadata);
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