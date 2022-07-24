using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Refit;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Tests.AppAPI;
using Youverse.Hosting.Tests.AppAPI.Transit;
using QueryBatchResultOptions = Youverse.Hosting.Controllers.QueryBatchResultOptions;

namespace Youverse.Hosting.Tests.DriveApi.App
{
    public class TransferFileTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [Test(Description = "Test basic transfer")]
        public async Task CanSendTransferAndSeeStatus()
        {
            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            Guid appId = Guid.NewGuid();
            var testContext = await _scaffold.OwnerApi.SetupTestSampleApp(appId, sender, false, TargetDrive.NewTargetDrive(), driveAllowAnonymousReads: true);
            var recipientContext = await _scaffold.OwnerApi.SetupTestSampleApp(testContext.AppId, recipient, false, testContext.TargetDrive);

            byte[] fileTag = new byte[] { 1, 1, 2, 3, 8 };

            await _scaffold.OwnerApi.CreateConnection(sender, recipient);

            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            var keyHeader = KeyHeader.NewRandom16();

            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = transferIv,
                StorageOptions = new StorageOptions()
                {
                    Drive = testContext.TargetDrive,
                    OverwriteFileId = null,
                    ExpiresTimestamp = null
                },

                TransitOptions = new TransitOptions()
                {
                    Recipients = new List<string>() { TestIdentities.Samwise }
                }
            };

            var bytes = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var thumbnail1 = new ThumbnailHeader()
            {
                PixelHeight = 300,
                PixelWidth = 300,
                ContentType = "image/jpeg"
            };
            var thumbnail1CipherBytes = keyHeader.EncryptDataAes(TestMedia.ThumbnailBytes300);

            var thumbnail2 = new ThumbnailHeader()
            {
                PixelHeight = 400,
                PixelWidth = 400,
                ContentType = "image/jpeg",
            };
            var thumbnail2CipherBytes = keyHeader.EncryptDataAes(TestMedia.ThumbnailBytes400);

