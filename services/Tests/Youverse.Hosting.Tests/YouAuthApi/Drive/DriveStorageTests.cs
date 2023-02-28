using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Hosting.Tests.DriveApi.YouAuth;
using Guid = System.Guid;

namespace Youverse.Hosting.Tests.YouAuthApi.Drive
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

            using (var client = _scaffold.CreateAnonymousApiHttpClient(identity.OdinId))
            {
                var svc = RestService.For<IDriveTestHttpClientForYouAuth>(client);

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

            using (var client = _scaffold.CreateAnonymousApiHttpClient(identity.OdinId))
            {
                var svc = RestService.For<IDriveTestHttpClientForYouAuth>(client);

                var getPayloadStreamResponse = await svc.GetPayload(
                    new ExternalFileIdentifier()
                    {
                        TargetDrive = uploadContext.UploadedFile.TargetDrive,
                        FileId = uploadContext.UploadedFile.FileId
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
                ContentType = "application/json",
                PayloadIsEncrypted = false,
                AllowDistribution = true,
                AppData = new()
                {
                    ContentIsComplete = false,
                    JsonContent = DotYouSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    FileType = 100,
                    DataType = 202,
                    UserDate = 0,
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