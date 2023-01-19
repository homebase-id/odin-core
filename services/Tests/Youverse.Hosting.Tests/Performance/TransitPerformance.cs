using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Policy;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using SQLitePCL;
using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Optimization.Cdn;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers.ClientToken.Transit;
using Youverse.Hosting.Controllers.OwnerToken.Cdn;
using Youverse.Hosting.Tests.AppAPI;
using Youverse.Hosting.Tests.AppAPI.Drive;
using Youverse.Hosting.Tests.AppAPI.Transit;
using Youverse.Hosting.Tests.OwnerApi.Drive;
using Youverse.Hosting.Tests.OwnerApi.Optimization.Cdn;

namespace Youverse.Hosting.Tests.Performance
{
    public class TransitPerformanceTests
    {
        private const int FileType = 844;

        // For the performance test
        private static readonly int MAXTHREADS = 16; // Should be at least 2 * your CPU cores. Can still be nice to test sometimes with lower. And not too high.
        private const int MAXITERATIONS = 10; // A number high enough to get warmed up and reliable

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

        [Test]
        public async Task TaskPerformanceTest_Transit()
        {
            Task[] tasks = new Task[MAXTHREADS];
            List<long[]> timers = new List<long[]>();
            long fileByteLength = 0;


            TargetDrive targetDrive = TargetDrive.NewTargetDrive();

            //
            // Prepare environment by connecting identities
            //
            var scenarioCtx = await _scaffold.Scenarios.CreateConnectedHobbits(targetDrive);
            var frodoAppContext = scenarioCtx.AppContexts[TestIdentities.Frodo.DotYouId];
            var samAppContext = scenarioCtx.AppContexts[TestIdentities.Samwise.DotYouId];

            //
            // Now back to performance testing
            //
            var sw = new Stopwatch();
            sw.Reset();
            sw.Start();

            for (var i = 0; i < MAXTHREADS; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    var (tmp, measurements) = await DoChat(i, MAXITERATIONS, frodoAppContext, samAppContext);
                    Debug.Assert(measurements.Length == MAXITERATIONS);
                    lock (timers)
                    {
                        fileByteLength += tmp;
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
            Console.WriteLine($"Average   : {oneDimensionalArray.Sum() / (MAXTHREADS * MAXITERATIONS)}ms");
            Console.WriteLine($"Median    : {oneDimensionalArray[(MAXTHREADS * MAXITERATIONS) / 2]}ms");

            Console.WriteLine(
                $"Capacity  : {(1000 * MAXITERATIONS * MAXTHREADS) / Math.Max(1, sw.ElapsedMilliseconds)} / second");
            Console.WriteLine(
                $"Bandwidth : {1000 * (fileByteLength / Math.Max(1, sw.ElapsedMilliseconds))} bytes / second");
        }



        public async Task<(long, long[])> DoChat(int threadno, int iterations, TestAppContext frodoAppContext, TestAppContext samAppContext)
        {
            long fileByteLength = 0;
            long[] timers = new long[iterations];
            Debug.Assert(timers.Length == iterations);
            var sw = new Stopwatch();

            var randomHeaderContent =
                string.Join("", Enumerable.Range(250, 250).Select(i => Guid.NewGuid().ToString("N"))); // 250 bytes
            var randomPayloadContent =
                string.Join("", Enumerable.Range(512, 512).Select(i => Guid.NewGuid().ToString("N"))); // 512 bytes

            var recipients = new List<string>()
            {
                // samAppContext.Identity.ToString()
                "samwise.digital"
            };


            var ctx = frodoAppContext;
            for (int count = 0; count < iterations; count++)
            {
                sw.Restart();
                using (var client = _scaffold.AppApi.CreateAppApiHttpClient(ctx))
                // using (var client = CreateClient(ctx.Identity, ctx.ClientAuthenticationToken, ctx.SharedSecret))
                {
                    var sendMessageResult = await SendMessage(client, ctx, recipients, randomHeaderContent,
                        randomPayloadContent);
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




        private async Task<List<ClientFileHeader>> GetMessages(TestAppContext recipientAppContext)
        {
            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(recipientAppContext))
            {
                //First force transfers to be put into their long term location
                var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(client);
                var resp = await transitAppSvc.ProcessIncomingInstructions(new ProcessTransitInstructionRequest() { TargetDrive = recipientAppContext.TargetDrive });
                Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);

                var driveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, recipientAppContext.SharedSecret);

                var queryBatchResponse = await driveSvc.QueryBatch(new QueryBatchRequest()
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

                Assert.IsTrue(queryBatchResponse.IsSuccessStatusCode);
                Assert.IsNotNull(queryBatchResponse.Content);

                
                return queryBatchResponse.Content.SearchResults.ToList();
            }
        }

        private async Task<ExternalFileIdentifier> SendMessage(HttpClient client, TestAppContext senderAppContext,
            List<string> recipients, string message, string payload)
        {
            // using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(senderAppContext.Identity, out var ownerSharedSecret))
            // using (var client = _scaffold.AppApi.CreateAppApiHttpClient(senderAppContext))
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
                        UseGlobalTransitId = true,
                        Recipients = recipients,
                        Schedule = ScheduleOptions.SendNowAwaitResponse
                    }
                };

                var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
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

                var ss = senderAppContext.SharedSecret.ToSensitiveByteArray();
                var descriptor = new UploadFileDescriptor()
                {
                    EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref ss),
                    FileMetadata = new()
                    {
                        ContentType = "application/json",
                        PayloadIsEncrypted = true,
                        AppData = new()
                        {
                            Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                            FileType = FileType,
                            ContentIsComplete = false,
                            JsonContent = DotYouSystemSerializer.Serialize(new { content = message }),
                            PreviewThumbnail = new ImageDataContent()
                            {
                                PixelHeight = 100,
                                PixelWidth = 100,
                                ContentType = "image/png",
                                Content = keyHeader.EncryptDataAes(TestMedia.PreviewPngThumbnailBytes)
                            },

                            AdditionalThumbnails = new[] { thumbnail1, thumbnail2 }
                        },
                        AccessControlList = AccessControlList.OwnerOnly
                    },
                };

                var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, transferIv, ref ss);

