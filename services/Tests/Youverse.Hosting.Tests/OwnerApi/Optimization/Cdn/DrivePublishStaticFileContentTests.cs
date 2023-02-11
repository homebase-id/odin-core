﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Core.Query;
using Youverse.Core.Services.Drive.Core.Storage;
using Youverse.Core.Services.Optimization.Cdn;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers.OwnerToken.Cdn;
using Youverse.Hosting.Tests.AppAPI;
using Youverse.Hosting.Tests.OwnerApi.Drive;

namespace Youverse.Hosting.Tests.OwnerApi.Optimization.Cdn
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

            var thumbnail1 = new ImageDataContent()
            {
                PixelHeight = 300,
                PixelWidth = 300,
                ContentType = "image/jpeg",
                Content = TestMedia.ThumbnailBytes300
            };

            var thumbnail2 = new ImageDataContent()
            {
                PixelHeight = 400,
                PixelWidth = 400,
                ContentType = "image/jpeg",
                Content = TestMedia.ThumbnailBytes400
            };

            const int section_1_filetype = 100;
            const int section_2_datatype = 888;

            int total_files_uploaded = 2;
            await CreateAnonymousUnEncryptedFile(
                testContext,
                fileType: section_1_filetype,
                dataType: 0,
                jsonContent: DotYouSystemSerializer.Serialize(new { content = "some content" }),
                tags: new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                payloadContent: null,
                previewThumbnail: new ImageDataContent()
                {
                    PixelHeight = 100,
                    PixelWidth = 100,
                    ContentType = "image/png",
                    Content = TestMedia.PreviewPngThumbnailBytes
                },
                additionalThumbs: new List<ImageDataContent>() { thumbnail1, thumbnail2 });

            await CreateAnonymousUnEncryptedFile(
                testContext,
                fileType: section_1_filetype,
                dataType: 0,
                jsonContent: DotYouSystemSerializer.Serialize(new { content = "some content" }),
                tags: new List<Guid>() { Guid.NewGuid() },
                payloadContent: "this is just a bit of text payload".ToUtf8ByteArray(),
                previewThumbnail: new ImageDataContent()
                {
                    PixelHeight = 100,
                    PixelWidth = 100,
                    ContentType = "image/png",
                    Content = TestMedia.PreviewPngThumbnailBytes
                },
                additionalThumbs: new List<ImageDataContent>() { thumbnail2 });

            await CreateAnonymousUnEncryptedFile(
                testContext,
                fileType: 0,
                dataType: section_2_datatype,
                jsonContent: DotYouSystemSerializer.Serialize(new { content = "stuff" }),
                tags: new List<Guid>() { Guid.NewGuid() },
                payloadContent: "payload".ToUtf8ByteArray(),
                previewThumbnail: new ImageDataContent()
                {
                    PixelHeight = 100,
                    PixelWidth = 100,
                    ContentType = "image/png",
                    Content = TestMedia.PreviewPngThumbnailBytes
                },
                additionalThumbs: new List<ImageDataContent>() { thumbnail2 });


            using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(testContext.Identity, out var ownerSharedSecret))
            {
                var staticFileSvc = RefitCreator.RestServiceFor<IStaticFileTestHttpClientForOwner>(client, ownerSharedSecret);

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
                        IncludePayload = true,
                        ExcludePreviewThumbnail = false,
                        IncludeAdditionalThumbnails = true,
                        IncludeJsonContent = true
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
                        IncludePayload = false,
                        ExcludePreviewThumbnail = false,
                        IncludeAdditionalThumbnails = false,
                        IncludeJsonContent = false
                    }
                });

                var publishResponse = await staticFileSvc.Publish(publishRequest);
                Assert.True(publishResponse.IsSuccessStatusCode, publishResponse.ReasonPhrase);
                Assert.NotNull(publishResponse.Content);

                var pubResult = publishResponse.Content;

                Assert.AreEqual(pubResult.Filename, publishRequest.Filename);
                Assert.AreEqual(pubResult.SectionResults.Count, publishRequest.Sections.Count);
                Assert.AreEqual(pubResult.SectionResults[0].Name, publishRequest.Sections[0].Name);
                Assert.AreEqual(pubResult.SectionResults[0].FileCount, total_files_uploaded);

                var getStaticFileSvc = RestService.For<IStaticFileTestHttpClientForOwner>(client);

                var getFileResponse = await getStaticFileSvc.GetStaticFile(publishRequest.Filename);
                Assert.True(getFileResponse.IsSuccessStatusCode, getFileResponse.ReasonPhrase);
                Assert.IsNotNull(getFileResponse.Content);

                Assert.IsTrue(getFileResponse.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
                Assert.IsNotNull(values);
                Assert.IsTrue(values.Single() == "*");

                //TODO: open the file and check it against what was uploaded.  going to have to do some json acrobatics maybe?
                var json = await getFileResponse.Content.ReadAsStringAsync();
                // Console.WriteLine(json);

                var sectionOutputArray = DotYouSystemSerializer.Deserialize<SectionOutput[]>(json);

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
            using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(testContext.Identity, out var ownerSharedSecret))
            {
                var staticFileSvc = RefitCreator.RestServiceFor<IStaticFileTestHttpClientForOwner>(client, ownerSharedSecret);
                var getProfileCardResponse1 = await staticFileSvc.GetPublicProfileCard();
                Assert.IsTrue(getProfileCardResponse1.StatusCode == HttpStatusCode.NotFound);


                string expectedJson = "{name:'Sam'}";
                await staticFileSvc.PublishPublicProfileCard(new PublishPublicProfileCardRequest()
                {
                    ProfileCardJson = expectedJson
                });
                
                var getProfileCardResponse2 = await staticFileSvc.GetPublicProfileCard();
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
            using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(testContext.Identity, out var ownerSharedSecret))
            {
                var staticFileSvc = RefitCreator.RestServiceFor<IStaticFileTestHttpClientForOwner>(client, ownerSharedSecret);
                var getPublicProfileImage1 = await staticFileSvc.GetPublicProfileImage();
                Assert.IsTrue(getPublicProfileImage1.StatusCode == HttpStatusCode.NotFound);

                var expectedImage = TestMedia.ThumbnailBytes300;
                await staticFileSvc.PublishPublicProfileImage(new PublishPublicProfileImageRequest()
                {
                    Image64 = expectedImage.ToBase64(),
                    ContentType = MediaTypeNames.Image.Jpeg
                });
                
                var getPublicProfileImage2 = await staticFileSvc.GetPublicProfileImage();
                Assert.IsTrue(getPublicProfileImage2.IsSuccessStatusCode);
                Assert.IsNotNull(getPublicProfileImage2.Content);
                Assert.IsTrue(getPublicProfileImage2.ContentHeaders.ContentType.MediaType == MediaTypeNames.Image.Jpeg);
                var bytes = await getPublicProfileImage2.Content.ReadAsByteArrayAsync();
                Assert.IsNotNull(ByteArrayUtil.EquiByteArrayCompare(expectedImage, bytes));
            }
        }

        public async Task CreateAnonymousUnEncryptedFile(TestAppContext testContext, int fileType, int dataType, string jsonContent, List<Guid> tags, byte[] payloadContent,
            ImageDataContent previewThumbnail,
            List<ImageDataContent> additionalThumbs)
        {
            using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(testContext.Identity, out var ownerSharedSecret))
            {
                var transferIv = ByteArrayUtil.GetRndByteArray(16);
                var keyHeader = KeyHeader.NewRandom16();

                var instructionSet = new UploadInstructionSet()
                {
                    TransferIv = transferIv,
                    StorageOptions = new StorageOptions()
                    {
                        Drive = testContext.TargetDrive
                    }
                };

                var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
                var instructionStream = new MemoryStream(bytes);


                var descriptor = new UploadFileDescriptor()
                {
                    EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref ownerSharedSecret),
                    FileMetadata = new()
                    {
                        ContentType = "application/json",
                        PayloadIsEncrypted = false,
                        AppData = new()
                        {
                            Tags = tags,
                            ContentIsComplete = payloadContent == null,
                            JsonContent = jsonContent,
                            FileType = fileType,
                            DataType = dataType,
                            PreviewThumbnail = previewThumbnail,
                            AdditionalThumbnails = additionalThumbs
                        },
                        AccessControlList = new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Anonymous }
                    },
                };

                var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, transferIv, ref ownerSharedSecret);

                var additionalThumbnailContent = additionalThumbs?.Select(thumb =>
                    new StreamPart(new MemoryStream(thumb.Content), thumb.GetFilename(), thumb.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail))
                ).ToArray();

                var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
                var response = await driveSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadContent == null ? new MemoryStream() : new MemoryStream(payloadContent), "payload.encrypted", "application/x-binary",
                        Enum.GetName(MultipartUploadParts.Payload)),
                    additionalThumbnailContent ?? Array.Empty<StreamPart>());

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var uploadResult = response.Content;

                Assert.That(uploadResult.File, Is.Not.Null);
                Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                Assert.IsTrue(uploadResult.File.TargetDrive.IsValid());

                Assert.That(uploadResult.RecipientStatus, Is.Null);
                var uploadedFile = uploadResult.File;

                //
                // Retrieve the file header that was uploaded; test it matches; 
                //
                var getFilesDriveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, ownerSharedSecret);
                var fileResponse = await getFilesDriveSvc.GetFileHeader(uploadedFile);

                Assert.That(fileResponse.IsSuccessStatusCode, Is.True);
                Assert.That(fileResponse.Content, Is.Not.Null);

                var clientFileHeader = fileResponse.Content;

                Assert.That(clientFileHeader.FileMetadata, Is.Not.Null);
                Assert.That(clientFileHeader.FileMetadata.AppData, Is.Not.Null);

                Assert.That(clientFileHeader.FileMetadata.ContentType, Is.EqualTo(descriptor.FileMetadata.ContentType));
                CollectionAssert.AreEquivalent(clientFileHeader.FileMetadata.AppData.Tags, descriptor.FileMetadata.AppData.Tags);
                Assert.That(clientFileHeader.FileMetadata.AppData.JsonContent, Is.EqualTo(descriptor.FileMetadata.AppData.JsonContent));
                Assert.That(clientFileHeader.FileMetadata.AppData.ContentIsComplete, Is.EqualTo(descriptor.FileMetadata.AppData.ContentIsComplete));

                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader, Is.Not.Null);
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.Null);
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv.Length, Is.GreaterThanOrEqualTo(16));
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.EqualTo(Guid.Empty.ToByteArray()), "Iv should be all zeros because PayloadIsEncrypted = false");
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes));

                //validate preview thumbnail
                Assert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.ContentType == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.ContentType);
                Assert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.PixelHeight == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelHeight);
                Assert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.PixelWidth == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelWidth);
                Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(descriptor.FileMetadata.AppData.PreviewThumbnail.Content, clientFileHeader.FileMetadata.AppData.PreviewThumbnail.Content));

                Assert.IsTrue(clientFileHeader.FileMetadata.AppData.AdditionalThumbnails.Count() == (additionalThumbs?.Count ?? 0));

                //
                // If payload was uploaded, get the payload that was uploaded, test it
                // 
                if(payloadContent != null)
                {
                    var payloadResponse = await getFilesDriveSvc.GetPayload(uploadedFile);
                    Assert.That(payloadResponse.IsSuccessStatusCode, Is.True);
                    Assert.That(payloadResponse.Content, Is.Not.Null);

                    var payloadResponseBytes = await payloadResponse.Content.ReadAsByteArrayAsync();
                    Assert.That(payloadContent ?? Array.Empty<byte>(), Is.EqualTo(payloadResponseBytes));
                }

                //
                // Validate additional thumbnails
                //

                if (null != additionalThumbs)
                {
                    // var descriptorList = descriptor.FileMetadata.AppData.AdditionalThumbnails.ToList();
                    var clientFileHeaderList = clientFileHeader.FileMetadata.AppData.AdditionalThumbnails.ToList();

                    //there should be the same number of thumbnails on the server as we sent; order should match
                    for (int i = 0; i < additionalThumbs.Count - 1; i++)
                    {
                        var thumbnailInDescriptor = additionalThumbs[i];
                        Assert.IsTrue(thumbnailInDescriptor.ContentType == clientFileHeaderList[i].ContentType);
                        Assert.IsTrue(thumbnailInDescriptor.PixelWidth == clientFileHeaderList[i].PixelWidth);
                        Assert.IsTrue(thumbnailInDescriptor.PixelHeight == clientFileHeaderList[i].PixelHeight);

                        var thumbnailResponse = await getFilesDriveSvc.GetThumbnail(new GetThumbnailRequest()
                        {
                            File = uploadedFile,
                            Height = thumbnailInDescriptor.PixelHeight,
                            Width = thumbnailInDescriptor.PixelWidth
                        });

                        Assert.IsTrue(thumbnailResponse.IsSuccessStatusCode);
                        Assert.IsNotNull(thumbnailResponse.Content);

                        Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnailInDescriptor.Content, await thumbnailResponse!.Content!.ReadAsByteArrayAsync()));
                    }
                }

                keyHeader.AesKey.Wipe();
            }
        }
    }
}