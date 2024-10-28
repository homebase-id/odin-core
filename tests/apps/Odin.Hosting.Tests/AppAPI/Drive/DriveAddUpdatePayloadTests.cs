using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Storage;
using Odin.Hosting.Tests.AppAPI.ApiClient;

namespace Odin.Hosting.Tests.AppAPI.Drive
{
    public class DriveAddUpdatePayloadTests
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

        private async Task<(UploadResult, List<ThumbnailContent> thumbnails)> UploadUnEncryptedFileWithTwoThumbnails(AppApiClient appApiClient,
            TargetDrive targetDrive,
            FileSystemType fileSystemType = FileSystemType.Standard)
        {
            var thumbnails = new List<ThumbnailContent>()
            {
                new()
                {
                    PixelHeight = 300,
                    PixelWidth = 300,
                    ContentType = "image/jpeg",
                    Content = TestMedia.ThumbnailBytes300
                },
                new()
                {
                    PixelHeight = 200,
                    PixelWidth = 200,
                    ContentType = "image/jpeg",
                    Content = TestMedia.ThumbnailBytes200
                }
            };

            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = false,
                IsEncrypted = false,
                AppData = new()
                {
                    Content = "some content",
                    FileType = 101,
                    GroupId = default,
                },
                AccessControlList = AccessControlList.OwnerOnly
            };


            var uploadResult = await appApiClient.Drive.UploadFile(targetDrive, fileMetadata, "", thumbnails, fileSystemType: fileSystemType);

            var getHeaderResponse = await appApiClient.Drive.GetFileHeader(uploadResult.File);

            Assert.IsTrue(getHeaderResponse.FileMetadata.GetPayloadDescriptor(WebScaffold.PAYLOAD_KEY).Thumbnails.Count() == thumbnails.Count(), "Missing one or more thumbnails");
            return (uploadResult, thumbnails);
        }
    }
}