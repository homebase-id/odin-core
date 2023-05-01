using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Storage;
using Youverse.Hosting.Tests.AppAPI.ApiClient;

namespace Youverse.Hosting.Tests.AppAPI.Drive
{
    public class DriveStorageIntentTests
    {
        private WebScaffold _scaffold;

        // var problemDetails = DotYouSystemSerializer.Deserialize<ProblemDetails>(response!.Error!.Content!);
        // Assert.IsNotNull(problemDetails);
        // Assert.IsTrue(int.Parse(problemDetails.Extensions["errorCode"].ToString() ?? string.Empty) == (int)YouverseClientErrorCode.CannotOverwriteNonExistentFile);

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

            var content1 = DotYouSystemSerializer.Serialize(new { data = "nom nom nom" });
            var content2 = DotYouSystemSerializer.Serialize(new { data = "chomp chomp chomp" });

            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = false,
                AppData = new()
                {
                    FileType = 101,
                    JsonContent = content1,
                    ContentIsComplete = true
                },
                PayloadIsEncrypted = false,
                AccessControlList = AccessControlList.Connected
            };

            //upload normal
            var uploadResult = await appApiClient.Drive.UploadFile(targetDrive, fileMetadata, "");
            var firstHeader = await appApiClient.Drive.GetFileHeader(uploadResult.File);
            Assert.IsTrue(firstHeader.FileMetadata.AppData.JsonContent == content1);
            //validate normal

            //update the content
            fileMetadata.AppData.JsonContent = content2;
            fileMetadata.VersionTag = firstHeader.FileMetadata.VersionTag;
            
            var updateResult = await appApiClient.Drive.UpdateMetadata(targetDrive, fileMetadata, overwriteFileId: uploadResult.File.FileId);
            
            Assert.IsTrue(updateResult.NewVersionTag != uploadResult.NewVersionTag);
            var updatedHeader = await appApiClient.Drive.GetFileHeader(uploadResult.File);

            Assert.IsTrue(updatedHeader.FileMetadata.AppData.JsonContent == content2);
            Assert.IsTrue(updatedHeader.FileMetadata.VersionTag != firstHeader.FileMetadata.VersionTag);
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