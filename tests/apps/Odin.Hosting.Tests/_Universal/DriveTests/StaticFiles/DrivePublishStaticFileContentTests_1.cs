using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Serialization;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Optimization.Cdn;
using Odin.Hosting.Controllers.Base.Cdn;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;

namespace Odin.Hosting.Tests._Universal.DriveTests.StaticFiles
{
    public class DrivePublishStaticFileContentTests_1
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


        public static IEnumerable TestCases()
        {
            yield return new object[]
            {
                new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), 
                HttpStatusCode.MethodNotAllowed
            };
            
            yield return new object[]
            {
                //no permissions
                new AppSpecifyDriveAccess(TargetDrive.NewTargetDrive(), DrivePermission.ReadWrite, new TestPermissionKeyList()), 
                HttpStatusCode.Forbidden,
            };
            
            yield return new object[]
            {
                new AppSpecifyDriveAccess(TargetDrive.NewTargetDrive(), DrivePermission.ReadWrite, new TestPermissionKeyList(PermissionKeys.PublishStaticContent)),
                HttpStatusCode.OK
            };
            
            yield return new object[]
            {
                new OwnerClientContext(TargetDrive.NewTargetDrive()), 
                HttpStatusCode.OK
            };
        }

        [Test(Description = "Publish static content to file, including payload and thumbnails")]
        [TestCaseSource(nameof(TestCases))]
        public async Task CanPublishStaticFileContentWithThumbnails(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
        {
            var identity = TestIdentities.Frodo;
            var ownerClient = _scaffold.CreateOwnerApiClientRedux(identity);
            await ownerClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Some Drive", "", false, false, false);

            await callerContext.Initialize(ownerClient);

            const int section_1_filetype = 100;
            const int section_2_datatype = 888;

            int total_files_uploaded = 2;
            await CreateAnonymousUnEncryptedFile(
                ownerClient.DriveRedux,
                callerContext.TargetDrive,
                fileType: section_1_filetype,
                dataType: 0,
                jsonContent: OdinSystemSerializer.Serialize(new { content = "some content" }),
                tags: [Guid.NewGuid(), Guid.NewGuid()],
                SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2());

            await CreateAnonymousUnEncryptedFile(
                ownerClient.DriveRedux,
                callerContext.TargetDrive,
                fileType: section_1_filetype,
                dataType: 0,
                jsonContent: OdinSystemSerializer.Serialize(new { content = "some content" }),
                tags: [Guid.NewGuid()],
                SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1());

            await CreateAnonymousUnEncryptedFile(
                ownerClient.DriveRedux,
                callerContext.TargetDrive,
                fileType: 0,
                dataType: section_2_datatype,
                jsonContent: OdinSystemSerializer.Serialize(new { content = "stuff" }),
                tags: [Guid.NewGuid()],
                SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1());

            var staticFileClient = new UniversalStaticFileApiClient(identity.OdinId, callerContext.GetFactory());

            //publish a static file
            var publishRequest = new PublishStaticFileRequest()
            {
                Filename = "test-file.ok",
                Config = new StaticFileConfiguration()
                {
                    CrossOriginBehavior = CrossOriginBehavior.AllowAllOrigins
                },
                Sections = new List<QueryParamSection>(),
            };

            publishRequest.Sections.Add(new QueryParamSection()
            {
                Name = $"Section matching filetype ({section_1_filetype})",
                QueryParams = new FileQueryParams()
                {
                    TargetDrive = callerContext.TargetDrive,
                    FileType = new List<int>() { section_1_filetype },
                },

                ResultOptions = new SectionResultOptions()
                {
                    PayloadKeys = new List<string>() { WebScaffold.PAYLOAD_KEY },
                    ExcludePreviewThumbnail = false,
                    IncludeHeaderContent = true
                }
            });

            publishRequest.Sections.Add(new QueryParamSection()
            {
                Name = $"Files matching datatype {section_2_datatype}",
                QueryParams = new FileQueryParams()
                {
                    TargetDrive = callerContext.TargetDrive,
                    DataType = new List<int>() { section_2_datatype },
                },

                ResultOptions = new SectionResultOptions()
                {
                    ExcludePreviewThumbnail = false,
                    IncludeHeaderContent = false
                }
            });

            var response = await staticFileClient.Publish(publishRequest);

            ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Actual status code was {response.StatusCode}");

            if (expectedStatusCode == HttpStatusCode.OK)
            {
                ClassicAssert.NotNull(response.Content);

                var pubResult = response.Content;

                ClassicAssert.AreEqual(pubResult.Filename, publishRequest.Filename);
                ClassicAssert.AreEqual(pubResult.SectionResults.Count, publishRequest.Sections.Count);
                ClassicAssert.AreEqual(pubResult.SectionResults[0].Name, publishRequest.Sections[0].Name);
                ClassicAssert.AreEqual(pubResult.SectionResults[0].FileCount, total_files_uploaded);

                var getFileResponse = await staticFileClient.GetStaticFile(publishRequest.Filename);
                ClassicAssert.True(getFileResponse.IsSuccessStatusCode, getFileResponse.ReasonPhrase);
                ClassicAssert.IsNotNull(getFileResponse.Content);

                ClassicAssert.IsTrue(getFileResponse.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
                ClassicAssert.IsNotNull(values);
                ClassicAssert.IsTrue(values.Single() == "*");

                //TODO: open the file and check it against what was uploaded.  going to have to do some json acrobatics maybe?
                var json = await getFileResponse.Content.ReadAsStringAsync();

                var sectionOutputArray = OdinSystemSerializer.Deserialize<SectionOutput[]>(json);

                ClassicAssert.IsNotNull(sectionOutputArray);
                ClassicAssert.IsTrue(sectionOutputArray.Length == pubResult.SectionResults.Count);
                ClassicAssert.IsTrue(sectionOutputArray.Length == publishRequest.Sections.Count);
            }
        }

        private async Task CreateAnonymousUnEncryptedFile(UniversalDriveApiClient driveClient, TargetDrive targetDrive, int fileType, int dataType,
            string jsonContent,
            List<Guid> tags,
            TestPayloadDefinition payload)
        {
            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = false,
                IsEncrypted = false,
                AppData = new()
                {
                    Tags = tags,
                    Content = jsonContent,
                    FileType = fileType,
                    DataType = dataType,
                    PreviewThumbnail = payload.PreviewThumbnail,
                },
                AccessControlList = new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Anonymous }
            };

            var uploadManifest = new UploadManifest()
            {
                PayloadDescriptors = [payload.ToPayloadDescriptor()]
            };

            var response = await driveClient.UploadNewFile(targetDrive,
                fileMetadata,
                uploadManifest: uploadManifest,
                payloads: [payload]);

            var uploadResult = response.Content;

            Assert.That(uploadResult.File, Is.Not.Null);
            Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
        }
    }
}