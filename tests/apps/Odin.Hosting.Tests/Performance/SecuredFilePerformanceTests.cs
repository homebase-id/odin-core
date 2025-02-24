using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Hosting.Tests.AppAPI.Drive;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.ApiClient;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Refit;

namespace Odin.Hosting.Tests.Performance
{
    public class SecuredFilePerformanceTests
    {
        // For the performance test
        private const int MAXTHREADS = 12; // Should be at least 2 * your CPU cores. Can still be nice to test sometimes with lower. And not too high.
        const int MAXITERATIONS = 50; // A number high enough to get warmed up and reliable
        IDriveTestHttpClientForApps frodoDriveService;
        ExternalFileIdentifier uploadedFile1;

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


        /*
    TaskPerformanceTest_SecuredFiles
      Duration: 17.4 sec

              Standard Output:
                2023-05-06 Host [SEMIBEASTII]
                Threads   : 12
                Iterations: 5,000
                Wall Time : 16,722ms
                Minimum   : 1ms
                Maximum   : 33ms
                Average   : 2ms
                Median    : 3ms
                Capacity  : 3,588 / second
                RSA Encryptions 1, Decryptions 9
                RSA Keys Created 5, Keys Expired 0
                DB Opened 12, Closed 0
                Bandwidth : 283,440,000 bytes / second

            No change after database caching
         */
        [Test]
        public async Task TaskPerformanceTest_SecuredFiles()
        {
            //
            // Prepare environment by uploading secured files
            //
            var frodoAppContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(TestIdentities.Frodo);
            var randomHeaderContent = string.Join("", Enumerable.Range(10, 10).Select(i => Guid.NewGuid().ToString("N"))); // 32 * 10 = 320 bytes
            var randomPayloadContent =
                string.Join("",
                    Enumerable.Range(2468, 2468).Select(i => Guid.NewGuid().ToString("N"))); // 32 * 2468 = 78,976 bytes, almost same size as public test
            uploadedFile1 =
                await UploadFileWithPayloadAndTwoThumbnails(frodoAppContext, randomHeaderContent, randomPayloadContent, AccessControlList.Connected);

            // Note to Michael: your GET requests will be done using the App API instead of YouAuth.
            // This is because I've not yet created a login scaffold for YouAuth in our test framework
            // The code paths for youAuth and app are identical (mostly) after authentication has
            // occured (and we cache the token) so this should be sufficient.

            // The primary difference is that app always runs as Owner, however the permission
            // context is still based on what the app has access to

            var frodoHttpClient = _scaffold.AppApi.CreateAppApiHttpClient(frodoAppContext);
            frodoDriveService = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(frodoHttpClient, frodoAppContext.SharedSecret);

            await PerformanceFramework.ThreadedTestAsync(MAXTHREADS, MAXITERATIONS, GetSecuredFile);
        }


