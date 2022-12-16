using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.JsonPatch.Internal;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Optimization.Cdn;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers.OwnerToken.Cdn;
using Youverse.Hosting.Tests.AppAPI;
using Youverse.Hosting.Tests.OwnerApi.Drive;
using Youverse.Hosting.Tests.OwnerApi.Optimization.Cdn;

namespace Youverse.Hosting.Tests.Performance
{
    public class MyPerformance
    {
        // For the performance test
        private const int
            MAXTHREADS =
                2; // Should be at least 2 * your CPU cores. Can still be nice to test sometimes with lower. And not too high.

        const int MAXITERATIONS = 10000; // A number high enough to get warmed up and reliable


        [Test]
        public async Task TaskPerformanceTest()
        {
            Task[] tasks = new Task[MAXTHREADS];
            List<long[]> timers = new List<long[]>();
            int fileByteLength = 0;

            var sw = new Stopwatch();
            sw.Reset();
            sw.Start();

            for (var i = 0; i < MAXTHREADS; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    var (tmp, measurements) = await CanPublishStaticFileContentWithThumbnails(i, MAXITERATIONS);
                    Debug.Assert(measurements.Length == MAXITERATIONS);
                    lock (timers)
                    {
                        fileByteLength = tmp;
                        timers.Add(measurements);
                    }
                });
            }

            try
            {
                Task.WaitAll(tasks);
            }
            catch (AggregateException ae)
            {
                foreach (var ex in ae.InnerExceptions)
                {
                    Console.WriteLine(ex.Message);
                }

                throw;
            }

            sw.Stop();

            Debug.Assert(timers.Count == MAXTHREADS);
            long[] oneDimensionalArray = timers.SelectMany(arr => arr).ToArray();
            Debug.Assert(oneDimensionalArray.Length == (MAXTHREADS * MAXITERATIONS));

            Array.Sort(oneDimensionalArray);
            for (var i = 1; i < MAXTHREADS * MAXITERATIONS; i++)
                Debug.Assert(oneDimensionalArray[i - 1] <= oneDimensionalArray[i]);

            Console.WriteLine($"Threads   : {MAXTHREADS}");
            Console.WriteLine($"Iterations: {MAXITERATIONS}");
            Console.WriteLine($"Time      : {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"Minimum   : {oneDimensionalArray[0]}ms");
            Console.WriteLine($"Maximum   : {oneDimensionalArray[MAXTHREADS * MAXITERATIONS - 1]}ms");
            Console.WriteLine($"Average   : {sw.ElapsedMilliseconds / (MAXTHREADS * MAXITERATIONS)}ms");
            Console.WriteLine($"Median    : {oneDimensionalArray[(MAXTHREADS * MAXITERATIONS) / 2]}ms");

            Console.WriteLine(
                $"Capacity  : {(1000 * MAXITERATIONS * MAXTHREADS) / Math.Max(1, sw.ElapsedMilliseconds)} / second");
            Console.WriteLine(
                $"Bandwidth : {fileByteLength * ((1000 * MAXITERATIONS * MAXTHREADS) / Math.Max(1, sw.ElapsedMilliseconds))} bytes / second");
        }

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

