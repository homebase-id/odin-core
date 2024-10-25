using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Optimization.Cdn;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Hosting.Controllers.Base.Cdn;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Refit;

namespace Odin.Hosting.Tests.Performance
{
    public class PublicStaticFilePerformanceTest
    {
        // For the performance test
        private const int MAXTHREADS = 12;
        const int MAXITERATIONS = 100;

        UniversalStaticFileApiClient _getUniversalStaticFileSvc;
        PublishStaticFileRequest publishRequest;
        StaticFilePublishResult pubResult;


        /*
         * File size is : 78,981 bytes
         *

    TaskPerformanceTest
      Duration: 32.2 sec

              Standard Output:
                2023-05-06 Host [SEMIBEASTII]
                Threads   : 12
                Iterations: 10,000
                Wall Time : 31,328ms
                Minimum   : 0ms
                Maximum   : 46ms
                Average   : 2ms
                Median    : 1ms
                Capacity  : 3,830 / second
                RSA Encryptions 1, Decryptions 9
                RSA Keys Created 5, Keys Expired 0
                DB Opened 12, Closed 0
                Bandwidth : 304,830,000 bytes / second

        After database caching:
TaskPerformanceTest
  Duration: 28.4 sec

                Standard Output:
                2023-06-01 Host [SEMIBEASTII]
                Threads   : 12
                Iterations: 10,000
                Wall Time : 27,816ms
                Minimum   : 0ms
                Maximum   : 47ms
                Average   : 2ms
                Median    : 1ms
                Capacity  : 4,314 / second
                RSA Encryptions 1, Decryptions 9
                RSA Keys Created 5, Keys Expired 0
                DB Opened 12, Closed 0
                Bandwidth : 343,317,000 bytes / second

         */
        [Test]
        public async Task TaskPerformanceTest()
        {
            //
            // Some initialization to prepare for the test
            //
            var identity = TestIdentities.Frodo;
            var testContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(identity);

            var thumbnail1 = new ThumbnailContent()
            {
                PixelHeight = 300,
                PixelWidth = 300,
                ContentType = "image/jpeg",
                Content = TestMedia.ThumbnailBytes300
            };

            var thumbnail2 = new ThumbnailContent()
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
                jsonContent: OdinSystemSerializer.Serialize(new { content = "some content" }),
                tags: new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                payloadContent: "some payload content".ToUtf8ByteArray(),
                previewThumbnail: new ThumbnailContent()
                {
                    PixelHeight = 100,
                    PixelWidth = 100,
                    ContentType = "image/png",
                    Content = TestMedia.PreviewPngThumbnailBytes
                },
                additionalThumbs: new List<ThumbnailContent>() { thumbnail1, thumbnail2 });

            await CreateAnonymousUnEncryptedFile(
                testContext,
                fileType: section_1_filetype,
                dataType: 0,
                jsonContent: OdinSystemSerializer.Serialize(new { content = "some content" }),
                tags: new List<Guid>() { Guid.NewGuid() },
                payloadContent: "this is just a bit of text payload".ToUtf8ByteArray(),
                previewThumbnail: new ThumbnailContent()
                {
                    PixelHeight = 100,
                    PixelWidth = 100,
                    ContentType = "image/png",
                    Content = TestMedia.PreviewPngThumbnailBytes
                },
                additionalThumbs: new List<ThumbnailContent>() { thumbnail2 });

            await CreateAnonymousUnEncryptedFile(
                testContext,
                fileType: 0,
                dataType: section_2_datatype,
                jsonContent: OdinSystemSerializer.Serialize(new { content = "stuff" }),
                tags: new List<Guid>() { Guid.NewGuid() },
                payloadContent: "payload".ToUtf8ByteArray(),
                previewThumbnail: new ThumbnailContent()
                {
                    PixelHeight = 100,
                    PixelWidth = 100,
                    ContentType = "image/png",
                    Content = TestMedia.PreviewPngThumbnailBytes
                },
                additionalThumbs: new List<ThumbnailContent>() { thumbnail2 });


            var client = _scaffold.CreateOwnerApiClientRedux(identity);


            //publish a static file
            publishRequest = new PublishStaticFileRequest()
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
            if (!publishResponse.IsSuccessStatusCode)
                Console.WriteLine("staticFileSvc.Publish(publishRequest): " + publishResponse.ReasonPhrase);

            Assert.True(publishResponse.IsSuccessStatusCode, publishResponse.ReasonPhrase);
            Assert.NotNull(publishResponse.Content);

            pubResult = publishResponse.Content;

