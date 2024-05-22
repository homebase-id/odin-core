using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Hosting.Tests.AppAPI.ApiClient;

namespace Odin.Hosting.Tests.AppAPI.Drive
{
    public class DriveQueryBatchCollectionTests
    {
        private WebScaffold _scaffold;

        [SetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests();
        }

        [TearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [Test]
        public async Task CanQueryBatchCollection()
        {
            var identity = TestIdentities.Pippin;

            var appId = Guid.NewGuid();

            var ownerClient = _scaffold.CreateOwnerApiClient(identity);

            //
            // Create 3 drives and grant ReadWrite
            //
            var appDrive1 = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some Drive 1", "", false);
            var appDrive2 = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some Drive 2", "", false);
            var appDrive3 = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some Drive 3", "", false);

            var appPermissionsGrant = new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = appDrive1.TargetDriveInfo,
                            Permission = DrivePermission.ReadWrite
                        }
                    },
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = appDrive2.TargetDriveInfo,
                            Permission = DrivePermission.ReadWrite
                        }
                    },
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = appDrive3.TargetDriveInfo,
                            Permission = DrivePermission.ReadWrite
                        }
                    }
                },
                PermissionSet = new PermissionSet()
            };

            await ownerClient.Apps.RegisterApp(appId, appPermissionsGrant);
            var client = _scaffold.CreateAppClient(identity, appId);

            //
            // Upload 3 files
            //
            var header1 = await UploadStandardRandomFileHeadersUsingOwnerApi(identity, appDrive1.TargetDriveInfo);
            var header2 = await UploadStandardRandomFileHeadersUsingOwnerApi(identity, appDrive2.TargetDriveInfo);
            var header3 = await UploadStandardRandomFileHeadersUsingOwnerApi(identity, appDrive3.TargetDriveInfo);


            const string section1Name = "s1";
            const string section2Name = "s2";
            const string section3Name = "s3";

            //
            // QueryBatchCollection
            //
            var sections = new List<CollectionQueryParamSection>()
            {
                new()
                {
                    Name = section1Name,
                    QueryParams = new FileQueryParams()
                    {
                        TargetDrive = appDrive1.TargetDriveInfo,
                        ClientUniqueIdAtLeastOne = new List<Guid>() { header1.uploadedMetadata.AppData.UniqueId.GetValueOrDefault() }
                    }
                },
                new()
                {
                    Name = section2Name,
                    QueryParams = new FileQueryParams()
                    {
                        TargetDrive = appDrive2.TargetDriveInfo,
                        ClientUniqueIdAtLeastOne = new List<Guid>() { header2.uploadedMetadata.AppData.UniqueId.GetValueOrDefault() }
                    }
                },
                new()
                {
                    Name = section3Name,
                    QueryParams = new FileQueryParams()
                    {
                        TargetDrive = appDrive3.TargetDriveInfo,
                        ClientUniqueIdAtLeastOne = new List<Guid>() { header3.uploadedMetadata.AppData.UniqueId.GetValueOrDefault() }
                    }
                }
            };

            var queryBatchResponse = await client.Drive.QueryBatchCollection(FileSystemType.Standard, sections);
            Assert.IsTrue(queryBatchResponse.IsSuccessStatusCode);
            var queryResult = queryBatchResponse.Content;
            Assert.IsNotNull(queryResult);

            Assert.IsTrue(queryResult.Results.Count == 3, "Should be 3 sections");

            Assert.IsNotNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == section1Name &&
                r.SearchResults.SingleOrDefault(r2 => r2.FileId == header1.uploadResult.File.FileId) != null));
            
            Assert.IsNotNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == section1Name &&
                r.InvalidDrive == false));
            
            Assert.IsNotNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == section2Name &&
                r.SearchResults.SingleOrDefault(r2 => r2.FileId == header2.uploadResult.File.FileId) != null));
            
            Assert.IsNotNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == section2Name &&
                r.InvalidDrive == false));
            
            Assert.IsNotNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == section3Name &&
                r.SearchResults.SingleOrDefault(r2 => r2.FileId == header3.uploadResult.File.FileId) != null));
            
            
            Assert.IsNotNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == section3Name &&
                r.InvalidDrive == false));
        }

        [Test]
        public async Task QueryBatchCollectionReturnsAvailableResults_EvenWhenNoAccessToDrive()
        {
            // pippin uploads files across 3 drives
            // his app has access to all 2 drives
            // he receives results for the 2 drives he has access to; the other one returns an error but does not fail.

            var identity = TestIdentities.Pippin;
            var appId = Guid.NewGuid();
            var ownerClient = _scaffold.CreateOwnerApiClient(identity);

            //
            // Create 3 drives and grant ReadWrite to 1 and 2 but no access is given to 3.  we will still query it below
            //
            var appDrive1 = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some Drive 1", "", false);
            var appDrive2 = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some Drive 2", "", false);
            var appDrive3 = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some Drive 3", "", false);

            var appPermissionsGrant = new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = appDrive1.TargetDriveInfo,
                            Permission = DrivePermission.ReadWrite
                        }
                    },
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = appDrive2.TargetDriveInfo,
                            Permission = DrivePermission.ReadWrite
                        }
                    }
                },
                PermissionSet = new PermissionSet()
            };

            await ownerClient.Apps.RegisterApp(appId, appPermissionsGrant);
            var client = _scaffold.CreateAppClient(identity, appId);

            //
            // Upload 3 files
            //
            var header1 = await UploadStandardRandomFileHeadersUsingOwnerApi(identity, appDrive1.TargetDriveInfo);
            var header2 = await UploadStandardRandomFileHeadersUsingOwnerApi(identity, appDrive2.TargetDriveInfo);
            var header3 = await UploadStandardRandomFileHeadersUsingOwnerApi(identity, appDrive3.TargetDriveInfo);

            const string section1Name = "s1";
            const string section2Name = "s2";
            const string section3Name = "s3";
            //
            // QueryBatchCollection
            //
            var sections = new List<CollectionQueryParamSection>()
            {
                new()
                {
                    Name = section1Name,
                    QueryParams = new FileQueryParams()
                    {
                        TargetDrive = appDrive1.TargetDriveInfo,
                        ClientUniqueIdAtLeastOne = new List<Guid>() { header1.uploadedMetadata.AppData.UniqueId.GetValueOrDefault() }
                    }
                },
                new()
                {
                    Name = section2Name,
                    QueryParams = new FileQueryParams()
                    {
                        TargetDrive = appDrive2.TargetDriveInfo,
                        ClientUniqueIdAtLeastOne = new List<Guid>() { header2.uploadedMetadata.AppData.UniqueId.GetValueOrDefault() }
                    }
                },
                new()
                {
                    Name = section3Name,
                    QueryParams = new FileQueryParams()
                    {
                        TargetDrive = appDrive3.TargetDriveInfo,
                        ClientUniqueIdAtLeastOne = new List<Guid>() { header3.uploadedMetadata.AppData.UniqueId.GetValueOrDefault() }
                    }
                }
            };

            var queryBatchResponse = await client.Drive.QueryBatchCollection(FileSystemType.Standard, sections);
            Assert.IsTrue(queryBatchResponse.IsSuccessStatusCode);
            var queryResult = queryBatchResponse.Content;
            Assert.IsNotNull(queryResult);

            Assert.IsTrue(queryResult.Results.Count == 3, "Should be 3 sections");

            Assert.IsNotNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == section1Name &&
                r.SearchResults.SingleOrDefault(r2 => r2.FileId == header1.uploadResult.File.FileId) != null));

            Assert.IsNotNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == section1Name &&
                r.InvalidDrive == false));

            Assert.IsNotNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == section2Name &&
                r.SearchResults.SingleOrDefault(r2 => r2.FileId == header2.uploadResult.File.FileId) != null));

            Assert.IsNotNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == section2Name &&
                r.InvalidDrive == false));

            // query 3 should not return results
            Assert.IsNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == section3Name &&
                r.SearchResults.SingleOrDefault(r2 => r2.FileId == header3.uploadResult.File.FileId) != null));

            // query 3 should have the invalid drive flag == true
            Assert.IsNotNull(queryResult.Results.SingleOrDefault(r =>
                r.Name == section3Name &&
                r.InvalidDrive == true));
        }

        private async Task<(UploadResult uploadResult, UploadFileMetadata uploadedMetadata)> UploadStandardRandomFileHeadersUsingOwnerApi(TestIdentity identity,
            TargetDrive targetDrive, AccessControlList acl = null)
        {
            var client = _scaffold.CreateOwnerApiClient(identity);
            var fileMetadata = new UploadFileMetadata()
            {
                IsEncrypted = false,
                AllowDistribution = false,
                AppData = new()
                {
                    FileType = 777,
                    Content = $"Some json content {Guid.NewGuid()}",
                    UniqueId = Guid.NewGuid(),
                },
                AccessControlList = acl ?? AccessControlList.OwnerOnly
            };

            var result = await client.Drive.UploadFile(FileSystemType.Standard, targetDrive, fileMetadata);
            return (result, fileMetadata);
        }
    }
}