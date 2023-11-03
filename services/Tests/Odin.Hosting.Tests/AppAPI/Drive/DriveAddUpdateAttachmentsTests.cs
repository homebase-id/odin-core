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
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Storage;
using Odin.Hosting.Tests.AppAPI.ApiClient;

namespace Odin.Hosting.Tests.AppAPI.Drive
{
    public class DriveAddUpdateAttachmentsTests
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
        public async Task FailToAddThumbnailToFileThatDoesNotExist()
        {
            var (appApiClient, targetDrive) = await CreateApp(TestIdentities.Samwise);

            // var (uploadResult, originalThumbnails) = await UploadUnEncryptedFileWithTwoThumbnails(appApiClient, targetDrive);
            // var targetFile = uploadResult.File;

            //invalid file but valid target drive
            var targetFile = new ExternalFileIdentifier()
            {
                FileId = Guid.NewGuid(),
                TargetDrive = targetDrive
            };

            var thumbnailsToAdd = new List<ThumbnailContent>()
            {
                new()
                {
                    PixelHeight = 400,
                    PixelWidth = 400,
                    ContentType = "image/jpeg",
                    Content = TestMedia.ThumbnailBytes400
                }
            };

            var (_, response) = await appApiClient.Drive.UploadAttachments(targetFile, thumbnailsToAdd);
            Assert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest);
            var problemDetails = OdinSystemSerializer.Deserialize<ProblemDetails>(response!.Error!.Content!);
            Assert.IsNotNull(problemDetails);
            Assert.IsTrue(int.Parse(problemDetails.Extensions["errorCode"].ToString() ?? string.Empty) == (int)OdinClientErrorCode.CannotOverwriteNonExistentFile);
        }

        [Test]
        public async Task CanRemoveThumbnail()
        {
            var (appApiClient, targetDrive) = await CreateApp(TestIdentities.Samwise);

            var (uploadResult, originalThumbnails) = await UploadUnEncryptedFileWithTwoThumbnails(appApiClient, targetDrive);
            var targetFile = uploadResult.File;

            var originalFile = await appApiClient.Drive.GetFileHeader(targetFile);

            var thumbnailToRemove = originalThumbnails.First();
            var deleteThumbnailResult =
                await appApiClient.Drive.DeleteThumbnail(uploadResult.File, thumbnailToRemove.PixelWidth, thumbnailToRemove.PixelHeight);

            //header should have all thumbnails from original upload and the new ones
            var updatedHeader = await appApiClient.Drive.GetFileHeader(targetFile);

            Assert.IsTrue(updatedHeader.FileMetadata.VersionTag != originalFile.FileMetadata.VersionTag, "Version tag should have been updated");
            Assert.IsTrue(updatedHeader.FileMetadata.Updated > originalFile.FileMetadata.Updated, "header modified date should have been updated");
            Assert.IsTrue(updatedHeader.FileMetadata.VersionTag == deleteThumbnailResult.NewVersionTag);
            Assert.IsTrue(updatedHeader.FileMetadata.Thumbnails.Count() ==
                          originalFile.FileMetadata.Thumbnails.Count() - 1);

            var getThumbResponse = await appApiClient.Drive.GetThumbnail(uploadResult.File, thumbnailToRemove.PixelWidth, thumbnailToRemove.PixelHeight,
                directMatchOnly: true);
            Assert.IsTrue(getThumbResponse.StatusCode == HttpStatusCode.NotFound);
        }

        [Test]
        public async Task CanAddAndUpdateThumbnailToExistingFile()
        {
            var (appApiClient, targetDrive) = await CreateApp(TestIdentities.Samwise);

            var (uploadResult, originalThumbnails) = await UploadUnEncryptedFileWithTwoThumbnails(appApiClient, targetDrive);
            var targetFile = uploadResult.File;

            var originalFile = await appApiClient.Drive.GetFileHeader(targetFile);

            var thumbnailsToUpdate = new List<ThumbnailContent>()
            {
                new()
                {
                    PixelHeight = 300,
                    PixelWidth = 300,
                    ContentType = "image/jpeg",
                    Content = TestMedia.ThumbnailBytes300Update
                }
            };

            var thumbnailsToAdd = new List<ThumbnailContent>()
            {
                new()
                {
                    PixelHeight = 400,
                    PixelWidth = 400,
                    ContentType = "image/jpeg",
                    Content = TestMedia.ThumbnailBytes400
                }
            };

            var finalThumbnailList = originalThumbnails.Concat(thumbnailsToAdd).ToList();

            var (_, response) = await appApiClient.Drive.UploadAttachments(targetFile, thumbnailsToAdd.Concat(thumbnailsToUpdate).ToList());
            Assert.IsTrue(response.IsSuccessStatusCode);

            var uploadAttachmentsResult = response.Content;
            Assert.IsNotNull(uploadAttachmentsResult);

            //header should have all thumbnails from original upload and the new ones
            var updatedHeader = await appApiClient.Drive.GetFileHeader(targetFile);

            Assert.IsTrue(updatedHeader.FileMetadata.VersionTag != originalFile.FileMetadata.VersionTag, "Version tag should have been updated");
            Assert.IsTrue(updatedHeader.FileMetadata.Updated > originalFile.FileMetadata.Updated, "header modified date should have been updated");
            Assert.IsTrue(updatedHeader.FileMetadata.VersionTag == uploadAttachmentsResult.NewVersionTag);
            Assert.IsTrue(updatedHeader.FileMetadata.Thumbnails.Count() == finalThumbnailList.Count(),
                $"Count was {updatedHeader.FileMetadata.Thumbnails.Count()} but should be {finalThumbnailList.Count()} ");

            var missing = updatedHeader.FileMetadata.Thumbnails.Except(finalThumbnailList);
            Assert.IsFalse(missing.Any());

            foreach (var thumb in updatedHeader.FileMetadata.Thumbnails)
            {
                var getThumbResponse = await appApiClient.Drive.GetThumbnail(uploadResult.File, thumb.PixelWidth, thumb.PixelHeight);
                Assert.IsTrue(getThumbResponse.IsSuccessStatusCode);
                Assert.IsTrue(getThumbResponse!.ContentHeaders!.ContentLength > 0);
            }
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

            Assert.IsTrue(getHeaderResponse.FileMetadata.Thumbnails.Count() == thumbnails.Count(), "Missing one or more thumbnails");
            return (uploadResult, thumbnails);
        }
    }
}
