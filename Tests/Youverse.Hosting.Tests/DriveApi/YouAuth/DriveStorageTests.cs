using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Transit.Upload;

namespace Youverse.Hosting.Tests.DriveApi.YouAuth
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
            var uploadContext = await this.UploadFile(identity, tag, SecurityGroupType.Connected);

            using (var client = _scaffold.CreateAnonymousApiHttpClient(identity))
            {
                var svc = RestService.For<IDriveTestHttpClientForYouAuth>(client);
                
                var getHeaderResponse = await svc.GetFileHeader(uploadContext.TestAppContext.TargetDrive, uploadContext.UploadedFile.FileId);
                Assert.IsTrue(getHeaderResponse.StatusCode == HttpStatusCode.Forbidden, $"Failed status code.  Value was {getHeaderResponse.StatusCode}");
            }
        }
        
        [Test]
        public async Task ShouldFailToGetSecuredFile_Payload()
        {
            var identity = TestIdentities.Samwise;
            Guid tag = Guid.NewGuid();
            var uploadContext = await this.UploadFile(identity, tag, SecurityGroupType.Connected);

            using (var client = _scaffold.CreateAnonymousApiHttpClient(identity))
            {
                var svc = RestService.For<IDriveTestHttpClientForYouAuth>(client);
                
                var getPayloadStreamResponse = await svc.GetPayload(uploadContext.TestAppContext.TargetDrive, uploadContext.UploadedFile.FileId);
                Assert.IsTrue(getPayloadStreamResponse.StatusCode == HttpStatusCode.Forbidden, $"Failed status code.  Value was {getPayloadStreamResponse.StatusCode}");
                Assert.IsNull(getPayloadStreamResponse.Content);
            }
        }
        
        private async Task<UploadTestUtilsContext> UploadFile(DotYouIdentity identity, Guid tag, SecurityGroupType requiredSecurityGroup)
        {
            List<Guid> tags = new List<Guid>() { tag };

            var uploadFileMetadata = new UploadFileMetadata()
            {
                ContentType = "application/json",
                PayloadIsEncrypted = false,
                AppData = new()
                {
                    ContentIsComplete = false,
                    JsonContent = JsonConvert.SerializeObject(new { message = "We're going to the beach; this is encrypted by the app" }),
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

            return await _scaffold.OwnerApi.Upload(identity, uploadFileMetadata, options);
        }
    }
}