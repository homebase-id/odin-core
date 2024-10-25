using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Hosting.Tests.OwnerApi.ApiClient;
using Odin.Hosting.Tests.YouAuthApi.ApiClient;

namespace Odin.Hosting.Tests.YouAuthApi.Auth
{
    public class BasicAuthenticationTests
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
        public async Task YouAuthDomainCanAccessAuthorizedContentViaCircle()
        {
            const string domain = "amazoom.org";
            const string jsonContent = "some content";
            var identity = TestIdentities.Merry;
            var merryClient = new OwnerApiClient(_scaffold.OldOwnerApi, identity);

            // Create a drive
            var targetDrive = await merryClient.Drive.CreateDrive(
                TargetDrive.NewTargetDrive(), "A secured Drive", "", allowAnonymousReads: false, ownerOnly: false);

            // Create a circle
            var circle = await merryClient.Membership.CreateCircle("A circle", new PermissionSetGrantRequest()
            {
                PermissionSet = new PermissionSet(),
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = targetDrive.TargetDriveInfo,
                            Permission = DrivePermission.Read
                        }
                    }
                }
            });

            var uploadResult = await UploadFile(identity, targetDrive.TargetDriveInfo, circle.Id, jsonContent);
            await AddYouAuthDomain(identity, domain, new List<GuidId>() { circle.Id });

            var cat = await RegisterClient(identity, domain);

            var youAuthApiClient = new YouAuthApiClient(identity, cat);

            var getFileHeaderResponse = await youAuthApiClient.Drives.GetFileHeader(uploadResult.File);
            Assert.IsTrue(getFileHeaderResponse.IsSuccessStatusCode, $"Status code returned: {getFileHeaderResponse.StatusCode}");
            var fileHeader = getFileHeaderResponse.Content;
            Assert.IsTrue(fileHeader.FileMetadata.AppData.Content == jsonContent);
        }

        private async Task<UploadResult> UploadFile(TestIdentity identity, TargetDrive targetDrive, GuidId circleId, string jsonContent)
        {
            var ownerClient = new OwnerApiClient(_scaffold.OldOwnerApi, identity);

            var standardFile = new UploadFileMetadata()
            {
                IsEncrypted = false,
                AllowDistribution = true,
                AppData = new()
                {
                    Content = jsonContent,
                    FileType = 101,
                    DataType = 202,
                    UserDate = new UnixTimeUtc(0),
                    Tags = default
                },
                AccessControlList = new AccessControlList()
                {
                    RequiredSecurityGroup = SecurityGroupType.Authenticated,
                    CircleIdList = new List<Guid>() { circleId }
                }
            };

            return await ownerClient.Drive.UploadFile(FileSystemType.Standard, targetDrive, standardFile);
        }

        [Test]
        public void ConnectedIdentityCanAccessAuthorizedContent()
        {
            Assert.Inconclusive("todo");
        }

        private async Task AddYouAuthDomain(TestIdentity identity, string domainName, List<GuidId> circleIds)
        {
            var domain = new AsciiDomainName(domainName);

            var client = new OwnerApiClient(_scaffold.OldOwnerApi, identity);
            var response = await client.YouAuth.RegisterDomain(domain, circleIds);

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            Assert.IsNotNull(response.Content);
        }

        private async Task<ClientAccessToken> RegisterClient(TestIdentity identity, string domainName)
        {
            var client = new OwnerApiClient(_scaffold.OldOwnerApi, identity);

            var domain = new AsciiDomainName(domainName);

            //register a client for domain1
            var domain1ClientRegistrationResponse = await client.YouAuth.RegisterClient(domain, "some friendly name");
            Assert.IsTrue(domain1ClientRegistrationResponse.IsSuccessStatusCode);

            return ClientAccessToken.FromPortableBytes(domain1ClientRegistrationResponse.Content.Data);
        }
    }
}