            var key = testContext.SharedSecret.ToSensitiveByteArray();
            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref key),
                FileMetadata = new()
                {
                    ContentType = "application/json",
                    AppData = new()
                    {
                        Tags = new List<byte[]>() { fileTag },
                        ContentIsComplete = true,
                        JsonContent = JsonConvert.SerializeObject(new { message = "We're going to the beach; this is encrypted by the app" }),
                        PreviewThumbnail = new ThumbnailContent()
                        {
                            PixelHeight = 100,
                            PixelWidth = 100,
                            ContentType = "image/png",
                            Content = keyHeader.EncryptDataAes(TestMedia.PreviewPngThumbnailBytes)
                        },
                        AdditionalThumbnails = new[] { thumbnail1, thumbnail2 }
                    },
                    PayloadIsEncrypted = true,
                    AccessControlList = new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Authenticated }
                },
            };

            var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, transferIv, ref key);

            var payloadData = "{payload:true, image:'b64 data'}";
            var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadData);

            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(sender, testContext.ClientAuthenticationToken))
            {
                var transitSvc = RestService.For<IDriveTestHttpClientForApps>(client);
                var response = await transitSvc.UploadWithThumbnails(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)),
                    new StreamPart(new MemoryStream(thumbnail1CipherBytes), thumbnail1.GetFilename(), thumbnail1.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)),
                    new StreamPart(new MemoryStream(thumbnail2CipherBytes), thumbnail2.GetFilename(), thumbnail2.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var transferResult = response.Content;

                Assert.That(transferResult.File, Is.Not.Null);
                Assert.That(transferResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                Assert.IsTrue(transferResult.File.TargetDrive.IsValid());

                foreach (var r in instructionSet.TransitOptions.Recipients)
                {
                    Assert.IsTrue(transferResult.RecipientStatus.ContainsKey(r), $"Could not find matching recipient {r}");
                    Assert.IsTrue(transferResult.RecipientStatus[r] == TransferStatus.TransferKeyCreated, $"transfer key not created for {r}");
                }
            }

            await _scaffold.OwnerApi.ProcessOutbox(sender);

            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(recipient, recipientContext.ClientAuthenticationToken))
            {
                //First force transfers to be put into their long term location
                var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(client);
                client.DefaultRequestHeaders.Add("SY4829", Guid.Parse("a1224889-c0b1-4298-9415-76332a9af80e").ToString());
                var resp = await transitAppSvc.ProcessTransfers();
                Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);


                var driveSvc = RestService.For<IDriveTestHttpClientForApps>(client);

                //lookup the fileId by the fileTag from earlier

                var queryBatchResponse = await driveSvc.QueryBatch(new QueryBatchRequest()
                {
                    QueryParams = new FileQueryParams()
                    {
                        TargetDrive = recipientContext.TargetDrive,
                        TagsMatchAll = new List<byte[]>() { fileTag }
                    },
                    ResultOptions = new Youverse.Hosting.Controllers.QueryBatchResultOptions()
                    {
                        MaxRecords = 1,
                        IncludeMetadataHeader = true
                    }
                });

                Assert.IsTrue(queryBatchResponse.IsSuccessStatusCode);
                Assert.IsNotNull(queryBatchResponse.Content);
                Assert.IsTrue(queryBatchResponse.Content.SearchResults.Count() == 1);

                var uploadedFile = new ExternalFileIdentifier()
                {
                    TargetDrive = recipientContext.TargetDrive,
                    FileId = queryBatchResponse.Content.SearchResults.Single().FileId
                };

                var fileResponse = await driveSvc.GetFileHeader(uploadedFile);

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

                var ss = recipientContext.SharedSecret.ToSensitiveByteArray();
                var decryptedKeyHeader = clientFileHeader.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ss);

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

                var payloadResponse = await driveSvc.GetPayload(uploadedFile);
                Assert.That(payloadResponse.IsSuccessStatusCode, Is.True);
                Assert.That(payloadResponse.Content, Is.Not.Null);

                var payloadResponseCipher = await payloadResponse.Content.ReadAsByteArrayAsync();
                Assert.That(((MemoryStream)payloadCipher).ToArray(), Is.EqualTo(payloadResponseCipher));

                var aesKey = decryptedKeyHeader.AesKey;
                var decryptedPayloadBytes = Core.Cryptography.Crypto.AesCbc.Decrypt(
                    cipherText: payloadResponseCipher,
                    Key: ref aesKey,
                    IV: decryptedKeyHeader.Iv);

                var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payloadData);
                Assert.That(payloadBytes, Is.EqualTo(decryptedPayloadBytes));

                // var decryptedPayloadRaw = System.Text.Encoding.UTF8.GetString(decryptedPayloadBytes);


                //
                // Validate additional thumbnails
                //

                var descriptorList = descriptor.FileMetadata.AppData.AdditionalThumbnails.ToList();
                var clientFileHeaderList = clientFileHeader.FileMetadata.AppData.AdditionalThumbnails.ToList();

                //validate thumbnail 1
                Assert.IsTrue(descriptorList[0].ContentType == clientFileHeaderList[0].ContentType);
                Assert.IsTrue(descriptorList[0].PixelWidth == clientFileHeaderList[0].PixelWidth);
                Assert.IsTrue(descriptorList[0].PixelHeight == clientFileHeaderList[0].PixelHeight);

                var thumbnailResponse1 = await driveSvc.GetThumbnail(new GetThumbnailRequest()
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

                var thumbnailResponse2 = await driveSvc.GetThumbnail(new GetThumbnailRequest()
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
            }

            keyHeader.AesKey.Wipe();
            key.Wipe();

            await _scaffold.OwnerApi.DisconnectIdentities(sender, recipientContext.Identity);
        }

        [Test(Description = "")]
        public async Task RecipientCanGetReceivedTransferFromDriveAndIsSearchable()
        {
            var sender = TestIdentities.Samwise;
            var recipients = new List<string>() { TestIdentities.Frodo };
            var categoryId = Guid.NewGuid();
            var message = "ping ping pong pong";
            var jsonMessage = JsonConvert.SerializeObject(new { message = message });
            var payloadText = "lets alllll prraayyy for this world";

            var utilsContext = await _scaffold.AppApi.TransferFile(sender, recipients, new TransitTestUtilsOptions()
            {
                ProcessOutbox = true,
                ProcessTransitBox = true,
                AppDataCategoryId = categoryId,
                AppDataJsonContent = jsonMessage,
                PayloadData = payloadText
            });

            var recipientContext = utilsContext.RecipientContexts[TestIdentities.Frodo];
            using (var recipientClient = _scaffold.AppApi.CreateAppApiHttpClient(TestIdentities.Frodo, recipientContext.ClientAuthenticationToken))
            {
                // var svc = RestService.For<ITransitTestAppHttpClient>(recipientClient);
                // var driveSvc = RestService.For<IDriveTestHttpClientForApps>(recipientClient);

                // var fileHeaderResponse = await driveSvc.GetFileHeader(inboxItem.File.TargetDrive, inboxItem.File.FileId);
                // Assert.That(fileHeaderResponse.IsSuccessStatusCode, Is.True);
                // Assert.That(fileHeaderResponse.Content, Is.Not.Null);

                // var clientFileHeader = fileHeaderResponse.Content;
                //
                // Assert.That(clientFileHeader.FileMetadata, Is.Not.Null);
                // Assert.That(clientFileHeader.FileMetadata.AppData, Is.Not.Null);
                //
                // Assert.That(clientFileHeader.FileMetadata.ContentType, Is.EqualTo(utilsContext.FileMetadata.ContentType));
                // CollectionAssert.AreEquivalent(clientFileHeader.FileMetadata.AppData.Tags, utilsContext.FileMetadata.AppData.Tags);
                // Assert.That(clientFileHeader.FileMetadata.AppData.JsonContent, Is.EqualTo(utilsContext.FileMetadata.AppData.JsonContent));
                // Assert.That(clientFileHeader.FileMetadata.AppData.ContentIsComplete, Is.EqualTo(utilsContext.FileMetadata.AppData.ContentIsComplete));
                //
                // Assert.That(clientFileHeader.EncryptedKeyHeader, Is.Not.Null);
                // Assert.That(clientFileHeader.EncryptedKeyHeader.Iv, Is.Not.Null);
                // Assert.That(clientFileHeader.EncryptedKeyHeader.Iv.Length, Is.GreaterThanOrEqualTo(16));
                // Assert.That(clientFileHeader.EncryptedKeyHeader.Iv, Is.Not.EqualTo(Guid.Empty.ToByteArray()), "Iv byte array was all zeros");
                // Assert.That(clientFileHeader.EncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes));
                //
                // var key = recipientContext.SharedSecret.ToSensitiveByteArray();
                // var decryptedKeyHeader = clientFileHeader.EncryptedKeyHeader.DecryptAesToKeyHeader(ref key);
                //
                // Assert.That(decryptedKeyHeader.AesKey.IsSet(), Is.True);
                // var fileKey = decryptedKeyHeader.AesKey;
                // Assert.That(fileKey, Is.Not.EqualTo(Guid.Empty.ToByteArray()));
                //
                // //get the payload and decrypt, then compare
                // var payloadResponse = await driveSvc.GetPayload(inboxItem.File.TargetDrive, inboxItem.File.FileId);
                // Assert.That(payloadResponse.IsSuccessStatusCode, Is.True);
                // Assert.That(payloadResponse.Content, Is.Not.Null);
                //
                // var payloadResponseCipher = await payloadResponse.Content.ReadAsByteArrayAsync();
                // var bytes = decryptedKeyHeader.AesKey;
                // var decryptedPayloadBytes = Core.Cryptography.Crypto.AesCbc.Decrypt(
                //     cipherText: payloadResponseCipher,
                //     Key: ref bytes,
                //     IV: decryptedKeyHeader.Iv);
                //
                // var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payloadText);
                // Assert.That(payloadBytes, Is.EqualTo(decryptedPayloadBytes));
                //
                // var driveQueryClient = RestService.For<IDriveTestHttpClientForApps>(recipientClient);
                //
                // var startCursor = Array.Empty<byte>();
                // var stopCursor = Array.Empty<byte>();
                // var qp = new QueryParams()
                // {
                //     TagsMatchAtLeastOne = new List<byte[]>() { categoryId.ToByteArray() }
                // };
                //
                // var resultOptions = new ResultOptions()
                // {
                //     IncludeMetadataHeader = true,
                //     MaxRecords = 100
                // };

                // var response = await driveQueryClient.GetBatch(recipientContext.TargetDrive, startCursor, stopCursor, qp, resultOptions);

//                var response = await driveQueryClient.GetBatch(recipientContext.DriveAlias, categoryId, true, 1, 100);
                // Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                // var batch = response.Content;
                // Assert.IsNotNull(batch);

                // Assert.IsTrue(batch.SearchResults.Count() == 1);
                // CollectionAssert.AreEquivalent(utilsContext.FileMetadata.AppData.Tags, batch.SearchResults.First().Tags);


                // Console.WriteLine($"Items with category: {categoryId}");
                // foreach (var item in page.Results)
                // {
                //     Console.WriteLine($"{item.PrimaryCategoryId} {item.JsonContent}");
                // }
            }
        }
    }
}