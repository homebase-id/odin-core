using System.Collections.Generic;
using System.Configuration;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal;
using Odin.Hosting.Tests._Universal.ApiClient;
using Odin.Hosting.Tests.YouAuthApi.ApiClient.Drives;
using Refit;
using Guid = System.Guid;

namespace Odin.Hosting.Tests.YouAuthApi.Drive
{
    public class DriveStorageTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
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
        public async Task ShouldFailToGetSecuredFile_Header()
        {
            var identity = TestIdentities.Samwise;
            Guid tag = Guid.NewGuid();
            var uploadContext = await this.UploadFile(identity.OdinId, tag, SecurityGroupType.Connected);

            // var client = new UniversalDriveApiClient(identity.OdinId, new GuestApiClientFactory());
            // var getHeaderResponse = await client.GetFileHeader(
            //     new ExternalFileIdentifier()
            //     {
            //         TargetDrive = uploadContext.UploadResult.File.TargetDrive,
            //         FileId = uploadContext.UploadResult.File.FileId
            //     });
            
            var client = _scaffold.CreateAnonymousApiHttpClient(identity.OdinId);
            var svc = RestService.For<IRefitGuestDriveQuery>(client);
            var getHeaderResponse = await svc.GetFileHeader(
                new ExternalFileIdentifier()
                {
                    TargetDrive = uploadContext.UploadResult.File.TargetDrive,
                    FileId = uploadContext.UploadResult.File.FileId
                });
            ClassicAssert.IsTrue(getHeaderResponse.StatusCode == HttpStatusCode.Forbidden, $"Failed status code.  Value was {getHeaderResponse.StatusCode}");
        }

        [Test]
        public async Task ShouldFailToGetSecuredFile_Payload()
        {
            var identity = TestIdentities.Samwise;
            Guid tag = Guid.NewGuid();
            var uploadContext = await this.UploadFile(identity.OdinId, tag, SecurityGroupType.Connected);
            
            // var client = new UniversalDriveApiClient(identity.OdinId, new GuestApiClientFactory());
            // var getPayloadStreamResponse = await client.GetPayload(
            //     new ExternalFileIdentifier()
            //     {
            //         TargetDrive = uploadContext.UploadResult.File.TargetDrive,
            //         FileId = uploadContext.UploadResult.File.FileId
            //     },
            //     WebScaffold.PAYLOAD_KEY);
            //
            // ClassicAssert.IsTrue(getPayloadStreamResponse.StatusCode == HttpStatusCode.Forbidden, $"Failed status code.  Value was {getPayloadStreamResponse.StatusCode}");

            var client = _scaffold.CreateAnonymousApiHttpClient(identity.OdinId);
            var svc = RestService.For<IRefitGuestDriveQuery>(client);
            
            var getPayloadStreamResponse = await svc.GetPayload(
                new GetPayloadRequest()
                {
                    File = new ExternalFileIdentifier()
                    {
                        TargetDrive = uploadContext.UploadResult.File.TargetDrive,
                        FileId = uploadContext.UploadResult.File.FileId
                    },
                    Key = WebScaffold.PAYLOAD_KEY
                });

            ClassicAssert.IsTrue(getPayloadStreamResponse.StatusCode == HttpStatusCode.Forbidden, $"Failed status code.  Value was {getPayloadStreamResponse.StatusCode}");
            ClassicAssert.IsNull(getPayloadStreamResponse.Content);
        }

        private async Task<(UploadResult UploadResult, UploadFileMetadata UploadedFileMetadata)> UploadFile(OdinId identity, Guid tag,
            SecurityGroupType requiredSecurityGroup)
        {
            List<Guid> tags = new List<Guid>() { tag };

            var uploadFileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = false,
                IsEncrypted = false,
                AppData = new()
                {
                    Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    FileType = 100,
                    DataType = 202,
                    UserDate = new UnixTimeUtc(0),
                    Tags = tags
                },
                AccessControlList = new AccessControlList()
                {
                    RequiredSecurityGroup = requiredSecurityGroup
                }
            };

            var client = _scaffold.CreateOwnerApiClient(TestIdentities.InitializedIdentities[identity]);
            var td = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "a drive", "", true);
            var response = await client.DriveRedux.UploadNewMetadata(td.TargetDriveInfo, uploadFileMetadata);
            var uploadResult = response.Content;
            return (uploadResult, uploadFileMetadata);
        }


        private async Task<UploadTestUtilsContext> UploadFilexx(OdinId identity, Guid tag, SecurityGroupType requiredSecurityGroup)
        {
            List<Guid> tags = new List<Guid>() { tag };

            var uploadFileMetadata = new UploadFileMetadata()
            {
                IsEncrypted = false,
                AllowDistribution = false,
                AppData = new()
                {
                    Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    FileType = 100,
                    DataType = 202,
                    UserDate = new UnixTimeUtc(0),
                    Tags = tags
                },
                AccessControlList = new AccessControlList()
                {
                    RequiredSecurityGroup = requiredSecurityGroup
                }
            };

            TransitTestUtilsOptions options = new TransitTestUtilsOptions()
            {
                PayloadData = "some payload data for good measure",
                DisconnectIdentitiesAfterTransfer = false,
                DriveAllowAnonymousReads = true
            };

            return await _scaffold.OldOwnerApi.Upload(identity, uploadFileMetadata, options);
        }
    }
}