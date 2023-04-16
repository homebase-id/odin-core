using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Storage;

namespace Youverse.Hosting.Tests.AppAPI.Drive
{
    public class FileConcurrencyTests
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
        public async Task NewConcurrencyTokenSetWhenFileUploaded()
        {
            var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

            var appDrive = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Chat Drive 1", "", false);
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
            const string payload = "";

            var fileMetadata = new UploadFileMetadata()
            {
                ContentType = "application/json",
                AllowDistribution = false,
                PayloadIsEncrypted = false,
                AppData = new()
                {
                    ContentIsComplete = true,
                    JsonContent = "some content",
                    FileType = 101,
                    GroupId = default,
                    // UniqueId = message.Id,
                },
                ConcurrencyToken = default, //new file
                AccessControlList = AccessControlList.OwnerOnly
            };

            //upload a new file
            var uploadResult = await appApiClient.Drive.UploadFile(FileSystemType.Standard, appDrive.TargetDriveInfo, fileMetadata, payload);

            //get the uploaded file
            var uploadedFile = await appApiClient.Drive.GetFileHeader(FileSystemType.Standard, uploadResult.File);

            Assert.IsFalse(uploadedFile.FileMetadata.ConcurrencyToken == Guid.Empty, "Server should have set a concurrency token on a new file");
        }

        [Test]
        public async Task UploadStaleConcurrencyTokenFails_AndReturns_BadRequest_ConcurrencyTokenMismatch()
        {
            var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

            var appDrive = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Chat Drive 1", "", false);
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
            const string payload = "";

            var fileMetadata = new UploadFileMetadata()
            {
                ContentType = "application/json",
                AllowDistribution = false,
                PayloadIsEncrypted = false,
                AppData = new()
                {
                    ContentIsComplete = true,
                    JsonContent = "some content",
                    FileType = 101,
                    GroupId = default,
                    // UniqueId = message.Id,
                },
                ConcurrencyToken = default, //new file
                AccessControlList = AccessControlList.OwnerOnly
            };

            //upload a new file
            var uploadResult = await appApiClient.Drive.UploadFile(FileSystemType.Standard, appDrive.TargetDriveInfo, fileMetadata, payload);

            //get the uploaded file
            var uploadedFile = await appApiClient.Drive.GetFileHeader(FileSystemType.Standard, uploadResult.File);

            Assert.IsFalse(uploadedFile.FileMetadata.ConcurrencyToken == Guid.Empty, "Server should have set a concurrency token on a new file");

            //just send a random token
            fileMetadata.ConcurrencyToken = Guid.Parse("7215bd54-c832-4f08-84fc-ebfb6193ee52");

            var (_, apiResponse) = await appApiClient.Drive.UploadRaw(FileSystemType.Standard, appDrive.TargetDriveInfo, fileMetadata, payload,
                overwriteFileId: uploadResult.File.FileId);
            
            Assert.IsTrue(apiResponse.StatusCode == HttpStatusCode.BadRequest);

            var details = DotYouSystemSerializer.Deserialize<ProblemDetails>(apiResponse!.Error!.Content!);
            Assert.IsTrue((YouverseClientErrorCode)int.Parse(details.Extensions["errorCode"].ToString()!) ==
                          YouverseClientErrorCode.ConcurrencyTokenMismatch);
        }
    }
}