        public async Task<(long, long[])> GetSecuredFile(int threadno, int iterations)
        {
            //
            // Calls to get the secured file parts
            // 

            long[] timers = new long[iterations];
            Debug.Assert(timers.Length == iterations);
            var sw = new Stopwatch();
            int fileByteLength = 0;


            // var headerResponse = await frodoDriveService.GetFileHeader(uploadedFile1.FileId, uploadedFile1.TargetDrive.Alias, uploadedFile1.TargetDrive.Type);
            var headerResponse = await frodoDriveService.GetFileHeaderAsPost(uploadedFile1);
            ClassicAssert.IsTrue(headerResponse.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(headerResponse.Content);
            // fileByteLength += (int)headerResponse.Content. .ToString().Length; -- help
            fileByteLength += 320;

            var thumbnail1 = headerResponse.Content.FileMetadata.GetPayloadDescriptor(WebScaffold.PAYLOAD_KEY).Thumbnails.FirstOrDefault();
            // var thumbnail1Response = await frodoDriveService.GetThumbnail(uploadedFile1.FileId, uploadedFile1.TargetDrive.Alias, uploadedFile1.TargetDrive.Type, thumbnail1.PixelWidth, thumbnail1.PixelWidth);
            var thumbnail1Response = await frodoDriveService.GetThumbnailAsPost(new GetThumbnailRequest()
            {
                File = uploadedFile1,
                Width = thumbnail1.PixelWidth,
                Height = thumbnail1.PixelHeight,
                PayloadKey = WebScaffold.PAYLOAD_KEY
            });
            ClassicAssert.IsTrue(thumbnail1Response.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(thumbnail1Response.Content);
            var encryptedThumbnailBytes = await thumbnail1Response.Content.ReadAsByteArrayAsync();
            fileByteLength += encryptedThumbnailBytes.Length;


            // var payload1Response2 = await frodoDriveService.GetPayload(uploadedFile1.FileId, uploadedFile1.TargetDrive.Alias, uploadedFile1.TargetDrive.Type);
            var payload1Response2 = await frodoDriveService.GetPayloadAsPost(new GetPayloadRequest() { File = uploadedFile1, Key = WebScaffold.PAYLOAD_KEY });
            ClassicAssert.IsTrue(payload1Response2.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(payload1Response2.Content);
            // System.Threading.Thread.Sleep(2000);

            //
            // I presume here we retrieve the file and download it
            //
            for (int count = 0; count < iterations; count++)
            {
                sw.Restart();

                // var payload1Response = await frodoDriveService.GetPayload(uploadedFile1.FileId, uploadedFile1.TargetDrive.Alias, uploadedFile1.TargetDrive.Type);
                var payload1Response =
                    await frodoDriveService.GetPayloadAsPost(new GetPayloadRequest() { File = uploadedFile1, Key = WebScaffold.PAYLOAD_KEY });
                // var contentType = payloadResponse.Headers.SingleOrDefault(h => h.Key == HttpHeaderConstants.DecryptedContentType);
                ClassicAssert.IsTrue(payload1Response.IsSuccessStatusCode);
                ClassicAssert.IsNotNull(payload1Response.Content);
                var encryptedPayloadBytes = await payload1Response.Content.ReadAsByteArrayAsync();
                fileByteLength += encryptedPayloadBytes.Length;


                // Finished doing all the work
                timers[count] = sw.ElapsedMilliseconds;

                //
                // If you want to introduce a delay be sure to use: await Task.Delay(1);
                // Take.Delay() is very inaccurate.
            }

            return (fileByteLength, timers);
        }


        private async Task<ExternalFileIdentifier> UploadFileWithPayloadAndTwoThumbnails(TestAppContext testAppContext, string jsonContent, string payload,
            AccessControlList acl)
        {
            var identity = TestIdentities.Frodo;

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var transferIv = ByteArrayUtil.GetRndByteArray(16);
                var keyHeader = KeyHeader.NewRandom16();

                var instructionSet = new UploadInstructionSet()
                {
                    TransferIv = transferIv,
                    StorageOptions = new StorageOptions()
                    {
                        Drive = testAppContext.TargetDrive
                    }
                };


                var thumbnail1 = new ThumbnailDescriptor()
                {
                    PixelHeight = 300,
                    PixelWidth = 300,
                    ContentType = "image/jpeg"
                };
                var thumbnail1CipherBytes = keyHeader.EncryptDataAes(TestMedia.ThumbnailBytes300);

                var thumbnail2 = new ThumbnailDescriptor()
                {
                    PixelHeight = 400,
                    PixelWidth = 400,
                    ContentType = "image/jpeg",
                };
                var thumbnail2CipherBytes = keyHeader.EncryptDataAes(TestMedia.ThumbnailBytes400);

                var descriptor = new UploadFileDescriptor()
                {
                    EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref ownerSharedSecret),
                    FileMetadata = new()
                    {
                        AllowDistribution = false,
                        IsEncrypted = true,
                        AppData = new()
                        {
                            Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                            Content = OdinSystemSerializer.Serialize(new { content = jsonContent }),
                            PreviewThumbnail = new ThumbnailContent()
                            {
                                PixelHeight = 100,
                                PixelWidth = 100,
                                ContentType = "image/png",
                                Content = keyHeader.EncryptDataAes(TestMedia.PreviewPngThumbnailBytes)
                            }
                        },
                        AccessControlList = acl
                    },
                };

                var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref ownerSharedSecret);

                var payloadIv = ByteArrayUtil.GetRndByteArray(16);
                var payloadKeyHeader = new KeyHeader()
                {
                    Iv = payloadIv,
                    AesKey = keyHeader.AesKey
                };
                var payloadCipher = payloadKeyHeader.EncryptDataAesAsStream(payload);

                instructionSet.Manifest.PayloadDescriptors.Add(new UploadManifestPayloadDescriptor()
                {
                    Iv = payloadIv,
                    PayloadKey = WebScaffold.PAYLOAD_KEY,
                    Thumbnails = (new[] { thumbnail1, thumbnail2 }).Select(thumb => new UploadedManifestThumbnailDescriptor()
                    {
                        ThumbnailKey = thumb.GetFilename(WebScaffold.PAYLOAD_KEY),
                        PixelHeight = thumb.PixelHeight,
                        PixelWidth = thumb.PixelWidth
                    })
                });

                var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
                var instructionStream = new MemoryStream(bytes);

                var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
                var response = await driveSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, WebScaffold.PAYLOAD_KEY, "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)),
                    new StreamPart(new MemoryStream(thumbnail1CipherBytes), thumbnail1.GetFilename(), thumbnail1.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail)),
                    new StreamPart(new MemoryStream(thumbnail2CipherBytes), thumbnail2.GetFilename(), thumbnail2.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail)));

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var uploadResult = response.Content;