        // First make this test pass, then change it from a test to something else.
        //
        // [Test(Description = "publish static content to file, including payload and thumbnails")]
        public async Task<(int, long[])> CanPublishStaticFileContentWithThumbnails(int threadno, int iterations)
        {
            long[] timers = new long[iterations];
            Debug.Assert(timers.Length == iterations);
            var sw = new Stopwatch();
            int fileByteLength = 0;

            //
            // Here's the original test
            //
            var identity = TestIdentities.Frodo;
            var testContext = await _scaffold.OwnerApi.SetupTestSampleApp(identity);

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


            using (var client =
                   _scaffold.OwnerApi.CreateOwnerApiHttpClient(testContext.Identity, out var ownerSharedSecret))
            {
                var staticFileSvc =
                    RefitCreator.RestServiceFor<IStaticFileTestHttpClientForOwner>(client, ownerSharedSecret);

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
                if (!publishResponse.IsSuccessStatusCode)
                    Console.WriteLine("staticFileSvc.Publish(publishRequest): " + publishResponse.ReasonPhrase);

                Assert.True(publishResponse.IsSuccessStatusCode, publishResponse.ReasonPhrase);
                Assert.NotNull(publishResponse.Content);

                var pubResult = publishResponse.Content;

                Assert.AreEqual(pubResult.Filename, publishRequest.Filename);
                Assert.AreEqual(pubResult.SectionResults.Count, publishRequest.Sections.Count);
                Assert.AreEqual(pubResult.SectionResults[0].Name, publishRequest.Sections[0].Name);
                Assert.AreEqual(pubResult.SectionResults[0].FileCount, total_files_uploaded);


                var getStaticFileSvc = RestService.For<IStaticFileTestHttpClientForOwner>(client);

                //
                // I presume here we retrieve the file and download it
                //

                for (int count = 0; count < iterations; count++)
                {
                    sw.Restart();

                    // Do all the work here
                    var getFileResponse = await getStaticFileSvc.GetStaticFile(publishRequest.Filename);
                    if (!getFileResponse.IsSuccessStatusCode)
                        Console.WriteLine("GetStaticFile(): " + getFileResponse.ReasonPhrase);
                    Assert.True(getFileResponse.IsSuccessStatusCode, getFileResponse.ReasonPhrase);
                    Assert.IsNotNull(getFileResponse.Content);

                    Assert.IsTrue(getFileResponse.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
                    Assert.IsNotNull(values);
                    Assert.IsTrue(values.Single() == "*");

                    //TODO: open the file and check it against what was uploaded.  going to have to do some json acrobatics maybe?
                    var json = await getFileResponse.Content.ReadAsStringAsync();
                    fileByteLength = json.Length;
                    // Console.WriteLine(json);

                    var sectionOutputArray = DotYouSystemSerializer.Deserialize<SectionOutput[]>(json);

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
            }

            return (fileByteLength, timers);
        }


        public async Task CreateAnonymousUnEncryptedFile(TestAppContext testContext, int fileType, int dataType,
            string jsonContent, List<Guid> tags, byte[] payloadContent,
            ImageDataContent previewThumbnail,
            List<ImageDataContent> additionalThumbs)
        {
            using (var client =
                   _scaffold.OwnerApi.CreateOwnerApiHttpClient(testContext.Identity, out var ownerSharedSecret))
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
                    EncryptedKeyHeader =
                        EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref ownerSharedSecret),
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
                        AccessControlList = new AccessControlList()
                            { RequiredSecurityGroup = SecurityGroupType.Anonymous }
                    },
                };

                var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, transferIv, ref ownerSharedSecret);

                var additionalThumbnailContent = additionalThumbs?.Select(thumb =>
                    new StreamPart(new MemoryStream(thumb.Content), thumb.GetFilename(), thumb.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail))
                ).ToArray();

                var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
                var response = await driveSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json",
                        Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json",
                        Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadContent == null ? new MemoryStream() : new MemoryStream(payloadContent),
                        "payload.encrypted", "application/x-binary",
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
                var fileResponse = await getFilesDriveSvc.GetFileHeader(uploadedFile);

                Assert.That(fileResponse.IsSuccessStatusCode, Is.True);
                Assert.That(fileResponse.Content, Is.Not.Null);

                var clientFileHeader = fileResponse.Content;

                Assert.That(clientFileHeader.FileMetadata, Is.Not.Null);
                Assert.That(clientFileHeader.FileMetadata.AppData, Is.Not.Null);

                Assert.That(clientFileHeader.FileMetadata.ContentType, Is.EqualTo(descriptor.FileMetadata.ContentType));
                CollectionAssert.AreEquivalent(clientFileHeader.FileMetadata.AppData.Tags,
                    descriptor.FileMetadata.AppData.Tags);
                Assert.That(clientFileHeader.FileMetadata.AppData.JsonContent,
                    Is.EqualTo(descriptor.FileMetadata.AppData.JsonContent));
                Assert.That(clientFileHeader.FileMetadata.AppData.ContentIsComplete,
                    Is.EqualTo(descriptor.FileMetadata.AppData.ContentIsComplete));

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

                Assert.IsTrue(clientFileHeader.FileMetadata.AppData.AdditionalThumbnails.Count() ==
                              (additionalThumbs?.Count ?? 0));

                //
                // If payload was uploaded, get the payload that was uploaded, test it
                // 
                if (payloadContent != null)
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

                        Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnailInDescriptor.Content,
                            await thumbnailResponse!.Content!.ReadAsByteArrayAsync()));
                    }
                }

                keyHeader.AesKey.Wipe();
            }
        }
    }
}