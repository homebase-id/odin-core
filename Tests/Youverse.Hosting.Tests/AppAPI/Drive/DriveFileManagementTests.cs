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
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Tests.AppAPI.Utils;

namespace Youverse.Hosting.Tests.AppAPI.Drive
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

            var testContext = await _scaffold.OwnerApi.SetupTestSampleApp(identity);

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

            var sba = testContext.SharedSecret.ToSensitiveByteArray();
            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sba),
                FileMetadata = new()
                {
                    ContentType = "application/json",
                    PayloadIsEncrypted = true,
                    AppData = new()
                    {
                        Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                        ContentIsComplete = true,
                        JsonContent = DotYouSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" })
                    }
                },
            };

            var key = testContext.SharedSecret.ToSensitiveByteArray();
            var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, transferIv, ref key);

            var payloadDataRaw = "{payload:true, image:'b64 data'}";
            var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadDataRaw);

            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(identity.DotYouId, testContext.ClientAuthenticationToken))
            {
                var transitSvc = RestService.For<IDriveTestHttpClientForApps>(client);
                var response = await transitSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

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

                var fileResponse = await driveSvc.GetFileHeader(new ExternalFileIdentifier() { TargetDrive = targetDrive, FileId = fileId });

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

                var decryptedKeyHeader = clientFileHeader.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref key);

                Assert.That(decryptedKeyHeader.AesKey.IsSet(), Is.True);
                var fileKey = decryptedKeyHeader.AesKey;
                Assert.That(fileKey, Is.Not.EqualTo(Guid.Empty.ToByteArray()));

                //get the payload and decrypt, then compare
                var payloadResponse = await driveSvc.GetPayload(new ExternalFileIdentifier() { TargetDrive = targetDrive, FileId = fileId });
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
                ContentType = "application/json",
                PayloadIsEncrypted = true,
                AppData = new()
                {
                    FileType = SomeFileType,
                    JsonContent = "{some:'file content'}",
                },
                AccessControlList = AccessControlList.NewOwnerOnly
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

            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(appContext.Identity, appContext.ClientAuthenticationToken))
            {
                var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, appContext.SharedSecret);
                
                //validate the file is in the index
                var fileIsInIndexResponse = await svc.QueryBatch(new QueryBatchRequest()
                {
                    QueryParams = FileQueryParams.FromFileType(appContext.TargetDrive, SomeFileType),
                    ResultOptionsRequest = new QueryBatchResultOptionsRequest()
                    {
                        MaxRecords = 10
                    }
                });

                Assert.IsTrue(fileIsInIndexResponse?.Content?.SearchResults?.SingleOrDefault()?.FileMetadata?.AppData?.FileType == SomeFileType);
                
                // delete the file
                var deleteFileResponse = await svc.DeleteFile(fileToDelete);
                Assert.IsTrue(deleteFileResponse.IsSuccessStatusCode);
                Assert.IsTrue(deleteFileResponse.Content);

                //
                // Should still be in index
                //
                
                var qbResponse = await svc.QueryBatch(new QueryBatchRequest()
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
                AssertFileHeaderIsMarkedDeleted(qbDeleteFileEntry);

                // crucial - it should return in query modified so apps can sync locally
                var queryModifiedResponse = await svc.QueryModified(new QueryModifiedRequest()
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
                AssertFileHeaderIsMarkedDeleted(queryModifiedDeletedEntry);

                // get file directly
                var getFileHeaderResponse = await svc.GetFileHeader(fileToDelete);
                Assert.IsTrue(getFileHeaderResponse.IsSuccessStatusCode);
                Assert.IsNotNull(getFileHeaderResponse.Content);
                var deletedFileHeader = getFileHeaderResponse.Content;
                AssertFileHeaderIsMarkedDeleted(deletedFileHeader);

                //there should not be a thumbnail
                var thumb = ctx.Thumbnails.Single();
                var getThumbnailResponse = await svc.GetThumbnail(new GetThumbnailRequest()
                {
                    File = fileToDelete,
                    Height = thumb.PixelHeight,
                    Width = thumb.PixelWidth
                });
                Assert.IsTrue(getThumbnailResponse.StatusCode == HttpStatusCode.NotFound);

                //there should not be a payload
                var getPayloadResponse = await svc.GetPayload(fileToDelete);
                Assert.IsTrue(getPayloadResponse.StatusCode == HttpStatusCode.NotFound);
            }
        }

        private void AssertFileHeaderIsMarkedDeleted(ClientFileHeader fileHeader)
        {
            Assert.IsTrue(fileHeader.FileId != Guid.Empty);
            Assert.IsNotNull(fileHeader.ServerMetadata.AccessControlList);
            Assert.IsTrue(fileHeader.ServerMetadata.AccessControlList.RequiredSecurityGroup == SecurityGroupType.Owner);
            Assert.IsNull(fileHeader.FileMetadata.GlobalTransitId); //TODO: we just uploaded a file in this case
            Assert.IsTrue(fileHeader.FileMetadata.Updated > 0);
            Assert.IsTrue(fileHeader.FileMetadata.Created == default);
            Assert.IsTrue(fileHeader.FileMetadata.PayloadSize == default);
            Assert.IsTrue(string.IsNullOrEmpty(fileHeader.FileMetadata.ContentType));
            Assert.IsTrue(string.IsNullOrEmpty(fileHeader.FileMetadata.SenderDotYouId));
            Assert.IsTrue(fileHeader.FileMetadata.OriginalRecipientList == null);
            Assert.IsTrue(fileHeader.FileMetadata.PayloadIsEncrypted == default);
            
            Assert.IsNotNull(fileHeader.FileMetadata.AppData);
            Assert.IsTrue(fileHeader.FileMetadata.AppData.ContentIsComplete == default);
            Assert.IsTrue(fileHeader.FileMetadata.AppData.AdditionalThumbnails == default);
            Assert.IsTrue(fileHeader.FileMetadata.AppData.DataType == default);
            Assert.IsTrue(fileHeader.FileMetadata.AppData.FileType == default);
            Assert.IsTrue(fileHeader.FileMetadata.AppData.GroupId == default);
            Assert.IsTrue(string.IsNullOrEmpty(fileHeader.FileMetadata.AppData.JsonContent));
            Assert.IsTrue(fileHeader.FileMetadata.AppData.PreviewThumbnail == default);
            Assert.IsTrue(fileHeader.FileMetadata.AppData.UserDate == default);
            Assert.IsTrue(fileHeader.FileMetadata.AppData.Tags == default);



        }

        [Test(Description = "")]
        [Ignore("There is no api exposed for hard-delete.  ")]
        public async Task CanHardDeleteFile()
        {
        }
    }
}