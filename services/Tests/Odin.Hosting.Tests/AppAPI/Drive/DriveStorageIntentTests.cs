using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Hosting.Tests.AppAPI.ApiClient;

namespace Odin.Hosting.Tests.AppAPI.Drive
{
    public class DriveStorageIntentTests
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
        public async Task CanUpdateMetadataOnly_StorageIntentMedata()
        {
            var (appApiClient, targetDrive) = await CreateApp(TestIdentities.Samwise);

            var content1 = OdinSystemSerializer.Serialize(new { data = "nom nom nom" });
            var content2 = OdinSystemSerializer.Serialize(new { data = "chomp chomp chomp" });

            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = false,
                AppData = new()
                {
                    FileType = 101,
                    Content = content1
                },
                PayloadIsEncrypted = false,
                AccessControlList = AccessControlList.Connected
            };

            //upload normal
            var uploadResult = await appApiClient.Drive.UploadFile(targetDrive, fileMetadata, "");
            var firstHeader = await appApiClient.Drive.GetFileHeader(uploadResult.File);
            Assert.IsTrue(firstHeader.FileMetadata.AppData.Content == content1);
            //validate normal

            //update the content
            fileMetadata.AppData.Content = content2;
            fileMetadata.VersionTag = firstHeader.FileMetadata.VersionTag;

            var updateResult = await appApiClient.Drive.UpdateMetadata(targetDrive, fileMetadata, overwriteFileId: uploadResult.File.FileId);

            Assert.IsTrue(updateResult.NewVersionTag != uploadResult.NewVersionTag);
            var updatedHeader = await appApiClient.Drive.GetFileHeader(uploadResult.File);

            Assert.IsTrue(updatedHeader.FileMetadata.AppData.Content == content2);
            Assert.IsTrue(updatedHeader.FileMetadata.VersionTag != firstHeader.FileMetadata.VersionTag);
        }

        [Test]
        public async Task CanUpdateMetadata_EvenWhenPayloadChanged_StorageIntentMedata()
        {
            var (appApiClient, targetDrive) = await CreateApp(TestIdentities.Samwise);

            var content1 = OdinSystemSerializer.Serialize(new { data = "nom nom nom" });
            var content2 = OdinSystemSerializer.Serialize(new { data = "chomp chomp chomp" });

            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = false,
                AppData = new()
                {
                    FileType = 101,
                    Content = content1
                },
                PayloadIsEncrypted = false,
                AccessControlList = AccessControlList.Connected
            };

            //upload normal
            var uploadResult = await appApiClient.Drive.UploadFile(targetDrive, fileMetadata, "");
            var firstHeader = await appApiClient.Drive.GetFileHeader(uploadResult.File);
            Assert.IsTrue(firstHeader.FileMetadata.AppData.Content == content1);
            //validate normal

            //update the content; indicate the payload changed
            fileMetadata.AppData.Content = content2;
            fileMetadata.VersionTag = firstHeader.FileMetadata.VersionTag;

            var updateResultResponse = await appApiClient.Drive.UpdateMetadataRaw(targetDrive, fileMetadata, overwriteFileId: uploadResult.File.FileId);

            Assert.IsTrue(updateResultResponse.IsSuccessStatusCode);
            Assert.IsTrue(updateResultResponse.Content.NewVersionTag != uploadResult.NewVersionTag);

            var updatedHeader = await appApiClient.Drive.GetFileHeader(uploadResult.File);

            Assert.IsTrue(updatedHeader.FileMetadata.AppData.Content == content2);
            Assert.IsTrue(updatedHeader.FileMetadata.VersionTag != firstHeader.FileMetadata.VersionTag);
            Assert.IsTrue(updatedHeader.FileMetadata.Payloads.Count == 0);
        }
        
        // 

        private async Task<(AppApiClient appApiClient, TargetDrive drive)> CreateApp(TestIdentity identity)
        {
            var ownerClient = _scaffold.CreateOwnerApiClient(identity);
            var appDrive = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some app Drive 1", "", false);
            var appId = Guid.NewGuid();

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
                PermissionSet = new PermissionSet(PermissionKeys.All)
            };

            var appRegistration = await ownerClient.Apps.RegisterApp(appId, appPermissionsGrant);
            var appApiClient = _scaffold.CreateAppClient(TestIdentities.Samwise, appId);

            return (appApiClient, appDrive.TargetDriveInfo);
        }
    }
}