            Assert.AreEqual(pubResult.Filename, publishRequest.Filename);
            Assert.AreEqual(pubResult.SectionResults.Count, publishRequest.Sections.Count);
            Assert.AreEqual(pubResult.SectionResults[0].Name, publishRequest.Sections[0].Name);
            Assert.AreEqual(pubResult.SectionResults[0].FileCount, total_files_uploaded);

            _getUniversalStaticFileSvc = client.StaticFilePublisher;

            await PerformanceFramework.ThreadedTestAsync(MAXTHREADS, MAXITERATIONS, CanPublishStaticFileContentWithThumbnails);
        }

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


        // First make this test pass, then change it from a test to something else.
        //
        // [Test(Description = "publish static content to file, including payload and thumbnails")]
        public async Task<(long, long[])> CanPublishStaticFileContentWithThumbnails(int threadno, int iterations)
        {
            long[] timers = new long[iterations];
            Debug.Assert(timers.Length == iterations);
            var sw = new Stopwatch();
            long fileByteLength = 0;


            //
            // I presume here we retrieve the file and download it
            //
            for (int count = 0; count < iterations; count++)
            {
                sw.Restart();

                // Do all the work here
                var getFileResponse = await _getUniversalStaticFileSvc.GetStaticFile(publishRequest.Filename);
                if (!getFileResponse.IsSuccessStatusCode)
                    Console.WriteLine("GetStaticFile(): " + getFileResponse.ReasonPhrase);
                Assert.True(getFileResponse.IsSuccessStatusCode, getFileResponse.ReasonPhrase);
                Assert.IsNotNull(getFileResponse.Content);

                Assert.IsTrue(getFileResponse.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
                Assert.IsNotNull(values);
                Assert.IsTrue(values.Single() == "*");

                //TODO: open the file and check it against what was uploaded.  going to have to do some json acrobatics maybe?
                var json = await getFileResponse.Content.ReadAsStringAsync();
                fileByteLength += json.Length;
                // Console.WriteLine(json);

                var sectionOutputArray = OdinSystemSerializer.Deserialize<SectionOutput[]>(json);

                Assert.IsNotNull(sectionOutputArray);
                Assert.IsTrue(sectionOutputArray.Length == pubResult.SectionResults.Count);
                Assert.IsTrue(sectionOutputArray.Length == publishRequest.Sections.Count);

                //
                // Suggestion that you first simply try to load a static URL here.
                //

                // Finished doing all the work
                timers[count] = sw.ElapsedMilliseconds;
                //
                // If you want to introduce a delay be sure to use: await Task.Delay(1);
                // Take.Delay() is very inaccurate.
            }

            return (fileByteLength, timers);
        }


        public async Task CreateAnonymousUnEncryptedFile(TestAppContext testContext, int fileType, int dataType,
            string jsonContent, List<Guid> tags, byte[] payloadContent,
            ThumbnailContent previewThumbnail,
            List<ThumbnailContent> additionalThumbs)
        {
            var client =
                _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(testContext.Identity, out var ownerSharedSecret);
            {
                var transferIv = ByteArrayUtil.GetRndByteArray(16);
                var keyHeader = KeyHeader.NewRandom16();

                var payloadKey = WebScaffold.PAYLOAD_KEY;
                var instructionSet = new UploadInstructionSet()
                {
                    TransferIv = transferIv,
                    StorageOptions = new StorageOptions()
                    {
                        Drive = testContext.TargetDrive
                    },
                    Manifest = new UploadManifest()
                };


                var descriptor = new UploadFileDescriptor()
                {
                    EncryptedKeyHeader =
                        EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref ownerSharedSecret),
                    FileMetadata = new()
                    {
                        AllowDistribution = false,
                        IsEncrypted = false,
                        AppData = new()
                        {
                            Tags = tags,
                            Content = jsonContent,
                            FileType = fileType,
                            DataType = dataType,
                            PreviewThumbnail = previewThumbnail
                        },
                        AccessControlList = new AccessControlList()
                            { RequiredSecurityGroup = SecurityGroupType.Anonymous }
                    },
                };

                var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref ownerSharedSecret);

                var additionalThumbnailContent = additionalThumbs?.Select(thumb =>
                    new StreamPart(new MemoryStream(thumb.Content), thumb.GetFilename(), thumb.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail))
                ).ToArray();


