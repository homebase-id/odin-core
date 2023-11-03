using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Time;
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

        [Test]
        public async Task ShouldFailToGetSecuredFile_Header()
        {
            var identity = TestIdentities.Samwise;
            Guid tag = Guid.NewGuid();
            var uploadContext = await this.UploadFile(identity.OdinId, tag, SecurityGroupType.Connected);

            var client = _scaffold.CreateAnonymousApiHttpClient(identity.OdinId);
            {
                var svc = RestService.For<IRefitGuestDriveQuery>(client);

                var getHeaderResponse = await svc.GetFileHeader(
                    new ExternalFileIdentifier()
                    {
                        TargetDrive = uploadContext.UploadedFile.TargetDrive,
                        FileId = uploadContext.UploadedFile.FileId
                    });
                Assert.IsTrue(getHeaderResponse.StatusCode == HttpStatusCode.Forbidden, $"Failed status code.  Value was {getHeaderResponse.StatusCode}");
            }
        }

        [Test]
        public async Task ShouldFailToGetSecuredFile_Payload()
        {
            var identity = TestIdentities.Samwise;
            Guid tag = Guid.NewGuid();
            var uploadContext = await this.UploadFile(identity.OdinId, tag, SecurityGroupType.Connected);

            var client = _scaffold.CreateAnonymousApiHttpClient(identity.OdinId);
            {
                var svc = RestService.For<IRefitGuestDriveQuery>(client);

                var getPayloadStreamResponse = await svc.GetPayload(
                    new GetPayloadRequest()
                    {
                        File = new ExternalFileIdentifier()
                        {
                            TargetDrive = uploadContext.UploadedFile.TargetDrive,
                            FileId = uploadContext.UploadedFile.FileId
                        }, 
                        Key = WebScaffold.PAYLOAD_KEY
                    });
                Assert.IsTrue(getPayloadStreamResponse.StatusCode == HttpStatusCode.Forbidden, $"Failed status code.  Value was {getPayloadStreamResponse.StatusCode}");
                Assert.IsNull(getPayloadStreamResponse.Content);
            }
        }

        private async Task<UploadTestUtilsContext> UploadFile(OdinId identity, Guid tag, SecurityGroupType requiredSecurityGroup)
        {
            List<Guid> tags = new List<Guid>() { tag };

            var uploadFileMetadata = new UploadFileMetadata()
            {
                PayloadIsEncrypted = false,
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
                ProcessOutbox = false,
                ProcessTransitBox = false,
                DisconnectIdentitiesAfterTransfer = false,
                DriveAllowAnonymousReads = true
            };

            return await _scaffold.OldOwnerApi.Upload(identity, uploadFileMetadata, options);
        }
    }
}