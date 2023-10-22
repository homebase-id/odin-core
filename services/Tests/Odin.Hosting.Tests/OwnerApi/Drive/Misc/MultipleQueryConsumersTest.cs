using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Storage;
using Odin.Core.Time;

namespace Odin.Hosting.Tests.OwnerApi.Drive.Misc
{
    public class MultipleQueryConsumersTest
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
        public async Task WillDisposeWithBothCommentAndStandardFilesAreUsed()
        {
            var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);

            var targetDrive = TargetDrive.NewTargetDrive();
            await frodoOwnerClient.Drive.CreateDrive(targetDrive, "Some drive", "", false, true);

            var standardFile = new UploadFileMetadata()
            {
                ContentType = "application/json",
                PayloadIsEncrypted = false,
                AllowDistribution = true,
                AppData = new()
                {
                    ContentIsComplete = true,
                    JsonContent = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    FileType = 101,
                    DataType = 202,
                    UserDate = new UnixTimeUtc(0),
                    Tags = default
                }
            };

            var standardFileUploadResult = await frodoOwnerClient.Drive.UploadFile(FileSystemType.Standard, targetDrive, standardFile, "");

            var commentFile = new UploadFileMetadata()
            {
                ContentType = "application/json",
                AllowDistribution = true,
                PayloadIsEncrypted = false,
                ReferencedFile = standardFileUploadResult.GlobalTransitIdFileIdentifier,
                AppData = new()
                {
                    ContentIsComplete = false,
                    JsonContent = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    FileType = 909,
                    DataType = 202,
                    UserDate = new UnixTimeUtc(0),
                    Tags = default
                }
            };

            var commentFileUploadResult = await frodoOwnerClient.Drive.UploadFile(FileSystemType.Comment, targetDrive, commentFile, "some payload data", payloadKey:WebScaffold.PAYLOAD_KEY);

            var standardFileResults = await frodoOwnerClient.Drive.QueryBatch(FileSystemType.Standard, new FileQueryParams()
            {
                TargetDrive = targetDrive,
                FileType = new[] { standardFile.AppData.FileType }
            });

            Assert.IsNotNull(standardFileResults.SearchResults.SingleOrDefault(f => f.FileId == standardFileUploadResult.File.FileId));

            var commentFileResults = await frodoOwnerClient.Drive.QueryBatch(FileSystemType.Comment, new FileQueryParams()
            {
                TargetDrive = targetDrive,
                FileType = new[] { commentFile.AppData.FileType }
            });
            Assert.IsNotNull(commentFileResults.SearchResults.SingleOrDefault(f => f.FileId == commentFileUploadResult.File.FileId));
        }
    }
}