                instructionSet.Manifest.PayloadDescriptors.Add(new UploadManifestPayloadDescriptor()
                {
                    PayloadKey = WebScaffold.PAYLOAD_KEY,
                    Thumbnails = additionalThumbs?.Select(thumb => new UploadedManifestThumbnailDescriptor()
                    {
                        ThumbnailKey = thumb.GetFilename(WebScaffold.PAYLOAD_KEY),
                        PixelWidth = thumb.PixelWidth,
                        PixelHeight = thumb.PixelHeight
                    })
                });

                var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
                var instructionStream = new MemoryStream(bytes);

                var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
                var response = await driveSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json",
                        Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json",
                        Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadContent == null ? new MemoryStream() : new MemoryStream(payloadContent),
                        payloadKey, "application/x-binary",
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
                var getFilesDriveSvc =
                    RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, ownerSharedSecret);
                var fileResponse = await getFilesDriveSvc.GetFileHeaderAsPost(uploadedFile);

                Assert.That(fileResponse.IsSuccessStatusCode, Is.True);
                Assert.That(fileResponse.Content, Is.Not.Null);

                var clientFileHeader = fileResponse.Content;

                Assert.That(clientFileHeader.FileMetadata, Is.Not.Null);
                Assert.That(clientFileHeader.FileMetadata.AppData, Is.Not.Null);


                CollectionAssert.AreEquivalent(clientFileHeader.FileMetadata.AppData.Tags,
                    descriptor.FileMetadata.AppData.Tags);
                Assert.That(clientFileHeader.FileMetadata.AppData.Content,
                    Is.EqualTo(descriptor.FileMetadata.AppData.Content));

                if (payloadContent?.Any() ?? false)
                {
                    Assert.IsTrue(clientFileHeader.FileMetadata.Payloads.Count == 1);
                }
                else
                {
                    Assert.IsTrue(clientFileHeader.FileMetadata.Payloads.Count == 0);
                }

                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader, Is.Not.Null);
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.Null);
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv.Length, Is.GreaterThanOrEqualTo(16));
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.EqualTo(Guid.Empty.ToByteArray()),
                    "Iv should be all zeros because PayloadIsEncrypted = false");
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes));

                //validate preview thumbnail
                Assert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.ContentType ==
                              clientFileHeader.FileMetadata.AppData.PreviewThumbnail.ContentType);
                Assert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.PixelHeight ==
                              clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelHeight);
                Assert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.PixelWidth ==
                              clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelWidth);
                Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(
                    descriptor.FileMetadata.AppData.PreviewThumbnail.Content,
                    clientFileHeader.FileMetadata.AppData.PreviewThumbnail.Content));

                Assert.IsTrue(clientFileHeader.FileMetadata.GetPayloadDescriptor(payloadKey).Thumbnails.Count() == (additionalThumbs?.Count ?? 0));

                //
                // If payload was uploaded, get the payload that was uploaded, test it
                // 
                if (payloadContent != null)
                {
                    var payloadResponse = await getFilesDriveSvc.GetPayloadPost(new GetPayloadRequest() { File = uploadedFile, Key = WebScaffold.PAYLOAD_KEY });
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
                    // var descriptorList = descriptor.FileMetadata.Thumbnails.ToList();
                    var clientFileHeaderList = clientFileHeader.FileMetadata.GetPayloadDescriptor(WebScaffold.PAYLOAD_KEY).Thumbnails.ToList();

                    //there should be the same number of thumbnails on the server as we sent; order should match
                    for (int i = 0; i < additionalThumbs.Count - 1; i++)
                    {
                        var thumbnailInDescriptor = additionalThumbs[i];
                        Assert.IsTrue(thumbnailInDescriptor.ContentType == clientFileHeaderList[i].ContentType);
                        Assert.IsTrue(thumbnailInDescriptor.PixelWidth == clientFileHeaderList[i].PixelWidth);
                        Assert.IsTrue(thumbnailInDescriptor.PixelHeight == clientFileHeaderList[i].PixelHeight);

                        var thumbnailResponse = await getFilesDriveSvc.GetThumbnailPost(new GetThumbnailRequest()
                        {
                            File = uploadedFile,
                            Height = thumbnailInDescriptor.PixelHeight,
                            Width = thumbnailInDescriptor.PixelWidth,
                            PayloadKey = WebScaffold.PAYLOAD_KEY
                        });

                        Assert.IsTrue(thumbnailResponse.IsSuccessStatusCode);
                        Assert.IsNotNull(thumbnailResponse.Content);

                        Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnailInDescriptor.Content,
                            await thumbnailResponse!.Content!.ReadAsByteArrayAsync()));
                    }
                }

                keyHeader.AesKey.Wipe();
            }
        }
    }
}