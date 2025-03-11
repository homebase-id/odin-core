using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming;
using Odin.Services.Peer.Incoming.Drive;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Peer.Outgoing;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Hosting.Tests.AppAPI.Drive;
using Odin.Hosting.Tests.AppAPI.Transit;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.ApiClient;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Refit;

namespace Odin.Hosting.Tests.Performance
{
    public class TransitPerformanceTests
    {
        private const int FileType = 844;

        // For the performance test
        private static readonly int MAXTHREADS = 12;
        private const int MAXITERATIONS = 300;
        TestAppContext _frodoAppContext;
        TestAppContext _samAppContext;

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
         * 2023-03-12
         *
            TaskPerformanceTest_Transit
      Duration: 21.5 sec

            Standard Output:
                Threads   : 12
                Iterations: 300
                Time      : 18748ms
                Minimum   : 16ms
                Maximum   : 431ms
                Average   : 60ms
                Median    : 53ms
                Capacity  : 192 / second
                Bandwidth : 146000 bytes / second
                RSA Encryptions 3616, Decryptions 24
                RSA Keys Created 12, Keys Expired 0
                DB Opened 14, Closed 0

            TaskPerformanceTest_Transit
      Duration: 26.2 sec

              Standard Output:
                2023-05-06 Host [SEMIBEASTII]
                Threads   : 12
                Iterations: 300
                Wall Time : 23,659ms
                Minimum   : 16ms
                Maximum   : 711ms
                Average   : 75ms
                Median    : 63ms
                Capacity  : 152 / second
                RSA Encryptions 16, Decryptions 24
                RSA Keys Created 12, Keys Expired 0
                DB Opened 15, Closed 0
                Bandwidth : 115,000 bytes / second

        No change after DB cache
TaskPerformanceTest_Transit
  Duration: 23.9 sec

              Standard Output:
                2023-06-01 Host [SEMIBEASTII]
                Threads   : 12
                Iterations: 300
                Wall Time : 20,969ms
                Minimum   : 13ms
                Maximum   : 503ms
                Average   : 66ms
                Median    : 54ms
                Capacity  : 171 / second
                RSA Encryptions 16, Decryptions 24
                RSA Keys Created 12, Keys Expired 0
                DB Opened 15, Closed 0
                Bandwidth : 130,000 bytes / second
          */
        
        // SEB:NOTE
        // Temporary explicit because the call to PerformanceFramework.ThreadedTestAsync is not correctly
        // synchronized with the backend and will end the backend host too soon and will error because
        // it fails to release af CFM lock on a filestream (either as an error on THIS test, or
        // as an "inconclusive warning" on another test that runs after this one.
        [Test, Explicit] 
        public async Task TaskPerformanceTest_Transit()
        {
            TargetDrive targetDrive = TargetDrive.NewTargetDrive();

            //
            // Prepare environment by connecting identities
            //
            var scenarioCtx = await _scaffold.Scenarios.CreateConnectedHobbits(targetDrive);
            _frodoAppContext = scenarioCtx.AppContexts[TestIdentities.Frodo.OdinId];
            _samAppContext = scenarioCtx.AppContexts[TestIdentities.Samwise.OdinId];

            await PerformanceFramework.ThreadedTestAsync(MAXTHREADS, MAXITERATIONS, DoChat);
        }


