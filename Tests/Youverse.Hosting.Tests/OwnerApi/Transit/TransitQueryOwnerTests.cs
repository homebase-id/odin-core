using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.ClientToken.Transit;
using Youverse.Hosting.Tests.AppAPI;
using Youverse.Hosting.Tests.AppAPI.Drive;
using Youverse.Hosting.Tests.AppAPI.Transit;
using Youverse.Hosting.Tests.OwnerApi.Drive;
using Youverse.Hosting.Tests.OwnerApi.Utils;

namespace Youverse.Hosting.Tests.OwnerApi.Transit
{
    public class TransitQueryOwnerTests
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
        public async Task CanSendTransferAndRecipientCanGetFilesByTag()
        {
            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            Guid appId = Guid.NewGuid();
            var targetDrive = TargetDrive.NewTargetDrive();
            var testContext = await _scaffold.OwnerApi.SetupTestSampleApp(appId, sender, false, targetDrive, driveAllowAnonymousReads: true);
            var recipientContext = await _scaffold.OwnerApi.SetupTestSampleApp(testContext.AppId, recipient, false, targetDrive);

            Guid fileTag = Guid.NewGuid();

            var senderCircleDef =
                await _scaffold.OwnerApi.CreateCircleWithDrive(sender.DotYouId, "Sender Circle",
                    permissionKeys: new List<int>() { },
                    drive: new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.ReadWrite
                    });

