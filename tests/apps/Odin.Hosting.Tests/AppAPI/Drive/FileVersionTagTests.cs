using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Storage;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.Drive
{
    public class FileVersionTagTests
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
        public async Task NewVersionTagSetWhenFileUploaded()
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
                AllowDistribution = false,
                IsEncrypted = false,
                AppData = new()
                {
                    Content = "some content",
                    FileType = 101,
                    GroupId = default,
                    // UniqueId = message.Id,
                },
                VersionTag = default, //new file
                AccessControlList = AccessControlList.OwnerOnly
            };

            //upload a new file
            var uploadResult = await appApiClient.Drive.UploadFile(appDrive.TargetDriveInfo, fileMetadata, payload);

            //get the uploaded file
            var uploadedFile = await appApiClient.Drive.GetFileHeader(uploadResult.File);

            Assert.IsFalse(uploadedFile.FileMetadata.VersionTag == Guid.Empty, "Server should have set a VersionTag on a new file");
        }

        [Test]
        public async Task UploadStaleVersionTagFails_AndReturns_BadRequest_VersionTagMismatch()
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
                AllowDistribution = false,
                IsEncrypted = false,
                AppData = new()
                {
                    Content = "some content",
                    FileType = 101,
                    GroupId = default,
                    // UniqueId = message.Id,
                },
                VersionTag = default, //new file
                AccessControlList = AccessControlList.OwnerOnly
            };

            //upload a new file
            var uploadResult = await appApiClient.Drive.UploadFile(appDrive.TargetDriveInfo, fileMetadata, payload);

            //get the uploaded file
            var uploadedFile = await appApiClient.Drive.GetFileHeader(uploadResult.File);

            Assert.IsFalse(uploadedFile.FileMetadata.VersionTag == Guid.Empty, "Server should have set a VersionTag on a new file");

            //just send a random token
            fileMetadata.VersionTag = Guid.Parse("7215bd54-c832-4f08-84fc-ebfb6193ee52");

            var (_, apiResponse) = await appApiClient.Drive.UploadRaw(FileSystemType.Standard, appDrive.TargetDriveInfo, fileMetadata, payload,
                overwriteFileId: uploadResult.File.FileId);

            Assert.IsTrue(apiResponse.StatusCode == HttpStatusCode.BadRequest);

            var code = TestUtils.ParseProblemDetails(apiResponse!.Error!);
            Assert.IsTrue(code == OdinClientErrorCode.VersionTagMismatch);
        }
    }
}