        public async Task<(long, long[])> DoChat(int threadno, int iterations)
        {
            long fileByteLength = 0;
            long[] timers = new long[iterations];
            ClassicAssert.IsTrue(timers.Length == iterations);
            var sw = new Stopwatch();

            var randomHeaderContent =
                string.Join("", Enumerable.Range(250, 250).Select(i => Guid.NewGuid().ToString("N"))); // 250 bytes
            var randomPayloadContent =
                string.Join("", Enumerable.Range(512, 512).Select(i => Guid.NewGuid().ToString("N"))); // 512 bytes

            var recipients = new List<string>()
            {
                "sam.dotyou.cloud"
            };


            var ctx = _frodoAppContext;
            for (int count = 0; count < iterations; count++)
            {
                sw.Restart();
                var client = _scaffold.AppApi.CreateAppApiHttpClient(ctx);
                {
                    var sendMessageResult = await SendMessage(client, ctx, recipients, randomHeaderContent, randomPayloadContent);
                }
                /* var samMessageHeaders = await GetMessages(samAppContext);

                foreach (var msg in samMessageHeaders)
                {
                    fileByteLength += msg.FileMetadata.AppData.JsonContent.Length;
                    //TODO: the message data will be encrypted. Let me know if you want to decrypt but I dont think that's needed for perf testing
                    //Console.WriteLine(msg.FileMetadata.AppData.JsonContent);
                }*/

                fileByteLength += 250 + 512;

                // Finished doing all the work
                timers[count] = sw.ElapsedMilliseconds;
            }


            return (fileByteLength, timers);
        }


        private async Task<List<SharedSecretEncryptedFileHeader>> GetMessages(TestAppContext recipientAppContext)
        {
            var client = _scaffold.AppApi.CreateAppApiHttpClient(recipientAppContext);
            {
                //First force transfers to be put into their long term location
                var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(client);
                var resp = await transitAppSvc.ProcessInbox(new ProcessInboxRequest() { TargetDrive = recipientAppContext.TargetDrive });
                ClassicAssert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);

                var driveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, recipientAppContext.SharedSecret);

                var queryBatchResponse = await driveSvc.GetBatch(new QueryBatchRequest()
                {
                    QueryParams = new FileQueryParams()
                    {
                        TargetDrive = recipientAppContext.TargetDrive,
                        FileType = new List<int>() { FileType }
                    },
                    ResultOptionsRequest = new QueryBatchResultOptionsRequest()
                    {
                        MaxRecords = 100,
                        IncludeMetadataHeader = true
                    }
                });

                ClassicAssert.IsTrue(queryBatchResponse.IsSuccessStatusCode);
                ClassicAssert.IsNotNull(queryBatchResponse.Content);


