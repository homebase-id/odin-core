using System;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Storage;

namespace Odin.Hosting.Tests.OwnerApi.Drive.CommentFileSystem
{
    public class CommentFsOwnerTests
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
        public async Task CanUploadComment()
        {
            var client = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);
            var drive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive", "", false, true);

            var blogMetadata = new UploadFileMetadata()
            {
                ContentType = "text/plain",
                AppData = new UploadAppFileMetaData()
                {
                    FileType = 333,
                    JsonContent = "some blog content here but really in json format",
                }
            };

            var blogPostUploadResult = await client.Drive.UploadFile(FileSystemType.Standard, drive.TargetDriveInfo, blogMetadata, "some payload", payloadKey:WebScaffold.PAYLOAD_KEY);

            var commentMetadata = new UploadFileMetadata()
            {
                ReferencedFile = blogPostUploadResult.GlobalTransitIdFileIdentifier,
                ContentType = "text/plain",
                AppData = new UploadAppFileMetaData()
                {
                    FileType = 10101,
                    JsonContent = "this is a comment about the blog post",
                }
            };

            var commentUploadResult = await client.Drive.UploadFile(FileSystemType.Comment, drive.TargetDriveInfo, commentMetadata, "some payload", payloadKey:WebScaffold.PAYLOAD_KEY);

            var commentFileHeader = await client.Drive.GetFileHeader(FileSystemType.Comment, commentUploadResult.File);

            Assert.IsTrue(commentFileHeader.ServerMetadata.FileSystemType == FileSystemType.Comment);
            Assert.IsTrue(commentFileHeader.FileMetadata.AppData.JsonContent == commentMetadata.AppData.JsonContent);
            Assert.IsTrue(commentFileHeader.FileMetadata.AppData.FileType == commentMetadata.AppData.FileType);
        }


        [Test]
        public async Task FailToGetCommentFromStandardFileSystem()
        {
            var client = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);
            var drive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive", "", false, true);

            var blogMetadata = new UploadFileMetadata()
            {
                ContentType = "text/plain",
                AppData = new UploadAppFileMetaData()
                {
                    FileType = 333,
                    JsonContent = "some blog content here but really in json format",
                }
            };

            var blogPostUploadResult = await client.Drive.UploadFile(FileSystemType.Standard, drive.TargetDriveInfo, blogMetadata, "some payload", payloadKey:WebScaffold.PAYLOAD_KEY);

            var commentMetadata = new UploadFileMetadata()
            {
                ContentType = "text/plain",
                ReferencedFile = blogPostUploadResult.GlobalTransitIdFileIdentifier,
                AppData = new UploadAppFileMetaData()
                {
                    FileType = 10101,
                    JsonContent = "this is a comment about the blog post",
                }
            };

            var commentUploadResult = await client.Drive.UploadFile(FileSystemType.Comment, drive.TargetDriveInfo, commentMetadata, "some payload", payloadKey:WebScaffold.PAYLOAD_KEY);

            try
            {
                var _ = await client.Drive.GetFileHeader(FileSystemType.Standard, commentUploadResult.File);
            }
            catch (Exception)
            {
                Assert.Pass("Exception throw as expected");
            }
        }
    }
}