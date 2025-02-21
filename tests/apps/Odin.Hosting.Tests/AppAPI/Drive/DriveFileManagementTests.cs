using System;
using System.Collections.Generic;
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
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Tests.AppAPI.Utils;
using Refit;
using QueryModifiedRequest = Odin.Services.Drives.QueryModifiedRequest;

namespace Odin.Hosting.Tests.AppAPI.Drive
{
    public class DriveFileManagementTests
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

        [Test(Description = "Test Upload only; no expire, no drive; no transfer")]
        public async Task UploadOnly()
        {
            var identity = TestIdentities.Frodo;

            var testContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(identity);

            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            var keyHeader = KeyHeader.NewRandom16();

            const string payloadKey = WebScaffold.PAYLOAD_KEY;
            var payloadIv = ByteArrayUtil.GetRndByteArray(16);

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
                        new ()
                        {
                            Iv = payloadIv,
                            PayloadKey = payloadKey
                        }
                    }
                }
            };

            var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var sba = testContext.SharedSecret.ToSensitiveByteArray();
            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sba),
                FileMetadata = new()
                {
                    AllowDistribution = false,
                    IsEncrypted = true,
                    AppData = new()
                    {
                        Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                        Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" })
                    }
                },
            };

            var key = testContext.SharedSecret.ToSensitiveByteArray();
            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref key);

            var payloadDataRaw = "{payload:true, image:'b64 data'}";
            var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadDataRaw);

            var client = _scaffold.AppApi.CreateAppApiHttpClient(testContext);
            {
                var svc = RestService.For<IDriveTestHttpClientForApps>(client);
                var response = await svc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, payloadKey, "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var uploadResult = response.Content;

                Assert.That(uploadResult.File, Is.Not.Null);
                Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                ClassicAssert.IsTrue(uploadResult.File.TargetDrive.IsValid());

                // Assert.That(uploadResult.RecipientStatus, Is.Not.Null);
                // ClassicAssert.IsTrue(uploadResult.RecipientStatus.Count == 0, "Too many recipient results returned");

                //

                ////
                var targetDrive = uploadResult.File.TargetDrive;
                var fileId = uploadResult.File.FileId;

                //retrieve the file that was uploaded; decrypt; 
                var driveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, testContext.SharedSecret);

                var fileResponse = await driveSvc.GetFileHeaderAsPost(new ExternalFileIdentifier() { TargetDrive = targetDrive, FileId = fileId });

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

                var decryptedKeyHeader = clientFileHeader.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref key);

                Assert.That(decryptedKeyHeader.AesKey.IsSet(), Is.True);
                var fileKey = decryptedKeyHeader.AesKey;
                Assert.That(fileKey, Is.Not.EqualTo(Guid.Empty.ToByteArray()));

                //get the payload and decrypt, then compare
                var payloadResponse = await driveSvc.GetPayloadAsPost(new GetPayloadRequest()
                {
                    Key = payloadKey,
                    File = new ExternalFileIdentifier() { TargetDrive = targetDrive, FileId = fileId }
                });

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

                var decryptedPayloadRaw = System.Text.Encoding.UTF8.GetString(decryptedPayloadBytes);

                decryptedKeyHeader.AesKey.Wipe();
            }

            keyHeader.AesKey.Wipe();
            key.Wipe();
        }

        [Test(Description = "")]
        public async Task CanSoftDeleteFile()
        {
            int SomeFileType = 194392901;

            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = false,
                IsEncrypted = true,
                AppData = new()
                {
                    FileType = SomeFileType,
                    Content = "{some:'file content'}",
                },
                AccessControlList = AccessControlList.OwnerOnly
            };

            var options = new TransitTestUtilsOptions()
            {
                PayloadData = "this will be deleted",
                IncludeThumbnail = true
            };

            AppTransitTestUtilsContext ctx = await _scaffold.AppApi.CreateAppAndUploadFileMetadata(TestIdentities.Frodo, fileMetadata, options);

            ClassicAssert.IsNotNull(ctx.Thumbnails.SingleOrDefault());
            var appContext = ctx.TestAppContext;
            var fileToDelete = ctx.UploadedFile;

            var client = _scaffold.AppApi.CreateAppApiHttpClient(appContext);
            {
                var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, appContext.SharedSecret);

                //validate the file is in the index
                var fileIsInIndexResponse = await svc.GetBatch(new QueryBatchRequest()
                {
                    QueryParams = FileQueryParams.FromFileType(appContext.TargetDrive, SomeFileType),
                    ResultOptionsRequest = new QueryBatchResultOptionsRequest()
                    {
                        MaxRecords = 10
                    }
                });

                ClassicAssert.IsTrue(fileIsInIndexResponse?.Content?.SearchResults?.SingleOrDefault()?.FileMetadata?.AppData?.FileType == SomeFileType);

                // delete the file
                var deleteFileResponse = await svc.DeleteFile(new DeleteFileRequest() { File = fileToDelete });
                ClassicAssert.IsTrue(deleteFileResponse.IsSuccessStatusCode);
                ClassicAssert.IsNotNull(deleteFileResponse.Content);
                ClassicAssert.IsTrue(deleteFileResponse.Content.LocalFileDeleted);

                //
                // Should still be in index
                //
                var qbResponse = await svc.GetBatch(new QueryBatchRequest()
                {
                    QueryParams = FileQueryParams.FromFileType(appContext.TargetDrive),
                    ResultOptionsRequest = new QueryBatchResultOptionsRequest()
                    {
                        MaxRecords = 10
                    }
                });

                ClassicAssert.IsTrue(qbResponse.IsSuccessStatusCode);
                ClassicAssert.IsNotNull(qbResponse.Content);
                var qbDeleteFileEntry = qbResponse.Content.SearchResults.SingleOrDefault();
                OdinTestAssertions.FileHeaderIsMarkedDeleted(qbDeleteFileEntry);

                // crucial - it should return in query modified so apps can sync locally
                var queryModifiedResponse = await svc.GetModified(new QueryModifiedRequest()
                {
                    QueryParams = FileQueryParams.FromFileType(appContext.TargetDrive, SomeFileType),
                    ResultOptions = new QueryModifiedResultOptions()
                    {
                        MaxRecords = 10
                    }
                });

                ClassicAssert.IsTrue(queryModifiedResponse.IsSuccessStatusCode);
                ClassicAssert.IsNotNull(queryModifiedResponse.Content);
                var queryModifiedDeletedEntry = qbResponse.Content.SearchResults.SingleOrDefault();
                ClassicAssert.IsNotNull(queryModifiedDeletedEntry);
                OdinTestAssertions.FileHeaderIsMarkedDeleted(queryModifiedDeletedEntry);

                // get file directly
                var getFileHeaderResponse = await svc.GetFileHeaderAsPost(fileToDelete);
                ClassicAssert.IsTrue(getFileHeaderResponse.IsSuccessStatusCode);
                ClassicAssert.IsNotNull(getFileHeaderResponse.Content);
                var deletedFileHeader = getFileHeaderResponse.Content;
                OdinTestAssertions.FileHeaderIsMarkedDeleted(deletedFileHeader);

                //there should not be a thumbnail
                var thumb = ctx.Thumbnails.FirstOrDefault();
                var getThumbnailResponse = await svc.GetThumbnailAsPost(new GetThumbnailRequest()
                {
                    File = fileToDelete,
                    Height = thumb.PixelHeight,
                    Width = thumb.PixelWidth,
                    PayloadKey = WebScaffold.PAYLOAD_KEY
                });
                ClassicAssert.IsTrue(getThumbnailResponse.StatusCode == HttpStatusCode.NotFound, $"code was {getThumbnailResponse.StatusCode}");

                //there should not be a payload
                var getPayloadResponse = await svc.GetPayloadAsPost(new GetPayloadRequest() { File = fileToDelete, Key = WebScaffold.PAYLOAD_KEY });
                ClassicAssert.IsTrue(getPayloadResponse.StatusCode == HttpStatusCode.NotFound);
            }
        }


        [Test(Description = "")]
        [Ignore("There is no api exposed for hard-delete for an App.")]
        public void CanHardDeleteFile()
        {
        }
    }
}