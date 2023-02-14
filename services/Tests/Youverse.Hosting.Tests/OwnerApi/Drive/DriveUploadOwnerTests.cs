using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Core.Query;
using Youverse.Core.Services.Drive.Core.Storage;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers.Base.Upload;
using Youverse.Hosting.Tests.AppAPI;

namespace Youverse.Hosting.Tests.OwnerApi.Drive
{
    public class DriveUploadOwnerTests
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

        [Test]
        [Ignore("This is tested in the app api until we determine if there are diff behaviors when transferring using the owner api")]
        public async Task CanGetAndSetGlobalTransitId()
        {
        }

        [Test(Description = "Test upload as owner")]
        public async Task UploadOnly()
        {
            var identity = TestIdentities.Frodo;
            var frodoOwnerClient = _scaffold.CreateOwnerApiClient(identity);

            var targetDrive = TargetDrive.NewTargetDrive();
            await frodoOwnerClient.Drive.CreateDrive(targetDrive, "some drive", "", false, true);

            var metadata = new UploadFileMetadata()
            {
                ContentType = "application/json",
                PayloadIsEncrypted = true,
                AppData = new()
                {
                    Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                    ContentIsComplete = true,
                    JsonContent = DotYouSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" })
                }
            };

            var uploadResult = await frodoOwnerClient.Drive.UploadStandardFileMetadata(targetDrive, metadata);

            Assert.That(uploadResult.File, Is.Not.Null);
            Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
            Assert.IsTrue(uploadResult.File.TargetDrive.IsValid());
            Assert.IsTrue(uploadResult.File.TargetDrive == targetDrive);
            Assert.That(uploadResult.RecipientStatus, Is.Null);

            ////
            var fileId = uploadResult.File.FileId;

            //retrieve the file that was uploaded; decrypt;
            var clientFileHeader = await frodoOwnerClient.Drive.GetFileHeader(
                new ExternalFileIdentifier() { TargetDrive = targetDrive, FileId = fileId },
                FileSystemType.Standard
                );
            
            Assert.That(clientFileHeader.FileMetadata, Is.Not.Null);
            Assert.That(clientFileHeader.FileMetadata.AppData, Is.Not.Null);

            Assert.That(clientFileHeader.FileMetadata.ContentType, Is.EqualTo(metadata.ContentType));
            CollectionAssert.AreEquivalent(clientFileHeader.FileMetadata.AppData.Tags, metadata.AppData.Tags);
            Assert.That(clientFileHeader.FileMetadata.AppData.JsonContent, Is.EqualTo(metadata.AppData.JsonContent));
            Assert.That(clientFileHeader.FileMetadata.AppData.ContentIsComplete, Is.EqualTo(metadata.AppData.ContentIsComplete));

            Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader, Is.Not.Null);
            Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.Null);
            Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv.Length, Is.GreaterThanOrEqualTo(16));
            Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.EqualTo(Guid.Empty.ToByteArray()), "Iv was all zeros");
            Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes));
            //
            // var decryptedKeyHeader = clientFileHeader.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ownerSharedSecret);
            //
            // Assert.That(decryptedKeyHeader.AesKey.IsSet(), Is.True);
            // var fileKey = decryptedKeyHeader.AesKey;
            // Assert.That(fileKey, Is.Not.EqualTo(Guid.Empty.ToByteArray()));
            //
            // //get the payload and decrypt, then compare
            // var payloadResponse = await frodoOwnerClient.Drive.GetPayload(new ExternalFileIdentifier() { TargetDrive = targetDrive, FileId = fileId });
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
            // var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payloadDataRaw);
            // Assert.That(payloadBytes, Is.EqualTo(decryptedPayloadBytes));
            //
            // // var decryptedPayloadRaw = System.Text.Encoding.UTF8.GetString(decryptedPayloadBytes);
            //
            // decryptedKeyHeader.AesKey.Wipe();
        }

        [Test(Description = "Test upload as owner")]
        public async Task FailsToUploadInvalidRequiredSecurityGroupToOwnerOnlyDrive()
        {
            var identity = TestIdentities.Frodo;

            var testContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(identity, ownerOnlyDrive: true);

            using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
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
                        PayloadIsEncrypted = true,
                        AppData = new()
                        {
                            Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                            ContentIsComplete = true,
                            JsonContent = DotYouSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" })
                        },
                        AccessControlList = new() { RequiredSecurityGroup = SecurityGroupType.Anonymous }
                    },
                };

                var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, transferIv, ref ownerSharedSecret);

                var payloadDataRaw = "{payload:true, image:'b64 data'}";
                var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadDataRaw);

                var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
                var response = await driveSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                Assert.That(response.IsSuccessStatusCode, Is.False);
                Assert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest);
                Assert.IsTrue(int.TryParse(DotYouSystemSerializer.Deserialize<ProblemDetails>(response!.Error!.Content!)!.Extensions["errorCode"].ToString(), out var code),
                    "Could not parse problem result");
                Assert.IsTrue(code == (int)YouverseClientErrorCode.CannotUploadEncryptedFileForAnonymous);
            }
        }

        [Test(Description = "Test upload thumbnails as owner")]
        public async Task UploadWithThumbnails()
        {
            var identity = TestIdentities.Frodo;

            var testContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(identity);

            using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
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
                            ContentIsComplete = false,
                            JsonContent = DotYouSystemSerializer.Serialize(new { content = "some content" }),

                            PreviewThumbnail = new ImageDataContent()
                            {
                                PixelHeight = 100,
                                PixelWidth = 100,
                                ContentType = "image/png",
                                Content = keyHeader.EncryptDataAes(TestMedia.PreviewPngThumbnailBytes)
                            },

                            AdditionalThumbnails = new[] { thumbnail1, thumbnail2 }
                        }
                    },
                };

                var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, transferIv, ref ownerSharedSecret);

                var payloadDataRaw = "{payload:true, image:'b64 data'}";
                var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadDataRaw);

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

                Assert.That(uploadResult.RecipientStatus, Is.Null);
                var uploadedFile = uploadResult.File;


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
                Assert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.ContentType == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.ContentType);
                Assert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.PixelHeight == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelHeight);
                Assert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.PixelWidth == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelWidth);
                Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(descriptor.FileMetadata.AppData.PreviewThumbnail.Content, clientFileHeader.FileMetadata.AppData.PreviewThumbnail.Content));

                Assert.IsTrue(clientFileHeader.FileMetadata.AppData.AdditionalThumbnails.Count() == 2);


                //
                // Get the payload that was uploaded, test it
                // 

                var payloadResponse = await getFilesDriveSvc.GetPayloadPost(uploadedFile);
                Assert.That(payloadResponse.IsSuccessStatusCode, Is.True);
                Assert.That(payloadResponse.Content, Is.Not.Null);

                var payloadResponseCipher = await payloadResponse.Content.ReadAsByteArrayAsync();
                Assert.That(((MemoryStream)payloadCipher).ToArray(), Is.EqualTo(payloadResponseCipher));

                var aesKey = decryptedKeyHeader.AesKey;
                var decryptedPayloadBytes = Core.Cryptography.Crypto.AesCbc.Decrypt(
                    cipherText: payloadResponseCipher,
                    Key: ref aesKey,
                    IV: decryptedKeyHeader.Iv);

                var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payloadDataRaw);
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
            }
        }

        //tests
        [Test(Description = "")]
        public async Task FailToUpdateNonExistentFile()
        {
            var identity = TestIdentities.Frodo;

            var testContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(identity);

            using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var transferIv = ByteArrayUtil.GetRndByteArray(16);
                var keyHeader = KeyHeader.NewRandom16();

                var instructionSet = new UploadInstructionSet()
                {
                    TransferIv = transferIv,
                    StorageOptions = new StorageOptions()
                    {
                        Drive = testContext.TargetDrive,
                        OverwriteFileId = Guid.NewGuid() //some random guid pretending to be a file that exists
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
                        PayloadIsEncrypted = true,
                        AppData = new()
                        {
                            Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                            ContentIsComplete = false,
                            JsonContent = "some content"
                        }
                    },
                };

                var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, transferIv, ref ownerSharedSecret);

                var payloadDataRaw = "{payload:true, image:'b64 data'}";
                var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadDataRaw);

                var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
                var response = await driveSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                Assert.That(response.IsSuccessStatusCode, Is.False);
                Assert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest);
                Assert.IsTrue(int.TryParse(DotYouSystemSerializer.Deserialize<ProblemDetails>(response!.Error!.Content!)!.Extensions["errorCode"].ToString(), out var code),
                    "Could not parse problem result");
                Assert.IsTrue(code == (int)YouverseClientErrorCode.CannotOverwriteNonExistentFile);

                keyHeader.AesKey.Wipe();
            }
        }

        [Test(Description = "")]
        public async Task CanUploadClientUniqueIdAndGetOneFile()
        {
            //(use query modified and querybatch)

            var identity = TestIdentities.Frodo;

            var testContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(identity);

            using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
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
                        PayloadIsEncrypted = true,
                        AppData = new()
                        {
                            UniqueId = Guid.NewGuid(),
                            Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                            ContentIsComplete = false,
                            JsonContent = DotYouSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" })
                        }
                    },
                };

                var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, transferIv, ref ownerSharedSecret);

                var payloadDataRaw = "{payload:true, image:'b64 data'}";
                var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadDataRaw);

                var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
                var response = await driveSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var uploadResult = response.Content;

                Assert.That(uploadResult.File, Is.Not.Null);
                Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                Assert.IsTrue(uploadResult.File.TargetDrive.IsValid());

                //
                // Get file bu ClientUniqueId
                //

                var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, ownerSharedSecret);
                var expectedClientUniqueId = descriptor.FileMetadata.AppData.UniqueId.GetValueOrDefault();
                var qp = new FileQueryParams()
                {
                    TargetDrive = uploadResult.File.TargetDrive,
                    ClientUniqueIdAtLeastOne = new List<Guid>() { expectedClientUniqueId }
                };

                var resultOptions = new QueryBatchResultOptionsRequest()
                {
                    CursorState = "",
                    MaxRecords = 10,
                    IncludeMetadataHeader = false
                };

                var request = new QueryBatchRequest()
                {
                    QueryParams = qp,
                    ResultOptionsRequest = resultOptions
                };

                var getBatchResponse = await svc.GetBatch(request);
                Assert.IsTrue(getBatchResponse.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var batch = getBatchResponse.Content;

                Assert.IsNotNull(batch);
                Assert.IsNotNull(batch.SearchResults.Single(item => item.FileMetadata.AppData.UniqueId == expectedClientUniqueId));
            }
        }

        [Test(Description = "")]
        public async Task FailToUploadTwoFilesWithSameClientUniqueId()
        {
            var uid1 = Guid.NewGuid();
            var (testAppContext, uid1UploadResult) = await this.UploadUniqueIdTestFile(TestIdentities.Merry, uid1);

            //
            // Upload a new file and try using uid1, which is already in use by file1
            //
            using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(testAppContext.Identity, out var ownerSharedSecret))
            {
                var keyHeader = KeyHeader.NewRandom16();
                var instructionSet = UploadInstructionSet.WithTargetDrive(testAppContext.TargetDrive);
                var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
                var instructionStream = new MemoryStream(bytes);

                var descriptor = new UploadFileDescriptor()
                {
                    EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, instructionSet.TransferIv, ref ownerSharedSecret),
                    FileMetadata = new()
                    {
                        ContentType = "application/json",
                        PayloadIsEncrypted = true,
                        AppData = new()
                        {
                            UniqueId = uid1, // Here we try to reuse the uniqueId associated with first upload
                            ContentIsComplete = false,
                            JsonContent = DotYouSystemSerializer.Serialize(new { message = "I am a second file" })
                        }
                    },
                };

                var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref ownerSharedSecret);

                var payloadDataRaw = "{payload:true, image:'b64 data'}";
                var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadDataRaw);

                var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
                var response = await driveSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                //
                // This should fail because we tried to reuse a uid1
                //
                Assert.That(response.IsSuccessStatusCode, Is.False);
                Assert.That(response.IsSuccessStatusCode, Is.False);
                Assert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest);
                Assert.IsTrue(int.TryParse(DotYouSystemSerializer.Deserialize<ProblemDetails>(response!.Error!.Content!)!.Extensions["errorCode"].ToString(), out var code),
                    "Could not parse problem result");
                Assert.IsTrue(code == (int)YouverseClientErrorCode.ExistingFileWithUniqueId);
            }
        }

        [Test(Description = "")]
        public async Task FailToChangeClientUniqueIdToExistingClientUniqueId()
        {
            var uid1 = Guid.NewGuid();
            var uid2 = Guid.NewGuid();

            var (testAppContext, uid1UploadResult) = await this.UploadUniqueIdTestFile(TestIdentities.Samwise, uid1);

            //
            // Upload a second file to the same drive with uid2
            //
            UploadResult secondFileUploadResult;
            using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(testAppContext.Identity, out var ownerSharedSecret))
            {
                var keyHeader = KeyHeader.NewRandom16();

                var instructionSet = UploadInstructionSet.WithTargetDrive(testAppContext.TargetDrive);
                var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
                var instructionStream = new MemoryStream(bytes);

                var descriptor = new UploadFileDescriptor()
                {
                    EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, instructionSet.TransferIv, ref ownerSharedSecret),
                    FileMetadata = new()
                    {
                        ContentType = "application/json",
                        PayloadIsEncrypted = true,
                        AppData = new()
                        {
                            UniqueId = uid2,
                            ContentIsComplete = false,
                            JsonContent = DotYouSystemSerializer.Serialize(new { message = "I am a second file" })
                        }
                    },
                };

                var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref ownerSharedSecret);

                var payloadDataRaw = "{payload:true, image:'b64 data'}";
                var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadDataRaw);

                var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
                var response = await driveSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);

                secondFileUploadResult = response.Content;

                Assert.That(secondFileUploadResult.File, Is.Not.Null);
                Assert.That(secondFileUploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                Assert.IsTrue(secondFileUploadResult.File.TargetDrive.IsValid());
            }

            //
            // Update second file and try using uid1, which is already in use by file1
            //
            using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(testAppContext.Identity, out var ownerSharedSecret))
            {
                var keyHeader = KeyHeader.NewRandom16();

                var instructionSet = UploadInstructionSet.WithTargetDrive(testAppContext.TargetDrive);

                //overwrite the second file we just uploaded
                instructionSet.StorageOptions.OverwriteFileId = secondFileUploadResult.File.FileId;

                var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
                var instructionStream = new MemoryStream(bytes);

                var descriptor = new UploadFileDescriptor()
                {
                    EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, instructionSet.TransferIv, ref ownerSharedSecret),
                    FileMetadata = new()
                    {
                        ContentType = "application/json",
                        PayloadIsEncrypted = true,
                        AppData = new()
                        {
                            UniqueId = uid1, //here we try to reuse the uniqueId associated with first upload
                            ContentIsComplete = false,
                            JsonContent = DotYouSystemSerializer.Serialize(new { message = "Some message" })
                        }
                    },
                };

                var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref ownerSharedSecret);

                var payloadDataRaw = "{payload:true, image:'b64 data'}";
                var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadDataRaw);

                var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
                var response = await driveSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                Assert.That(response.IsSuccessStatusCode, Is.False);
                Assert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest);
                Assert.IsTrue(int.TryParse(DotYouSystemSerializer.Deserialize<ProblemDetails>(response!.Error!.Content!)!.Extensions["errorCode"].ToString(), out var code),
                    "Could not parse problem result");
                Assert.IsTrue(code == (int)YouverseClientErrorCode.ExistingFileWithUniqueId);
            }
        }


        private async Task<(TestAppContext appContext, UploadResult uploadResult)> UploadUniqueIdTestFile(TestIdentity identity, Guid? uniqueId)
        {
            var testContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(identity);

            using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var keyHeader = KeyHeader.NewRandom16();

                var instructionSet = UploadInstructionSet.WithTargetDrive(testContext.TargetDrive);

                var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
                var instructionStream = new MemoryStream(bytes);

                var descriptor = new UploadFileDescriptor()
                {
                    EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, instructionSet.TransferIv, ref ownerSharedSecret),
                    FileMetadata = new()
                    {
                        ContentType = "application/json",
                        PayloadIsEncrypted = true,
                        AppData = new()
                        {
                            UniqueId = uniqueId,
                            ContentIsComplete = false,
                            JsonContent = DotYouSystemSerializer.Serialize(new { message = "Some message" })
                        }
                    },
                };

                var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref ownerSharedSecret);

                var payloadDataRaw = "{payload:true, image:'b64 data'}";
                var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadDataRaw);

                var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
                var response = await driveSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var uploadResult = response.Content;

                Assert.That(uploadResult.File, Is.Not.Null);
                Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                Assert.IsTrue(uploadResult.File.TargetDrive.IsValid());

                //
                // Get file bu ClientUniqueId
                //

                var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, ownerSharedSecret);
                var qp = new FileQueryParams()
                {
                    TargetDrive = uploadResult.File.TargetDrive,
                    ClientUniqueIdAtLeastOne = new List<Guid>() { uniqueId.GetValueOrDefault() }
                };

                var resultOptions = new QueryBatchResultOptionsRequest()
                {
                    CursorState = "",
                    MaxRecords = 10,
                    IncludeMetadataHeader = false
                };

                var request = new QueryBatchRequest()
                {
                    QueryParams = qp,
                    ResultOptionsRequest = resultOptions
                };

                var getBatchResponse = await svc.GetBatch(request);
                Assert.IsTrue(getBatchResponse.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var batch = getBatchResponse.Content;

                Assert.IsNotNull(batch);
                Assert.IsNotNull(batch.SearchResults.Single(item => item.FileMetadata.AppData.UniqueId == uniqueId.GetValueOrDefault()));

                return (testContext, uploadResult);
            }
        }
    }
}