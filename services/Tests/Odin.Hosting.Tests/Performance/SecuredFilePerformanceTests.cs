﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Encryption;
using Odin.Hosting.Tests.AppAPI.Drive;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.ApiClient;
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
            string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
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

            PerformanceFramework.ThreadedTest(MAXTHREADS, MAXITERATIONS, GetSecuredFile);
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
            Assert.IsTrue(headerResponse.IsSuccessStatusCode);
            Assert.IsNotNull(headerResponse.Content);
            // fileByteLength += (int)headerResponse.Content. .ToString().Length; -- help
            fileByteLength += 320;

            var thumbnail1 = headerResponse.Content.FileMetadata.AppData.AdditionalThumbnails.FirstOrDefault();
            // var thumbnail1Response = await frodoDriveService.GetThumbnail(uploadedFile1.FileId, uploadedFile1.TargetDrive.Alias, uploadedFile1.TargetDrive.Type, thumbnail1.PixelWidth, thumbnail1.PixelWidth);
            var thumbnail1Response = await frodoDriveService.GetThumbnailAsPost(new GetThumbnailRequest()
            {
                File = uploadedFile1,
                Width = thumbnail1.PixelWidth,
                Height = thumbnail1.PixelHeight
            });
            Assert.IsTrue(thumbnail1Response.IsSuccessStatusCode);
            Assert.IsNotNull(thumbnail1Response.Content);
            var encryptedThumbnailBytes = await thumbnail1Response.Content.ReadAsByteArrayAsync();
            fileByteLength += encryptedThumbnailBytes.Length;


            // var payload1Response2 = await frodoDriveService.GetPayload(uploadedFile1.FileId, uploadedFile1.TargetDrive.Alias, uploadedFile1.TargetDrive.Type);
            var payload1Response2 = await frodoDriveService.GetPayloadAsPost(new GetPayloadRequest() { File = uploadedFile1, Key = WebScaffold.PAYLOAD_KEY});
            Assert.IsTrue(payload1Response2.IsSuccessStatusCode);
            Assert.IsNotNull(payload1Response2.Content);
            // System.Threading.Thread.Sleep(2000);

            //
            // I presume here we retrieve the file and download it
            //
            for (int count = 0; count < iterations; count++)
            {
                sw.Restart();

                // var payload1Response = await frodoDriveService.GetPayload(uploadedFile1.FileId, uploadedFile1.TargetDrive.Alias, uploadedFile1.TargetDrive.Type);
                var payload1Response = await frodoDriveService.GetPayloadAsPost(new GetPayloadRequest() { File = uploadedFile1, Key = WebScaffold.PAYLOAD_KEY });
                // var contentType = payloadResponse.Headers.SingleOrDefault(h => h.Key == HttpHeaderConstants.DecryptedContentType);
                Assert.IsTrue(payload1Response.IsSuccessStatusCode);
                Assert.IsNotNull(payload1Response.Content);
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

                var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
                var instructionStream = new MemoryStream(bytes);

                var thumbnail1 = new ImageDataHeader()
                {
                    PixelHeight = 300,
                    PixelWidth = 300,
                    ContentType = "image/jpeg"
                };
                var thumbnail1CipherBytes = keyHeader.EncryptDataAes(TestMedia.ThumbnailBytes300);

                var thumbnail2 = new ImageDataHeader()
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
                        ContentType = "application/json",
                        AllowDistribution = false,
                        PayloadIsEncrypted = true,
                        AppData = new()
                        {
                            Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                            ContentIsComplete = false,
                            JsonContent = OdinSystemSerializer.Serialize(new { content = jsonContent }),
                            PreviewThumbnail = new ImageDataContent()
                            {
                                PixelHeight = 100,
                                PixelWidth = 100,
                                ContentType = "image/png",
                                Content = keyHeader.EncryptDataAes(TestMedia.PreviewPngThumbnailBytes)
                            },

                            AdditionalThumbnails = new[] { thumbnail1, thumbnail2 }
                        },
                        AccessControlList = acl
                    },
                };

                var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref ownerSharedSecret);

                var payloadCipher = keyHeader.EncryptDataAesAsStream(payload);

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
                Assert.IsTrue(uploadResult.File.TargetDrive.IsValid());

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

                Assert.That(clientFileHeader.FileMetadata.ContentType, Is.EqualTo(descriptor.FileMetadata.ContentType));
                CollectionAssert.AreEquivalent(clientFileHeader.FileMetadata.AppData.Tags, descriptor.FileMetadata.AppData.Tags);
                Assert.That(clientFileHeader.FileMetadata.AppData.JsonContent, Is.EqualTo(descriptor.FileMetadata.AppData.JsonContent));
                Assert.That(clientFileHeader.FileMetadata.AppData.ContentIsComplete, Is.EqualTo(descriptor.FileMetadata.AppData.ContentIsComplete));

                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader, Is.Not.Null);
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.Null);
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv.Length, Is.GreaterThanOrEqualTo(16));
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.EqualTo(Guid.Empty.ToByteArray()), "Iv was all zeros");
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes));

                var decryptedKeyHeader = clientFileHeader.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ownerSharedSecret);

                Assert.That(decryptedKeyHeader.AesKey.IsSet(), Is.True);
                Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(decryptedKeyHeader.AesKey.GetKey(), keyHeader.AesKey.GetKey()));

                //validate preview thumbnail
                Assert.IsTrue(
                    descriptor.FileMetadata.AppData.PreviewThumbnail.ContentType == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.ContentType);
                Assert.IsTrue(
                    descriptor.FileMetadata.AppData.PreviewThumbnail.PixelHeight == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelHeight);
                Assert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.PixelWidth == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelWidth);
                Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(descriptor.FileMetadata.AppData.PreviewThumbnail.Content,
                    clientFileHeader.FileMetadata.AppData.PreviewThumbnail.Content));

                Assert.IsTrue(clientFileHeader.FileMetadata.AppData.AdditionalThumbnails.Count() == 2);


                //
                // Get the payload that was uploaded, test it
                // 

                var payloadResponse = await getFilesDriveSvc.GetPayloadPost(new GetPayloadRequest() {File = uploadedFile, Key = WebScaffold.PAYLOAD_KEY });
                Assert.That(payloadResponse.IsSuccessStatusCode, Is.True);
                Assert.That(payloadResponse.Content, Is.Not.Null);

                var payloadResponseCipher = await payloadResponse.Content.ReadAsByteArrayAsync();
                Assert.That(((MemoryStream)payloadCipher).ToArray(), Is.EqualTo(payloadResponseCipher));

                var aesKey = decryptedKeyHeader.AesKey;
                var decryptedPayloadBytes = AesCbc.Decrypt(
                    cipherText: payloadResponseCipher,
                    Key: ref aesKey,
                    IV: decryptedKeyHeader.Iv);

                var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payload);
                Assert.That(payloadBytes, Is.EqualTo(decryptedPayloadBytes));

                //
                // Validate additional thumbnails
                //

                var descriptorList = descriptor.FileMetadata.AppData.AdditionalThumbnails.ToList();
                var clientFileHeaderList = clientFileHeader.FileMetadata.AppData.AdditionalThumbnails.ToList();

                //validate thumbnail 1
                Assert.IsTrue(descriptorList[0].ContentType == clientFileHeaderList[0].ContentType);
                Assert.IsTrue(descriptorList[0].PixelWidth == clientFileHeaderList[0].PixelWidth);
                Assert.IsTrue(descriptorList[0].PixelHeight == clientFileHeaderList[0].PixelHeight);

                var thumbnailResponse1 = await getFilesDriveSvc.GetThumbnailPost(new GetThumbnailRequest()
                {
                    File = uploadedFile,
                    Height = thumbnail1.PixelHeight,
                    Width = thumbnail1.PixelWidth
                });

                Assert.IsTrue(thumbnailResponse1.IsSuccessStatusCode);
                Assert.IsNotNull(thumbnailResponse1.Content);

                Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnail1CipherBytes, await thumbnailResponse1!.Content!.ReadAsByteArrayAsync()));

                //validate thumbnail 2
                Assert.IsTrue(descriptorList[1].ContentType == clientFileHeaderList[1].ContentType);
                Assert.IsTrue(descriptorList[1].PixelWidth == clientFileHeaderList[1].PixelWidth);
                Assert.IsTrue(descriptorList[1].PixelHeight == clientFileHeaderList[1].PixelHeight);

                var thumbnailResponse2 = await getFilesDriveSvc.GetThumbnailPost(new GetThumbnailRequest()
                {
                    File = uploadedFile,
                    Height = thumbnail2.PixelHeight,
                    Width = thumbnail2.PixelWidth
                });

                Assert.IsTrue(thumbnailResponse2.IsSuccessStatusCode);
                Assert.IsNotNull(thumbnailResponse2.Content);
                Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnail2CipherBytes, await thumbnailResponse2.Content!.ReadAsByteArrayAsync()));

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