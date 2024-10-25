using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Routing;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Core.Storage;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.ApiClient;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.Drive.StandardFileSystem
{
    public class DriveUploadOwnerTests
    {
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


        [Test]
        [Ignore("This is tested in the app api until we determine if there are diff behaviors when transferring using the owner api")]
        public void CanGetAndSetGlobalTransitId()
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
                IsEncrypted = true,
                AllowDistribution = false,
                AppData = new()
                {
                    Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                    Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" })
                }
            };


            var (uploadResponse, _) = await frodoOwnerClient.DriveRedux.UploadNewEncryptedMetadata(targetDrive, metadata);

            var uploadResult = uploadResponse.Content;

            Assert.That(uploadResult.File, Is.Not.Null);
            Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
            Assert.IsTrue(uploadResult.File.TargetDrive.IsValid());
            Assert.IsTrue(uploadResult.File.TargetDrive == targetDrive);
            Assert.That(uploadResult.RecipientStatus, Is.Null);

            ////
            var fileId = uploadResult.File.FileId;

            //retrieve the file that was uploaded; decrypt;
            var clientFileHeader = await frodoOwnerClient.Drive.GetFileHeader(
                FileSystemType.Standard,
                new ExternalFileIdentifier() { TargetDrive = targetDrive, FileId = fileId }
            );

            Assert.That(clientFileHeader.FileMetadata, Is.Not.Null);
            Assert.That(clientFileHeader.FileMetadata.AppData, Is.Not.Null);

            CollectionAssert.AreEquivalent(clientFileHeader.FileMetadata.AppData.Tags, metadata.AppData.Tags);
            Assert.That(clientFileHeader.FileMetadata.AppData.Content, Is.EqualTo(metadata.AppData.Content));
            Assert.That(clientFileHeader.FileMetadata.Payloads.Count == 0);

            Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader, Is.Not.Null);
            Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.Null);
            Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv.Length, Is.GreaterThanOrEqualTo(16));
            Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.EqualTo(Guid.Empty.ToByteArray()), "Iv was all zeros");
            Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes));

            Assert.IsTrue(clientFileHeader.FileByteCount > 0, "Disk usage was not calculated");

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

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var transferIv = ByteArrayUtil.GetRndByteArray(16);
                var keyHeader = KeyHeader.NewRandom16();

                var instructionSet = new UploadInstructionSet()
                {
                    TransferIv = transferIv,
                    StorageOptions = new StorageOptions()
                    {
                        Drive = testContext.TargetDrive
                    },
                    Manifest = new UploadManifest()
                    {
                        PayloadDescriptors = new List<UploadManifestPayloadDescriptor>()
                        {
                            WebScaffold.CreatePayloadDescriptorFrom(WebScaffold.PAYLOAD_KEY)
                        }
                    }
                };

                var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
                var instructionStream = new MemoryStream(bytes);

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
                            Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" })
                        },
                        AccessControlList = new() { RequiredSecurityGroup = SecurityGroupType.Anonymous }
                    },
                };

                var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref ownerSharedSecret);

                var payloadDataRaw = "{payload:true, image:'b64 data'}";
                var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadDataRaw);

                var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
                var response = await driveSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, WebScaffold.PAYLOAD_KEY, "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                Assert.That(response.IsSuccessStatusCode, Is.False);
                Assert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest);

                var code = TestUtils.ParseProblemDetails(response!.Error!);
                Assert.IsTrue(code == OdinClientErrorCode.CannotUploadEncryptedFileForAnonymous);
            }
        }

        [Test(Description = "Test upload thumbnails as owner")]
        public async Task UploadWithThumbnails()
        {
            var identity = TestIdentities.Frodo;

            var testContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(identity);

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var transferIv = ByteArrayUtil.GetRndByteArray(16);
                var keyHeader = KeyHeader.NewRandom16();

                var thumbnail1 = new ThumbnailDescriptor()
                {
                    PixelHeight = 300,
                    PixelWidth = 300,
                    ContentType = "image/jpeg"
                };
                var thumbnail1CipherBytes = keyHeader.EncryptDataAes(TestMedia.ThumbnailBytes300);
                var tk1 = $"{thumbnail1.PixelHeight}{thumbnail1.PixelWidth}{WebScaffold.PAYLOAD_KEY}";

                var thumbnail2 = new ThumbnailDescriptor()
                {
                    PixelHeight = 400,
                    PixelWidth = 400,
                    ContentType = "image/jpeg",
                };
                var thumbnail2CipherBytes = keyHeader.EncryptDataAes(TestMedia.ThumbnailBytes400);
                var tk2 = $"{thumbnail2.PixelHeight}{thumbnail2.PixelWidth}{WebScaffold.PAYLOAD_KEY}";

                var instructionSet = new UploadInstructionSet()
                {
                    TransferIv = transferIv,
                    StorageOptions = new StorageOptions()
                    {
                        Drive = testContext.TargetDrive
                    },
                    Manifest = new UploadManifest()
                    {
                        PayloadDescriptors = new List<UploadManifestPayloadDescriptor>()
                        {
                            new UploadManifestPayloadDescriptor()
                            {
                                Iv = ByteArrayUtil.GetRndByteArray(16),
                                PayloadKey = WebScaffold.PAYLOAD_KEY,
                                Thumbnails = new List<UploadedManifestThumbnailDescriptor>()
                                {
                                    new()
                                    {
                                        PixelHeight = thumbnail1.PixelHeight,
                                        PixelWidth = thumbnail1.PixelWidth,
                                        ThumbnailKey = tk1
                                    },
                                    new()
                                    {
                                        PixelWidth = thumbnail2.PixelWidth,
                                        PixelHeight = thumbnail2.PixelHeight,
                                        ThumbnailKey = tk2
                                    }
                                }
                            }
                        }
                    }
                };

                var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
                var instructionStream = new MemoryStream(bytes);

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
                            Content = OdinSystemSerializer.Serialize(new { content = "some content" }),

                            PreviewThumbnail = new ThumbnailContent()
                            {
                                PixelHeight = 100,
                                PixelWidth = 100,
                                ContentType = "image/png",
                                Content = keyHeader.EncryptDataAes(TestMedia.PreviewPngThumbnailBytes)
                            }
                        }
                    },
                };

                var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref ownerSharedSecret);

                var payloadDataRaw = "{payload:true, image:'b64 data'}";
                var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadDataRaw);

                var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
                var response = await driveSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, WebScaffold.PAYLOAD_KEY, "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)),
                    new StreamPart(new MemoryStream(thumbnail1CipherBytes), tk1, thumbnail1.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail)),
                    new StreamPart(new MemoryStream(thumbnail2CipherBytes), tk2, thumbnail2.ContentType,
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
                Assert.That(clientFileHeader.FileMetadata.Payloads.Count == 1);

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

                Assert.IsTrue(clientFileHeader.FileMetadata.GetPayloadDescriptor(WebScaffold.PAYLOAD_KEY).Thumbnails.Count() == 2);


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
                    iv: decryptedKeyHeader.Iv);

                var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payloadDataRaw);
                Assert.That(payloadBytes, Is.EqualTo(decryptedPayloadBytes));

                // var decryptedPayloadRaw = System.Text.Encoding.UTF8.GetString(decryptedPayloadBytes);


                //
                // Validate additional thumbnails
                //

                var descriptorList = new List<ThumbnailDescriptor>() { thumbnail1, thumbnail2 };

                var clientFileHeaderList = clientFileHeader.FileMetadata.GetPayloadDescriptor(WebScaffold.PAYLOAD_KEY).Thumbnails.ToList();

                //validate thumbnail 1
                Assert.IsTrue(descriptorList[0].ContentType == clientFileHeaderList[0].ContentType);
                Assert.IsTrue(descriptorList[0].PixelWidth == clientFileHeaderList[0].PixelWidth);
                Assert.IsTrue(descriptorList[0].PixelHeight == clientFileHeaderList[0].PixelHeight);

                var thumbnailResponse1 = await getFilesDriveSvc.GetThumbnailPost(new GetThumbnailRequest()
                {
                    File = uploadedFile,
                    Height = thumbnail1.PixelHeight,
                    Width = thumbnail1.PixelWidth,
                    PayloadKey = WebScaffold.PAYLOAD_KEY
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
                    Width = thumbnail2.PixelWidth,
                    PayloadKey = WebScaffold.PAYLOAD_KEY
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

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
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
                    },
                    Manifest = new UploadManifest()
                    {
                        PayloadDescriptors = new List<UploadManifestPayloadDescriptor>()
                        {
                            WebScaffold.CreatePayloadDescriptorFrom(WebScaffold.PAYLOAD_KEY)
                        }
                    }
                };

                var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
                var instructionStream = new MemoryStream(bytes);

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
                            Content = "some content"
                        }
                    },
                };

                var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref ownerSharedSecret);

                var payloadDataRaw = "{payload:true, image:'b64 data'}";
                var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadDataRaw);

                var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
                var response = await driveSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, WebScaffold.PAYLOAD_KEY, "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                Assert.That(response.IsSuccessStatusCode, Is.False);
                Assert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest);

                var code = TestUtils.ParseProblemDetails(response.Error!);
                Assert.IsTrue(code == OdinClientErrorCode.CannotOverwriteNonExistentFile);

                keyHeader.AesKey.Wipe();
            }
        }

        [Test(Description = "")]
        public async Task CanUploadClientUniqueIdAndGetOneFile()
        {
            //(use query modified and querybatch)

            var identity = TestIdentities.Frodo;

            var testContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(identity);

            var ownerClient = _scaffold.CreateOwnerApiClient(identity);
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var fileMetadata = new UploadFileMetadata()
                {
                    AllowDistribution = false,
                    IsEncrypted = true,
                    AppData = new()
                    {
                        UniqueId = Guid.NewGuid(),
                        Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                        Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" })
                    }
                };

                var payloadDataRaw = "{payload:true, image:'b64 data'}";

                var testPayloads = new List<TestPayloadDefinition>()
                {
                    new()
                    {
                        Iv = ByteArrayUtil.GetRndByteArray(16),
                        Key = WebScaffold.PAYLOAD_KEY,
                        ContentType = "text/plain",
                        Content = payloadDataRaw.ToUtf8ByteArray(),
                        Thumbnails = new List<ThumbnailContent>() { }
                    }
                };

                var uploadManifest = new UploadManifest()
                {
                    PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
                };


                var (response, _, _, _) =
                    await ownerClient.DriveRedux.UploadNewEncryptedFile(testContext.TargetDrive, fileMetadata, uploadManifest, testPayloads);

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
                var expectedClientUniqueId = fileMetadata.AppData.UniqueId.GetValueOrDefault();
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
            var client2 = _scaffold.CreateOwnerApiClient(TestIdentities.Merry);

            var f2 = new UploadFileMetadata()
            {
                AllowDistribution = false,
                IsEncrypted = true,
                AppData = new()
                {
                    UniqueId = uid1,
                    Content = OdinSystemSerializer.Serialize(new { message = "I am a second file" })
                }
            };


            var response = await client2.DriveRedux.UploadNewMetadata(testAppContext.TargetDrive, f2);
            //
            // This should fail because we tried to reuse a uid1
            //
            Assert.That(response.IsSuccessStatusCode, Is.False);
            Assert.That(response.IsSuccessStatusCode, Is.False);
            Assert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest);

            var code = TestUtils.ParseProblemDetails(response.Error!);
            Assert.IsTrue(code == OdinClientErrorCode.ExistingFileWithUniqueId);
        }


        [Test(Description = "")]
        public async Task FailToChangeClientUniqueIdToExistingClientUniqueId()
        {
            var uid1 = Guid.NewGuid();
            var uid2 = Guid.NewGuid();

            var (testAppContext, uid1UploadResult) = await this.UploadUniqueIdTestFile(TestIdentities.Samwise, uid1);

            var client = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

            //
            // Upload a second file to the same drive with uid2
            //
            var fileMetadata2 = new UploadFileMetadata()
            {
                AllowDistribution = false,
                IsEncrypted = true,
                AppData = new()
                {
                    UniqueId = uid2,
                    Content = OdinSystemSerializer.Serialize(new { message = "I am a second file" })
                }
            };

            var response2 = await client.DriveRedux.UploadNewMetadata(testAppContext.TargetDrive, fileMetadata2);
            Assert.That(response2.IsSuccessStatusCode, Is.True);
            Assert.That(response2.Content, Is.Not.Null);

            UploadResult secondFileUploadResult = response2.Content;
            Assert.That(secondFileUploadResult.File, Is.Not.Null);
            Assert.That(secondFileUploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
            Assert.IsTrue(secondFileUploadResult.File.TargetDrive.IsValid());


            //
            // Update second file and try using uid1, which is already in use by file1
            //
            var fileMetadata3 = new UploadFileMetadata()
            {
                AllowDistribution = false,
                IsEncrypted = true,
                VersionTag = secondFileUploadResult.NewVersionTag,
                AppData = new()
                {
                    UniqueId = uid1,
                    Content = OdinSystemSerializer.Serialize(new { message = "Some message" })
                }
            };

            var response3 = await client.DriveRedux.UpdateExistingMetadata(secondFileUploadResult.File, secondFileUploadResult.NewVersionTag, fileMetadata3);

            Assert.That(response3.IsSuccessStatusCode, Is.False);
            Assert.IsTrue(response3.StatusCode == HttpStatusCode.BadRequest);

            var code = TestUtils.ParseProblemDetails(response3.Error!);
            Assert.IsTrue(code == OdinClientErrorCode.ExistingFileWithUniqueId);
        }

        private async Task<(TestAppContext appContext, UploadResult uploadResult)> UploadUniqueIdTestFile(TestIdentity identity, Guid? uniqueId)
        {
            var testContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(identity);

            var client = _scaffold.CreateOwnerApiClient(identity);

            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = false,
                IsEncrypted = true,
                AppData = new()
                {
                    UniqueId = uniqueId,
                    Content = OdinSystemSerializer.Serialize(new { message = "Some message" })
                }
            };

            var response = await client.DriveRedux.UploadNewMetadata(testContext.TargetDrive, fileMetadata);

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Assert.That(response.Content, Is.Not.Null);
            var uploadResult = response.Content;

            Assert.That(uploadResult.File, Is.Not.Null);
            Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
            Assert.IsTrue(uploadResult.File.TargetDrive.IsValid());

            //
            // Get file by ClientUniqueId
            //

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

            var getBatchResponse = await client.DriveRedux.QueryBatch(request);
            Assert.IsTrue(getBatchResponse.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            var batch = getBatchResponse.Content;

            Assert.IsNotNull(batch);
            Assert.IsNotNull(batch.SearchResults.Single(item => item.FileMetadata.AppData.UniqueId == uniqueId.GetValueOrDefault()));

            return (testContext, uploadResult);
        }
    }
}