            var recipientCircleDef =
                await _scaffold.OwnerApi.CreateCircleWithDrive(recipient.DotYouId, "Recipient Circle",
                    permissionKeys: new List<int>() { },
                    drive: new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.ReadWrite
                    });

            await _scaffold.OwnerApi.CreateConnection(sender.DotYouId, recipient.DotYouId,
                createConnectionOptions: new CreateConnectionOptions()
                {
                    CircleIdsGrantedToRecipient = new List<GuidId>() { senderCircleDef.Id },
                    CircleIdsGrantedToSender = new List<GuidId>() { recipientCircleDef.Id }
                });

            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            var keyHeader = KeyHeader.NewRandom16();

            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = transferIv,
                StorageOptions = new StorageOptions()
                {
                    Drive = testContext.TargetDrive,
                    OverwriteFileId = null
                },

                TransitOptions = new TransitOptions()
                {
                    Recipients = new List<string>() { recipient.DotYouId }
                }
            };

            var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var key = testContext.SharedSecret.ToSensitiveByteArray();
            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref key),
                FileMetadata = new()
                {
                    ContentType = "application/json",
                    AppData = new()
                    {
                        Tags = new List<Guid>() { fileTag },
                        ContentIsComplete = true,
                        JsonContent = DotYouSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
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
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

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

            ExternalFileIdentifier uploadedFile;
            var fileTagQueryParams = new FileQueryParams()
            {
                TargetDrive = recipientContext.TargetDrive,
                TagsMatchAll = new List<Guid>() { fileTag }
            };


            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(recipient, recipientContext.ClientAuthenticationToken))
            {
                //First force transfers to be put into their long term location
                var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(client);
                // client.DefaultRequestHeaders.Add("SY4829", Guid.Parse("a1224889-c0b1-4298-9415-76332a9af80e").ToString());
                var resp = await transitAppSvc.ProcessIncomingInstructions(new ProcessTransitInstructionRequest() { TargetDrive = recipientContext.TargetDrive });
                Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);

                var driveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, recipientContext.SharedSecret);

                //lookup the fileId by the fileTag from earlier

                var queryBatchResponse = await driveSvc.QueryBatch(new QueryBatchRequest()
                {
                    QueryParams = fileTagQueryParams,
                    ResultOptionsRequest = new QueryBatchResultOptionsRequest()
                    {
                        MaxRecords = 1,
                        IncludeMetadataHeader = true
                    }
                });

                Assert.IsTrue(queryBatchResponse.IsSuccessStatusCode);
                Assert.IsNotNull(queryBatchResponse.Content);
                Assert.IsTrue(queryBatchResponse.Content.SearchResults.Count() == 1);

                uploadedFile = new ExternalFileIdentifier()
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

                decryptedKeyHeader.AesKey.Wipe();
                keyHeader.AesKey.Wipe();
            }


            // reupload the file on the recipient's identity and set the filemetadata to authenticated.
            // this is done because when files are received, they are set to owner only on the recipients identity
            instructionSet.StorageOptions = new StorageOptions()
            {
                OverwriteFileId = uploadedFile.FileId,
                Drive = uploadedFile.TargetDrive
            };

            instructionSet.TransitOptions = null;
            
            await _scaffold.OwnerApi.UploadFile(recipient.DotYouId, instructionSet, descriptor.FileMetadata, payloadData, true);


            //
            //  The final test - use transit query batch for the sender to get the file on the recipients identity over transit
            //
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(sender, out var sharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ITransitQueryHttpClientForOwner>(client, sharedSecret);

                var queryBatchResponse = await svc.GetBatch(new TransitQueryBatchRequest()
                {
                    DotYouId = recipient.DotYouId,
                    QueryParams = fileTagQueryParams,
                    ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
                });

                Assert.IsTrue(queryBatchResponse.IsSuccessStatusCode);
                Assert.IsNotNull(queryBatchResponse.Content);
                Assert.IsTrue(queryBatchResponse.Content.SearchResults.Count() == 1);

                var fileResponse = queryBatchResponse.Content.SearchResults.Single();
                Assert.IsTrue(uploadedFile.FileId == fileResponse.FileId);
            }

            keyHeader.AesKey.Wipe();
            key.Wipe();

            await _scaffold.OwnerApi.DisconnectIdentities(sender.DotYouId, recipientContext.Identity);
        }

        [Test]
        [Ignore("need to implement transit query modified items")]
        public async Task CanQueryDriveModifiedItems()
        {
            var identity = TestIdentities.Samwise;

            var uploadFileMetadata = new UploadFileMetadata()
            {
                ContentType = "application/json",
                PayloadIsEncrypted = false,
                AppData = new()
                {
                    ContentIsComplete = false,
                    JsonContent = DotYouSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    FileType = 100,
                    DataType = 202,
                    UserDate = 0
                }
            };

            TransitTestUtilsOptions options = new TransitTestUtilsOptions()
            {
                PayloadData = "some payload data for good measure",
                ProcessOutbox = false,
                ProcessTransitBox = false,
                DisconnectIdentitiesAfterTransfer = true
            };

            var uploadContext = await _scaffold.OwnerApi.Upload(identity.DotYouId, uploadFileMetadata, options);


            //
            // make a change to the file we just uploaded
            //

            var instructionSet = UploadInstructionSet.WithTargetDrive(uploadContext.UploadedFile.TargetDrive);
            instructionSet.StorageOptions.OverwriteFileId = uploadContext.UploadedFile.FileId;

            uploadFileMetadata.AppData.DataType = 10844;
            var _ = await _scaffold.OwnerApi.UploadFile(identity.DotYouId, instructionSet, uploadFileMetadata, "a new payload", true);

            //
            // query the data to see the changes
            //
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, ownerSharedSecret);

                var qp = new FileQueryParams()
                {
                    TargetDrive = uploadContext.UploadedFile.TargetDrive
                };

                var resultOptions = new QueryModifiedResultOptions()
                {
                    IncludeJsonContent = true,
                    Cursor = 0,
                    MaxRecords = 10,
                };

                var request = new QueryModifiedRequest()
                {
                    QueryParams = qp,
                    ResultOptions = resultOptions,
                };

                var response = await svc.GetModified(request);
                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var batch = response.Content;
                Assert.IsNotNull(batch);

                //TODO: what to test here?
                Assert.IsTrue(batch.SearchResults.Any());

                var firstResult = batch.SearchResults.First();

                //ensure file content was sent 
                Assert.NotNull(firstResult.FileMetadata.AppData.JsonContent);
                Assert.IsNotEmpty(firstResult.FileMetadata.AppData.JsonContent);

                Assert.IsTrue(firstResult.FileMetadata.AppData.FileType == uploadFileMetadata.AppData.FileType);
                Assert.IsTrue(firstResult.FileMetadata.AppData.DataType == uploadFileMetadata.AppData.DataType);
                Assert.IsTrue(firstResult.FileMetadata.AppData.UserDate == uploadFileMetadata.AppData.UserDate);
                Assert.IsTrue(firstResult.FileMetadata.ContentType == uploadFileMetadata.ContentType);
                Assert.IsTrue(string.IsNullOrEmpty(firstResult.FileMetadata.SenderDotYouId));

                //must be ordered correctly
                //TODO: How to test this with a fileId?
            }
        }

        [Test]
        [Ignore("need to implement transit query modified items")]
        public async Task CanQueryDriveModifiedItemsRedactedContent()
        {
            var identity = TestIdentities.Samwise;

            var uploadFileMetadata = new UploadFileMetadata()
            {
                ContentType = "application/json",
                PayloadIsEncrypted = false,
                AppData = new()
                {
                    ContentIsComplete = false,
                    JsonContent = DotYouSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    FileType = 100,
                    DataType = 202,
                    UserDate = 0
                }
            };

            TransitTestUtilsOptions options = new TransitTestUtilsOptions()
            {
                PayloadData = "some payload data for good measure",
                ProcessOutbox = false,
                ProcessTransitBox = false,
                DisconnectIdentitiesAfterTransfer = true
            };

            var uploadContext = await _scaffold.OwnerApi.Upload(identity.DotYouId, uploadFileMetadata, options);

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, ownerSharedSecret);

                var qp = new FileQueryParams()
                {
                    TargetDrive = uploadContext.UploadedFile.TargetDrive
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

                var response = await svc.GetBatch(request);
                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var batch = response.Content;
                Assert.IsNotNull(batch);
                Assert.IsTrue(batch.SearchResults.Any(), "No items returned");
                Assert.IsTrue(batch.SearchResults.All(item => string.IsNullOrEmpty(item.FileMetadata.AppData.JsonContent)), "One or more items had content");
            }
        }
    }
}