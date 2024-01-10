using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Optimization.Cdn;
using Odin.Hosting.Controllers.Base.Cdn;
using Odin.Hosting.Controllers.OwnerToken.Cdn;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;

namespace Odin.Hosting.Tests.OwnerApi.Optimization.Cdn
{
    public class DrivePublishStaticFileContentTests
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

        [Test(Description = "publish static content to file, including payload and thumbnails")]
        public async Task CanPublishStaticFileContentWithThumbnails()
        {
            var identity = TestIdentities.Frodo;
            var testContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(identity);

            const int section_1_filetype = 100;
            const int section_2_datatype = 888;

            int total_files_uploaded = 2;
            await CreateAnonymousUnEncryptedFile(
                testContext,
                fileType: section_1_filetype,
                dataType: 0,
                jsonContent: OdinSystemSerializer.Serialize(new { content = "some content" }),
                tags: new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                SamplePayloadDefinitions.PayloadDefinitionWithThumbnail2);

            await CreateAnonymousUnEncryptedFile(
                testContext,
                fileType: section_1_filetype,
                dataType: 0,
                jsonContent: OdinSystemSerializer.Serialize(new { content = "some content" }),
                tags: new List<Guid>() { Guid.NewGuid() },
                SamplePayloadDefinitions.PayloadDefinitionWithThumbnail1);

            await CreateAnonymousUnEncryptedFile(
                testContext,
                fileType: 0,
                dataType: section_2_datatype,
                jsonContent: OdinSystemSerializer.Serialize(new { content = "stuff" }),
                tags: new List<Guid>() { Guid.NewGuid() },
                SamplePayloadDefinitions.PayloadDefinitionWithThumbnail1);


            var client = _scaffold.CreateOwnerApiClientRedux(identity);
            {
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
                        TargetDrive = testContext.TargetDrive,
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
                        TargetDrive = testContext.TargetDrive,
                        DataType = new List<int>() { section_2_datatype },
                    },

                    ResultOptions = new SectionResultOptions()
                    {
                        ExcludePreviewThumbnail = false,
                        IncludeHeaderContent = false
                    }
                });

                var publishResponse = await client.StaticFilePublisher.Publish(publishRequest);
                Assert.True(publishResponse.IsSuccessStatusCode, publishResponse.ReasonPhrase);
                Assert.NotNull(publishResponse.Content);

                var pubResult = publishResponse.Content;

                Assert.AreEqual(pubResult.Filename, publishRequest.Filename);
                Assert.AreEqual(pubResult.SectionResults.Count, publishRequest.Sections.Count);
                Assert.AreEqual(pubResult.SectionResults[0].Name, publishRequest.Sections[0].Name);
                Assert.AreEqual(pubResult.SectionResults[0].FileCount, total_files_uploaded);


                var getFileResponse = await client.StaticFilePublisher.GetStaticFile(publishRequest.Filename);
                Assert.True(getFileResponse.IsSuccessStatusCode, getFileResponse.ReasonPhrase);
                Assert.IsNotNull(getFileResponse.Content);

                Assert.IsTrue(getFileResponse.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
                Assert.IsNotNull(values);
                Assert.IsTrue(values.Single() == "*");

                //TODO: open the file and check it against what was uploaded.  going to have to do some json acrobatics maybe?
                var json = await getFileResponse.Content.ReadAsStringAsync();

                var sectionOutputArray = OdinSystemSerializer.Deserialize<SectionOutput[]>(json);

                Assert.IsNotNull(sectionOutputArray);
                Assert.IsTrue(sectionOutputArray.Length == pubResult.SectionResults.Count);
                Assert.IsTrue(sectionOutputArray.Length == publishRequest.Sections.Count);
            }
        }

        [Test]
        public async Task CanPublishPublicProfileCard()
        {
            var identity = TestIdentities.Frodo;
            var testContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(identity);
            var client = _scaffold.CreateOwnerApiClientRedux(identity);

            {
                var getProfileCardResponse1 = await client.StaticFilePublisher.GetPublicProfileCard();
                Assert.IsTrue(getProfileCardResponse1.StatusCode == HttpStatusCode.NotFound);


                string expectedJson = "{name:'Sam'}";
                await client.StaticFilePublisher.PublishPublicProfileCard(new PublishPublicProfileCardRequest()
                {
                    ProfileCardJson = expectedJson
                });

                var getProfileCardResponse2 = await client.StaticFilePublisher.GetPublicProfileCard();
                Assert.IsTrue(getProfileCardResponse2.IsSuccessStatusCode);
                Assert.IsNotNull(getProfileCardResponse2.Content);
                Assert.IsTrue(getProfileCardResponse2.ContentHeaders.ContentType.MediaType == MediaTypeNames.Application.Json);
                var json = await getProfileCardResponse2.Content.ReadAsStringAsync();
                Assert.IsNotNull(json);
                Assert.IsTrue(json == expectedJson);
            }
        }

        [Test]
        public async Task CanPublishPublicProfileImage()
        {
            var identity = TestIdentities.Frodo;
            var testContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(identity);
            var client = _scaffold.CreateOwnerApiClientRedux(identity);
            
            {
                var getPublicProfileImage1 = await client.StaticFilePublisher.GetPublicProfileImage();
                Assert.IsTrue(getPublicProfileImage1.StatusCode == HttpStatusCode.NotFound);

                var expectedImage = TestMedia.ThumbnailBytes300;
                await client.StaticFilePublisher.PublishPublicProfileImage(new PublishPublicProfileImageRequest()
                {
                    Image64 = expectedImage.ToBase64(),
                    ContentType = MediaTypeNames.Image.Jpeg
                });

                var getPublicProfileImage2 = await client.StaticFilePublisher.GetPublicProfileImage();
                Assert.IsTrue(getPublicProfileImage2.IsSuccessStatusCode);
                Assert.IsNotNull(getPublicProfileImage2.Content);
                Assert.IsTrue(getPublicProfileImage2.ContentHeaders.ContentType.MediaType == MediaTypeNames.Image.Jpeg);
                var bytes = await getPublicProfileImage2.Content.ReadAsByteArrayAsync();
                Assert.IsNotNull(ByteArrayUtil.EquiByteArrayCompare(expectedImage, bytes));
            }
        }

        private async Task CreateAnonymousUnEncryptedFile(TestAppContext testContext, int fileType, int dataType,
            string jsonContent,
            List<Guid> tags,
            TestPayloadDefinition payload)
        {
            var client = _scaffold.CreateOwnerApiClientRedux(TestIdentities.All[testContext.Identity]);

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

            var response = await client.DriveRedux.UploadNewFile(testContext.TargetDrive,
                fileMetadata,
                uploadManifest: uploadManifest,
                payloads: [payload],
                useGlobalTransitId: false);

            var uploadResult = response.Content;

            Assert.That(uploadResult.File, Is.Not.Null);
            Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
        }
    }
}