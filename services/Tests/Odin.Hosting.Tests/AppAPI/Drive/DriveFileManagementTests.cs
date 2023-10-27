﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Encryption;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Tests.AppAPI.Utils;
using Refit;
using QueryModifiedRequest = Odin.Core.Services.Drives.QueryModifiedRequest;

namespace Odin.Hosting.Tests.AppAPI.Drive
{
    public class DriveFileManagementTests
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

        [Test(Description = "Test Upload only; no expire, no drive; no transfer")]
        public async Task UploadOnly()
        {
            var identity = TestIdentities.Frodo;

            var testContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(identity);

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

            var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var sba = testContext.SharedSecret.ToSensitiveByteArray();
            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sba),
                FileMetadata = new()
                {
                    AllowDistribution = false,
                    PayloadIsEncrypted = true,
                    AppData = new()
                    {
                        Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                        JsonContent = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" })
                    }
                },
            };

            var key = testContext.SharedSecret.ToSensitiveByteArray();
            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref key);

            var payloadDataRaw = "{payload:true, image:'b64 data'}";
            var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadDataRaw);

            const string payloadKey = "xx";
            var client = _scaffold.AppApi.CreateAppApiHttpClient(testContext);
            {
                var transitSvc = RestService.For<IDriveTestHttpClientForApps>(client);
                var response = await transitSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, payloadKey, "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var uploadResult = response.Content;

                Assert.That(uploadResult.File, Is.Not.Null);
                Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                Assert.IsTrue(uploadResult.File.TargetDrive.IsValid());

                // Assert.That(uploadResult.RecipientStatus, Is.Not.Null);
                // Assert.IsTrue(uploadResult.RecipientStatus.Count == 0, "Too many recipient results returned");

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
                Assert.That(clientFileHeader.FileMetadata.AppData.JsonContent, Is.EqualTo(descriptor.FileMetadata.AppData.JsonContent));
                Assert.IsTrue(clientFileHeader.FileMetadata.Payloads.Count == 1);
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
                    Key: ref aesKey,
                    IV: decryptedKeyHeader.Iv);

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
                PayloadIsEncrypted = true,
                AppData = new()
                {
                    FileType = SomeFileType,
                    JsonContent = "{some:'file content'}",
                },
                AccessControlList = AccessControlList.OwnerOnly
            };

            var options = new TransitTestUtilsOptions()
            {
                PayloadData = "this will be deleted",
                IncludeThumbnail = true
            };

            AppTransitTestUtilsContext ctx = await _scaffold.AppApi.CreateAppAndUploadFileMetadata(TestIdentities.Frodo, fileMetadata, options);

            Assert.IsNotNull(ctx.Thumbnails.SingleOrDefault());
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

                Assert.IsTrue(fileIsInIndexResponse?.Content?.SearchResults?.SingleOrDefault()?.FileMetadata?.AppData?.FileType == SomeFileType);

                // delete the file
                var deleteFileResponse = await svc.DeleteFile(new DeleteFileRequest() { File = fileToDelete });
                Assert.IsTrue(deleteFileResponse.IsSuccessStatusCode);
                Assert.IsNotNull(deleteFileResponse.Content);
                Assert.IsTrue(deleteFileResponse.Content.LocalFileDeleted);

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

                Assert.IsTrue(qbResponse.IsSuccessStatusCode);
                Assert.IsNotNull(qbResponse.Content);
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

                Assert.IsTrue(queryModifiedResponse.IsSuccessStatusCode);
                Assert.IsNotNull(queryModifiedResponse.Content);
                var queryModifiedDeletedEntry = qbResponse.Content.SearchResults.SingleOrDefault();
                Assert.IsNotNull(queryModifiedDeletedEntry);
                OdinTestAssertions.FileHeaderIsMarkedDeleted(queryModifiedDeletedEntry);

                // get file directly
                var getFileHeaderResponse = await svc.GetFileHeaderAsPost(fileToDelete);
                Assert.IsTrue(getFileHeaderResponse.IsSuccessStatusCode);
                Assert.IsNotNull(getFileHeaderResponse.Content);
                var deletedFileHeader = getFileHeaderResponse.Content;
                OdinTestAssertions.FileHeaderIsMarkedDeleted(deletedFileHeader);

                //there should not be a thumbnail
                var thumb = ctx.Thumbnails.FirstOrDefault();
                var getThumbnailResponse = await svc.GetThumbnailAsPost(new GetThumbnailRequest()
                {
                    File = fileToDelete,
                    Height = thumb.PixelHeight,
                    Width = thumb.PixelWidth
                });
                Assert.IsTrue(getThumbnailResponse.StatusCode == HttpStatusCode.NotFound);

                //there should not be a payload
                var getPayloadResponse = await svc.GetPayloadAsPost(new GetPayloadRequest() { File = fileToDelete, Key = WebScaffold.PAYLOAD_KEY });
                Assert.IsTrue(getPayloadResponse.StatusCode == HttpStatusCode.NotFound);
            }
        }


        [Test(Description = "")]
        [Ignore("There is no api exposed for hard-delete for an App.")]
        public void CanHardDeleteFile()
        {
        }
    }
}