                var payloadCipher = keyHeader.EncryptDataAesAsStream(payload);

                var driveSvc = RestService.For<IDriveTestHttpClientForApps>(client);
                var response = await driveSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json",
                        Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json",
                        Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary",
                        Enum.GetName(MultipartUploadParts.Payload)),
                    new StreamPart(new MemoryStream(thumbnail1CipherBytes), thumbnail1.GetFilename(),
                        thumbnail1.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)),
                    new StreamPart(new MemoryStream(thumbnail2CipherBytes), thumbnail2.GetFilename(),
                        thumbnail2.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));

                Assert.IsTrue(response.IsSuccessStatusCode, $"Actual code was {response.StatusCode}");
                Assert.IsNotNull(response.Content);
                var uploadResult = response.Content;
                
                if(instructionSet.TransitOptions?.Recipients?.Any() ?? false)
                {
                    var wasDeliveredToAll =
                        instructionSet.TransitOptions.Recipients.All(r =>
                            uploadResult.RecipientStatus[r] == TransferStatus.Delivered);

                    Assert.IsTrue(wasDeliveredToAll);
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
                // Assert.IsTrue(uploadResult.File.TargetDrive.IsValid());
                //
                // var uploadedFile = uploadResult.File;

                //
                // foreach (var recipient in recipients)
                // {
                //     Assert.IsTrue(uploadResult.RecipientStatus.ContainsKey(recipient), $"Message was not delivered to ${recipient}");
                //     Assert.IsTrue(uploadResult.RecipientStatus[recipient] == TransferStatus.Delivered, $"Message was not delivered to ${recipient}");
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
                // Assert.That(clientFileHeader.FileMetadata.ContentType, Is.EqualTo(descriptor.FileMetadata.ContentType));
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
                // Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(decryptedKeyHeader.AesKey.GetKey(), keyHeader.AesKey.GetKey()));
                //
                // //validate preview thumbnail
                // Assert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.ContentType == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.ContentType);
                // Assert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.PixelHeight == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelHeight);
                // Assert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.PixelWidth == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelWidth);
                // Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(descriptor.FileMetadata.AppData.PreviewThumbnail.Content, clientFileHeader.FileMetadata.AppData.PreviewThumbnail.Content));
                //
                // Assert.IsTrue(clientFileHeader.FileMetadata.AppData.AdditionalThumbnails.Count() == 2);
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
                // var descriptorList = descriptor.FileMetadata.AppData.AdditionalThumbnails.ToList();
                // var clientFileHeaderList = clientFileHeader.FileMetadata.AppData.AdditionalThumbnails.ToList();
                //
                // //validate thumbnail 1
                // Assert.IsTrue(descriptorList[0].ContentType == clientFileHeaderList[0].ContentType);
                // Assert.IsTrue(descriptorList[0].PixelWidth == clientFileHeaderList[0].PixelWidth);
                // Assert.IsTrue(descriptorList[0].PixelHeight == clientFileHeaderList[0].PixelHeight);
                //
                // var thumbnailResponse1 = await getFilesDriveSvc.GetThumbnail(
                //     fileId: uploadedFile.FileId,
                //     alias: uploadedFile.TargetDrive.Alias,
                //     type: uploadedFile.TargetDrive.Type,
                //     thumbnail1.PixelHeight,
                //     thumbnail1.PixelWidth
                // );
                //
                // Assert.IsTrue(thumbnailResponse1.IsSuccessStatusCode);
                // Assert.IsNotNull(thumbnailResponse1.Content);
                //
                // Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnail1CipherBytes, await thumbnailResponse1!.Content!.ReadAsByteArrayAsync()));
                //
                // //validate thumbnail 2
                // Assert.IsTrue(descriptorList[1].ContentType == clientFileHeaderList[1].ContentType);
                // Assert.IsTrue(descriptorList[1].PixelWidth == clientFileHeaderList[1].PixelWidth);
                // Assert.IsTrue(descriptorList[1].PixelHeight == clientFileHeaderList[1].PixelHeight);
                //
                // var thumbnailResponse2 = await getFilesDriveSvc.GetThumbnail(
                //     fileId: uploadedFile.FileId,
                //     alias: uploadedFile.TargetDrive.Alias,
                //     type: uploadedFile.TargetDrive.Type,
                //     thumbnail2.PixelHeight,
                //     thumbnail2.PixelWidth);
                //
                // Assert.IsTrue(thumbnailResponse2.IsSuccessStatusCode);
                // Assert.IsNotNull(thumbnailResponse2.Content);
                // Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnail2CipherBytes, await thumbnailResponse2.Content!.ReadAsByteArrayAsync()));
                //
                // decryptedKeyHeader.AesKey.Wipe();
                keyHeader.AesKey.Wipe();

                //
                // End testing that the file was correctly uploaded
                //

                // return uploadedFile;
            }
        }

        private async Task<ExternalFileIdentifier> SendMessageUsingOwnerApi(TestAppContext senderAppContext,
            List<string> recipients, string message, string payload)
        {
            using (var client =
                   _scaffold.OwnerApi.CreateOwnerApiHttpClient(senderAppContext.Identity, out var ownerSharedSecret))
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
                    TransitOptions = new TransitOptions()
                    {
                        UseGlobalTransitId = true,
                        Recipients = recipients,
                        Schedule = ScheduleOptions.SendNowAwaitResponse
                    }
                };

                var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
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
                        PayloadIsEncrypted = true,
                        AppData = new()
                        {
                            Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                            FileType = FileType,
                            ContentIsComplete = false,
                            JsonContent = DotYouSystemSerializer.Serialize(new { content = message }),
                            PreviewThumbnail = new ImageDataContent()
                            {
                                PixelHeight = 100,
                                PixelWidth = 100,
                                ContentType = "image/png",
                                Content = keyHeader.EncryptDataAes(TestMedia.PreviewPngThumbnailBytes)
                            },

                            AdditionalThumbnails = new[] { thumbnail1, thumbnail2 }
                        },
                        AccessControlList = AccessControlList.OwnerOnly
                    },
                };

                var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, transferIv, ref ownerSharedSecret);

                var payloadCipher = keyHeader.EncryptDataAesAsStream(payload);

                var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
                var response = await driveSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)),
                    new StreamPart(new MemoryStream(thumbnail1CipherBytes), thumbnail1.GetFilename(), thumbnail1.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)),
                    new StreamPart(new MemoryStream(thumbnail2CipherBytes), thumbnail2.GetFilename(), thumbnail2.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var uploadResult = response.Content;

                Assert.That(uploadResult.File, Is.Not.Null);
                Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                Assert.IsTrue(uploadResult.File.TargetDrive.IsValid());

                foreach (var recipient in recipients)
                {
                    Assert.IsTrue(uploadResult.RecipientStatus.ContainsKey(recipient), $"Message was not delivered to ${recipient}");
                    Assert.IsTrue(uploadResult.RecipientStatus[recipient] == TransferStatus.Delivered, $"Message was not delivered to ${recipient}");
                }

                var uploadedFile = uploadResult.File;


                //
                // Begin testing that the file was correctly uploaded
                //

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
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.EqualTo(Guid.Empty.ToByteArray()), "Iv was all zeros");
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes));

                var decryptedKeyHeader = clientFileHeader.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ownerSharedSecret);

                Assert.That(decryptedKeyHeader.AesKey.IsSet(), Is.True);
                Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(decryptedKeyHeader.AesKey.GetKey(), keyHeader.AesKey.GetKey()));

                //validate preview thumbnail
                Assert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.ContentType == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.ContentType);
                Assert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.PixelHeight == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelHeight);
                Assert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.PixelWidth == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelWidth);
                Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(descriptor.FileMetadata.AppData.PreviewThumbnail.Content, clientFileHeader.FileMetadata.AppData.PreviewThumbnail.Content));

                Assert.IsTrue(clientFileHeader.FileMetadata.AppData.AdditionalThumbnails.Count() == 2);


                //
                // Get the payload that was uploaded, test it
                // 

                var payloadResponse = await getFilesDriveSvc.GetPayload(uploadedFile);
                Assert.That(payloadResponse.IsSuccessStatusCode, Is.True);
                Assert.That(payloadResponse.Content, Is.Not.Null);

                var payloadResponseCipher = await payloadResponse.Content.ReadAsByteArrayAsync();
                Assert.That(((MemoryStream)payloadCipher).ToArray(), Is.EqualTo(payloadResponseCipher));

                var aesKey = decryptedKeyHeader.AesKey;
                var decryptedPayloadBytes = Core.Cryptography.Crypto.AesCbc.Decrypt(
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

                var thumbnailResponse1 = await getFilesDriveSvc.GetThumbnail(new GetThumbnailRequest()
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

                var thumbnailResponse2 = await getFilesDriveSvc.GetThumbnail(new GetThumbnailRequest()
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

                //
                // End testing that the file was correctly uploaded
                //

                return uploadedFile;
            }
        }
    }
}