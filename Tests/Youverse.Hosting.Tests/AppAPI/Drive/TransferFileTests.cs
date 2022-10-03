using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.ClientToken.Transit;
using Youverse.Hosting.Tests.AppAPI;
using Youverse.Hosting.Tests.AppAPI.Transit;

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

        [Test(Description = "Test basic transfer; includes thumbnails")]
        public async Task CanSendTransferAndRecipientCanGetFilesByTag()
        {
            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            Guid appId = Guid.NewGuid();
            var testContext = await _scaffold.OwnerApi.SetupTestSampleApp(appId, sender, false, TargetDrive.NewTargetDrive(), driveAllowAnonymousReads: true);
            var recipientContext = await _scaffold.OwnerApi.SetupTestSampleApp(testContext.AppId, recipient, false, testContext.TargetDrive);

            Guid fileTag = Guid.NewGuid();

            await _scaffold.OwnerApi.CreateConnection(sender.DotYouId, recipient.DotYouId);

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
                    Recipients = new List<string>() { recipient.DotYouId }
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

            var key = testContext.SharedSecret.ToSensitiveByteArray();
            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref key),
                FileMetadata = new()
                {
                    //todo: maybe rename to payload content type
                    ContentType = "application/json",
                    AppData = new()
                    {
                        Tags = new List<Guid>() { fileTag },
                        ContentIsComplete = true,
                        JsonContent = DotYouSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                        PreviewThumbnail = new ImageDataContent()
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
                var response = await transitSvc.Upload(
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

            await _scaffold.OwnerApi.ProcessOutbox(sender.DotYouId);

            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(recipient, recipientContext.ClientAuthenticationToken))
            {
                //First force transfers to be put into their long term location
                var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(client);
                client.DefaultRequestHeaders.Add("SY4829", Guid.Parse("a1224889-c0b1-4298-9415-76332a9af80e").ToString());
                var resp = await transitAppSvc.ProcessIncomingTransfers(new ProcessTransfersRequest() { TargetDrive = recipientContext.TargetDrive });
                Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);


                var driveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, recipientContext.SharedSecret);

                //lookup the fileId by the fileTag from earlier

                var queryBatchResponse = await driveSvc.QueryBatch(new QueryBatchRequest()
                {
                    QueryParams = new FileQueryParams()
                    {
                        TargetDrive = recipientContext.TargetDrive,
                        TagsMatchAll = new List<Guid>() { fileTag }
                    },
                    ResultOptionsRequest = new Youverse.Hosting.Controllers.QueryBatchResultOptionsRequest()
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
                    FileId = queryBatchResponse.Content.SearchResults.Single().FileMetadata.File.FileId
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

            await _scaffold.OwnerApi.DisconnectIdentities(sender.DotYouId, recipientContext.Identity);
        }

        // [Test(Description = "Updates a thumbnail")]
        public async Task UpdateThumbnail()
        {
            //upload a file with a thumbnail
        }

        // [Test(Description = "Updates a thumbnail; and transfer it")]
        public async Task UpdateThumbnailWithTransfer()
        {
            //upload a file with a thumbnail

            //transfer the thumbnail

            //upload an updated thumbnail

            //transfer that update

            //NOTE: I think this requires supporting file collaboration in transit where I can send you updates for an existing file
        }

        //[Test(Description = "")]
        public async Task RecipientCanGetReceivedTransferFromDriveAndIsSearchable()
        {
            Assert.Inconclusive("TODO");
        }
    }
}