                Assert.That(uploadResult.File, Is.Not.Null);
                Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                ClassicAssert.IsTrue(uploadResult.File.TargetDrive.IsValid());

                Assert.That(uploadResult.RecipientStatus, Is.Null);
                var uploadedFile = uploadResult.File;


                //
                // Begin testing that the file was correctly uploaded
                //

                //
                // Retrieve the file header that was uploaded; test it matches; 
                //
                var getFilesDriveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, ownerSharedSecret);
                var fileResponse = await getFilesDriveSvc.GetFileHeaderAsPost(uploadedFile);

                Assert.That(fileResponse.IsSuccessStatusCode, Is.True);
                Assert.That(fileResponse.Content, Is.Not.Null);

                var clientFileHeader = fileResponse.Content;

                Assert.That(clientFileHeader.FileMetadata, Is.Not.Null);
                Assert.That(clientFileHeader.FileMetadata.AppData, Is.Not.Null);

                CollectionAssert.AreEquivalent(clientFileHeader.FileMetadata.AppData.Tags, descriptor.FileMetadata.AppData.Tags);
                Assert.That(clientFileHeader.FileMetadata.AppData.Content, Is.EqualTo(descriptor.FileMetadata.AppData.Content));
                ClassicAssert.IsTrue(clientFileHeader.FileMetadata.Payloads.Count == 1);

                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader, Is.Not.Null);
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.Null);
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv.Length, Is.GreaterThanOrEqualTo(16));
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.EqualTo(Guid.Empty.ToByteArray()), "Iv was all zeros");
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes));

                var decryptedKeyHeader = clientFileHeader.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ownerSharedSecret);

                Assert.That(decryptedKeyHeader.AesKey.IsSet(), Is.True);
                ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(decryptedKeyHeader.AesKey.GetKey(), keyHeader.AesKey.GetKey()));

                //validate preview thumbnail
                ClassicAssert.IsTrue(
                    descriptor.FileMetadata.AppData.PreviewThumbnail.ContentType == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.ContentType);
                ClassicAssert.IsTrue(
                    descriptor.FileMetadata.AppData.PreviewThumbnail.PixelHeight == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelHeight);
                ClassicAssert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.PixelWidth == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelWidth);
                ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(descriptor.FileMetadata.AppData.PreviewThumbnail.Content,
                    clientFileHeader.FileMetadata.AppData.PreviewThumbnail.Content));

                ClassicAssert.IsTrue(clientFileHeader.FileMetadata.GetPayloadDescriptor(WebScaffold.PAYLOAD_KEY).Thumbnails.Count() == 2);


                //
                // Get the payload that was uploaded, test it
                // 

                var payloadResponse = await getFilesDriveSvc.GetPayloadPost(new GetPayloadRequest() { File = uploadedFile, Key = WebScaffold.PAYLOAD_KEY });
                Assert.That(payloadResponse.IsSuccessStatusCode, Is.True);
                Assert.That(payloadResponse.Content, Is.Not.Null);

                var payloadResponseCipher = await payloadResponse.Content.ReadAsByteArrayAsync();
                Assert.That(((MemoryStream)payloadCipher).ToArray(), Is.EqualTo(payloadResponseCipher));

                var aesKey = decryptedKeyHeader.AesKey;
                var decryptedPayloadBytes = AesCbc.Decrypt(
                    cipherText: payloadResponseCipher,
                    key: aesKey,
                    iv: payloadIv);

                var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payload);
                Assert.That(payloadBytes, Is.EqualTo(decryptedPayloadBytes));

                //
                // Validate additional thumbnails
                //

                var descriptorList = new List<ThumbnailDescriptor>() { thumbnail1, thumbnail2 };
                var clientFileHeaderList = clientFileHeader.FileMetadata.GetPayloadDescriptor(WebScaffold.PAYLOAD_KEY).Thumbnails.ToList();

                //validate thumbnail 1
                ClassicAssert.IsTrue(descriptorList[0].ContentType == clientFileHeaderList[0].ContentType);
                ClassicAssert.IsTrue(descriptorList[0].PixelWidth == clientFileHeaderList[0].PixelWidth);
                ClassicAssert.IsTrue(descriptorList[0].PixelHeight == clientFileHeaderList[0].PixelHeight);

                var thumbnailResponse1 = await getFilesDriveSvc.GetThumbnailPost(new GetThumbnailRequest()
                {
                    File = uploadedFile,
                    Height = thumbnail1.PixelHeight,
                    Width = thumbnail1.PixelWidth,
                    PayloadKey = WebScaffold.PAYLOAD_KEY
                });

                ClassicAssert.IsTrue(thumbnailResponse1.IsSuccessStatusCode);
                ClassicAssert.IsNotNull(thumbnailResponse1.Content);

                ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnail1CipherBytes, await thumbnailResponse1!.Content!.ReadAsByteArrayAsync()));

                //validate thumbnail 2
                ClassicAssert.IsTrue(descriptorList[1].ContentType == clientFileHeaderList[1].ContentType);
                ClassicAssert.IsTrue(descriptorList[1].PixelWidth == clientFileHeaderList[1].PixelWidth);
                ClassicAssert.IsTrue(descriptorList[1].PixelHeight == clientFileHeaderList[1].PixelHeight);

                var thumbnailResponse2 = await getFilesDriveSvc.GetThumbnailPost(new GetThumbnailRequest()
                {
                    File = uploadedFile,
                    Height = thumbnail2.PixelHeight,
                    Width = thumbnail2.PixelWidth,
                    PayloadKey = WebScaffold.PAYLOAD_KEY
                });

                ClassicAssert.IsTrue(thumbnailResponse2.IsSuccessStatusCode);
                ClassicAssert.IsNotNull(thumbnailResponse2.Content);
                ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnail2CipherBytes, await thumbnailResponse2.Content!.ReadAsByteArrayAsync()));

                decryptedKeyHeader.AesKey.Wipe();
                keyHeader.AesKey.Wipe();
                ownerSharedSecret.Wipe();


                //
                // End testing that the file was correctly uploaded
                //

                return uploadedFile;
            }
        }
    }
}