                return queryBatchResponse.Content.SearchResults.ToList();
            }
        }

        private async Task<ExternalFileIdentifier> SendMessage(HttpClient client, TestAppContext senderAppContext,
            List<string> recipients, string message, string payload)
        {
            // var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(senderAppContext.Identity, out var ownerSharedSecret))
            // var client = _scaffold.AppApi.CreateAppApiHttpClient(senderAppContext))
            {
                var transferIv = ByteArrayUtil.GetRndByteArray(16);
                var keyHeader = KeyHeader.NewRandom16();

                var instructionSet = new UploadInstructionSet()
                {
                    TransferIv = transferIv,
                    StorageOptions = new StorageOptions()
                    {
                        Drive = senderAppContext.TargetDrive
                    },
                    //TODO: comment transit options if you only want to upload
                    TransitOptions = new TransitOptions()
                    {
                        Recipients = recipients
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

                var ss = senderAppContext.SharedSecret.ToSensitiveByteArray();
                var descriptor = new UploadFileDescriptor()
                {
                    EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref ss),
                    FileMetadata = new()
                    {
                        AllowDistribution = true,
                        IsEncrypted = true,
                        AppData = new()
                        {
                            Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                            FileType = FileType,
                            Content = OdinSystemSerializer.Serialize(new { content = message }),
                            PreviewThumbnail = new ThumbnailContent()
                            {
                                PixelHeight = 100,
                                PixelWidth = 100,
                                ContentType = "image/png",
                                Content = keyHeader.EncryptDataAes(TestMedia.PreviewPngThumbnailBytes)
                            }
                        },
                        AccessControlList = AccessControlList.Connected
                    },
                };

                var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref ss);

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
                    Thumbnails =( new []{thumbnail1, thumbnail2}).Select(t=>new UploadedManifestThumbnailDescriptor()
                    {
                        ThumbnailKey = t.GetFilename(WebScaffold.PAYLOAD_KEY),
                        PixelWidth = t.PixelWidth,
                        PixelHeight = t.PixelHeight
                    })
                });
                
                var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
                var instructionStream = new MemoryStream(bytes);
                
                var driveSvc = RestService.For<IDriveTestHttpClientForApps>(client);
                var response = await driveSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json",
                        Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json",
                        Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, WebScaffold.PAYLOAD_KEY, "application/x-binary",
                        Enum.GetName(MultipartUploadParts.Payload)),
                    new StreamPart(new MemoryStream(thumbnail1CipherBytes), thumbnail1.GetFilename(),
                        thumbnail1.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)),
                    new StreamPart(new MemoryStream(thumbnail2CipherBytes), thumbnail2.GetFilename(),
                        thumbnail2.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));

                ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Actual code was {response.StatusCode}");
                ClassicAssert.IsNotNull(response.Content);
                var uploadResult = response.Content;

                if (instructionSet.TransitOptions?.Recipients?.Any() ?? false)
                {
                    var wasPutInOutboxForAll =
                        instructionSet.TransitOptions.Recipients.All(r =>
                            uploadResult.RecipientStatus[r] == TransferStatus.Enqueued);

                    ClassicAssert.IsTrue(wasPutInOutboxForAll);
                }


                //TODO: since we added the indexer changes of batch commits, we need a way to test it is working and no files are lost
                return null;
                // if (response.StatusCode == HttpStatusCode.InternalServerError)
                // {
                //     Console.WriteLine("--- Error info");
                //     Console.WriteLine(response.Error.Content);
                // }
                //
                // Assert.That(response.Content, Is.Not.Null);
                // var uploadResult = response.Content;
                //
                // Assert.That(uploadResult.File, Is.Not.Null);
                // Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                // ClassicAssert.IsTrue(uploadResult.File.TargetDrive.IsValid());
                //
                // var uploadedFile = uploadResult.File;

                //
                // foreach (var recipient in recipients)
                // {
                //     ClassicAssert.IsTrue(uploadResult.RecipientStatus.ContainsKey(recipient), $"Message was not delivered to ${recipient}");
                //     ClassicAssert.IsTrue(uploadResult.RecipientStatus[recipient] == TransferStatus.Delivered, $"Message was not delivered to ${recipient}");
                // }
                //
                //
                //
                // //
                // // Begin testing that the file was correctly uploaded
                // //
                //
                // //
                // // Retrieve the file header that was uploaded; test it matches; 
                // //
                // var getFilesDriveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, ss);
                // var fileResponse = await getFilesDriveSvc.GetFileHeader(uploadedFile.FileId, uploadedFile.TargetDrive.Alias, uploadedFile.TargetDrive.Type);
                //
                // Assert.That(fileResponse.IsSuccessStatusCode, Is.True);
                // Assert.That(fileResponse.Content, Is.Not.Null);
                //
                // var clientFileHeader = fileResponse.Content;
                //
                // Assert.That(clientFileHeader.FileMetadata, Is.Not.Null);
                // Assert.That(clientFileHeader.FileMetadata.AppData, Is.Not.Null);
                //
                // 
                // CollectionAssert.AreEquivalent(clientFileHeader.FileMetadata.AppData.Tags, descriptor.FileMetadata.AppData.Tags);
                // Assert.That(clientFileHeader.FileMetadata.AppData.JsonContent, Is.EqualTo(descriptor.FileMetadata.AppData.JsonContent));
                // Assert.That(clientFileHeader.FileMetadata.AppData.ContentIsComplete, Is.EqualTo(descriptor.FileMetadata.AppData.ContentIsComplete));
                //
                // Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader, Is.Not.Null);
                // Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.Null);
                // Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv.Length, Is.GreaterThanOrEqualTo(16));
                // Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.EqualTo(Guid.Empty.ToByteArray()), "Iv was all zeros");
                // Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes));
                //
                // var decryptedKeyHeader = clientFileHeader.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ss);
                //
                // Assert.That(decryptedKeyHeader.AesKey.IsSet(), Is.True);
                // ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(decryptedKeyHeader.AesKey.GetKey(), keyHeader.AesKey.GetKey()));
                //
                // //validate preview thumbnail
                // ClassicAssert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.ContentType == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.ContentType);
                // ClassicAssert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.PixelHeight == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelHeight);
                // ClassicAssert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.PixelWidth == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelWidth);
                // ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(descriptor.FileMetadata.AppData.PreviewThumbnail.Content, clientFileHeader.FileMetadata.AppData.PreviewThumbnail.Content));
                //
                // ClassicAssert.IsTrue(clientFileHeader.FileMetadata.Thumbnails.Count() == 2);
                //
                //
                // //
                // // Get the payload that was uploaded, test it
                // // 
                //
                // var payloadResponse = await getFilesDriveSvc.GetPayload(uploadedFile.FileId, uploadedFile.TargetDrive.Alias, uploadedFile.TargetDrive.Type);
                // Assert.That(payloadResponse.IsSuccessStatusCode, Is.True);
                // Assert.That(payloadResponse.Content, Is.Not.Null);
                //
                // var payloadResponseCipher = await payloadResponse.Content.ReadAsByteArrayAsync();
                // Assert.That(((MemoryStream)payloadCipher).ToArray(), Is.EqualTo(payloadResponseCipher));
                //
                // var aesKey = decryptedKeyHeader.AesKey;
                // var decryptedPayloadBytes = Core.Cryptography.Crypto.AesCbc.Decrypt(
                //     cipherText: payloadResponseCipher,
                //     Key: ref aesKey,
                //     IV: decryptedKeyHeader.Iv);
                //
                // var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payload);
                // Assert.That(payloadBytes, Is.EqualTo(decryptedPayloadBytes));
                //
                // //
                // // Validate additional thumbnails
                // //
                //
                // var descriptorList = descriptor.FileMetadata.Thumbnails.ToList();
                // var clientFileHeaderList = clientFileHeader.FileMetadata.Thumbnails.ToList();
                //
                // //validate thumbnail 1
                // ClassicAssert.IsTrue(descriptorList[0].ContentType == clientFileHeaderList[0].ContentType);
                // ClassicAssert.IsTrue(descriptorList[0].PixelWidth == clientFileHeaderList[0].PixelWidth);
                // ClassicAssert.IsTrue(descriptorList[0].PixelHeight == clientFileHeaderList[0].PixelHeight);
                //
                // var thumbnailResponse1 = await getFilesDriveSvc.GetThumbnail(
                //     fileId: uploadedFile.FileId,
                //     alias: uploadedFile.TargetDrive.Alias,
                //     type: uploadedFile.TargetDrive.Type,
                //     thumbnail1.PixelHeight,
                //     thumbnail1.PixelWidth
                // );
                //
                // ClassicAssert.IsTrue(thumbnailResponse1.IsSuccessStatusCode);
                // ClassicAssert.IsNotNull(thumbnailResponse1.Content);
                //
                // ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnail1CipherBytes, await thumbnailResponse1!.Content!.ReadAsByteArrayAsync()));
                //
                // //validate thumbnail 2
                // ClassicAssert.IsTrue(descriptorList[1].ContentType == clientFileHeaderList[1].ContentType);
                // ClassicAssert.IsTrue(descriptorList[1].PixelWidth == clientFileHeaderList[1].PixelWidth);
                // ClassicAssert.IsTrue(descriptorList[1].PixelHeight == clientFileHeaderList[1].PixelHeight);
                //
                // var thumbnailResponse2 = await getFilesDriveSvc.GetThumbnail(
                //     fileId: uploadedFile.FileId,
                //     alias: uploadedFile.TargetDrive.Alias,
                //     type: uploadedFile.TargetDrive.Type,
                //     thumbnail2.PixelHeight,
                //     thumbnail2.PixelWidth);
                //
                // ClassicAssert.IsTrue(thumbnailResponse2.IsSuccessStatusCode);
                // ClassicAssert.IsNotNull(thumbnailResponse2.Content);
                // ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnail2CipherBytes, await thumbnailResponse2.Content!.ReadAsByteArrayAsync()));
                //
                // decryptedKeyHeader.AesKey.Wipe();
                // keyHeader.AesKey.Wipe();

                //
                // End testing that the file was correctly uploaded
                //

                // return uploadedFile;
            }
        }
        
    }
}