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
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.DriveCore.Query;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Core.Storage;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.ClientToken.Transit;
using Youverse.Hosting.Controllers.OwnerToken.Transit;
using Youverse.Hosting.Tests.AppAPI.Drive;
using Youverse.Hosting.Tests.AppAPI.Transit;
using Youverse.Hosting.Tests.AppAPI.Utils;
using Youverse.Hosting.Tests.OwnerApi.Utils;

namespace Youverse.Hosting.Tests.OwnerApi.Transit.Query
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
        public async Task CanTransferStandardFileAndRecipientCanQueryFilesByTag()
        {
            var sender = TestIdentities.Samwise;
            var recipient = TestIdentities.Pippin;

            Guid appId = Guid.NewGuid();
            var targetDrive = TargetDrive.NewTargetDrive();
            var senderContext =
                await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, sender, canReadConnections: true, targetDrive, driveAllowAnonymousReads: true);
            var recipientContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(senderContext.AppId, recipient, canReadConnections: true, targetDrive);

            Guid fileTag = Guid.NewGuid();

            var senderCircleDef =
                await _scaffold.OldOwnerApi.CreateCircleWithDrive(sender.OdinId, "Sender Circle",
                    permissionKeys: new List<int>() { },
                    drive: new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.ReadWrite
                    });

            var recipientCircleDef =
                await _scaffold.OldOwnerApi.CreateCircleWithDrive(recipient.OdinId, "Recipient Circle",
                    permissionKeys: new List<int>() { },
                    drive: new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.ReadWrite
                    });

            await _scaffold.OldOwnerApi.CreateConnection(sender.OdinId, recipient.OdinId,
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
                    Drive = senderContext.TargetDrive,
                    OverwriteFileId = null
                },

                TransitOptions = new TransitOptions()
                {
                    Recipients = new List<string>() { recipient.OdinId },
                    UseGlobalTransitId = true
                }
            };

            var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var key = senderContext.SharedSecret.ToSensitiveByteArray();
            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref key),
                FileMetadata = new()
                {
                    ContentType = "application/json",
                    AllowDistribution = true,
                    AppData = new()
                    {
                        Tags = new List<Guid>() { fileTag },
                        ContentIsComplete = false,
                        JsonContent = DotYouSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    },
                    PayloadIsEncrypted = true,
                    AccessControlList = new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected }
                },
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref key);

            var payloadData = "{payload:true, image:'b64 data'}";
            var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadData);

            //
            // upload and send the file 
            //
            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(senderContext))
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

            await _scaffold.OldOwnerApi.ProcessOutbox(sender.OdinId);

            ExternalFileIdentifier uploadedFile;
            var fileTagQueryParams = new FileQueryParams()
            {
                TargetDrive = recipientContext.TargetDrive,
                TagsMatchAll = new List<Guid>() { fileTag }
            };


            //
            // validate recipient got the file
            //
            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(recipientContext))
            {
                //First force transfers to be put into their long term location
                var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(client);
                var resp = await transitAppSvc.ProcessIncomingInstructions(
                    new ProcessTransitInstructionRequest() { TargetDrive = recipientContext.TargetDrive });
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

                var fileResponse = await driveSvc.GetFileHeaderAsPost(uploadedFile);

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

                var payloadResponse = await driveSvc.GetPayloadAsPost(uploadedFile);
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

            descriptor.FileMetadata.AllowDistribution = false;
            instructionSet.TransitOptions = null;

            await _scaffold.OldOwnerApi.UploadFile(recipient.OdinId, instructionSet, descriptor.FileMetadata, payloadData, true);


            //
            //  The final test - use transit query batch for the sender to get the file on the recipients identity over transit
            //
            using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sender, out var sharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ITransitQueryHttpClientForOwner>(client, sharedSecret);

                var queryBatchResponse = await svc.GetBatch(new TransitQueryBatchRequest()
                {
                    OdinId = recipient.OdinId,
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

            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipientContext.Identity);
        }

        [Test]
        public async Task CanTransferStandardFileAndRecipientCanGetFileHeaderThumbAndPayload()
        {
            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            Guid appId = Guid.NewGuid();
            var targetDrive = TargetDrive.NewTargetDrive();
            var senderContext =
                await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, sender, canReadConnections: true, targetDrive, driveAllowAnonymousReads: true);
            var recipientContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(senderContext.AppId, recipient, canReadConnections: true, targetDrive);

            Guid fileTag = Guid.NewGuid();

            var senderCircleDef =
                await _scaffold.OldOwnerApi.CreateCircleWithDrive(sender.OdinId, "Sender Circle",
                    permissionKeys: new List<int>() { },
                    drive: new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.ReadWrite
                    });

            var recipientCircleDef =
                await _scaffold.OldOwnerApi.CreateCircleWithDrive(recipient.OdinId, "Recipient Circle",
                    permissionKeys: new List<int>() { },
                    drive: new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.ReadWrite
                    });

            await _scaffold.OldOwnerApi.CreateConnection(sender.OdinId, recipient.OdinId,
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
                    Drive = senderContext.TargetDrive,
                    OverwriteFileId = null
                },

                TransitOptions = new TransitOptions()
                {
                    Recipients = new List<string>() { recipient.OdinId },
                    UseGlobalTransitId = true
                }
            };

            var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var key = senderContext.SharedSecret.ToSensitiveByteArray();
            var json = DotYouSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" });
            var encryptedJsonContent64 = keyHeader.EncryptDataAesAsStream(json).ToByteArray().ToBase64();

            var thumbnail1 = new ImageDataHeader()
            {
                PixelHeight = 300,
                PixelWidth = 300,
                ContentType = "image/jpeg"
            };
            var thumbnail1OriginalBytes = TestMedia.ThumbnailBytes300;
            var thumbnail1CipherBytes = keyHeader.EncryptDataAes(thumbnail1OriginalBytes);

            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref key),
                FileMetadata = new()
                {
                    ContentType = "application/json",
                    AllowDistribution = true,
                    AppData = new()
                    {
                        Tags = new List<Guid>() { fileTag },
                        ContentIsComplete = false,
                        JsonContent = encryptedJsonContent64,
                        AdditionalThumbnails = new[] { thumbnail1 }
                    },
                    PayloadIsEncrypted = true,
                    AccessControlList = new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected }
                },
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref key);

            var originalPayloadData = "{payload:true, image:'b64 data'}";
            var originalPayloadCipherBytes = keyHeader.EncryptDataAesAsStream(originalPayloadData);

            //
            // upload and send the file 
            //
            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(senderContext))
            {
                var transitSvc = RestService.For<IDriveTestHttpClientForApps>(client);
                var response = await transitSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(originalPayloadCipherBytes, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)),
                    new StreamPart(new MemoryStream(thumbnail1CipherBytes), thumbnail1.GetFilename(), thumbnail1.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail)));

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

            await _scaffold.OldOwnerApi.ProcessOutbox(sender.OdinId);

            ExternalFileIdentifier uploadedFile;
            var fileTagQueryParams = new FileQueryParams()
            {
                TargetDrive = recipientContext.TargetDrive,
                TagsMatchAll = new List<Guid>() { fileTag }
            };

            //
            // Validate recipient got the file
            //
            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(recipientContext))
            {
                //First force transfers to be put into their long term location
                var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(client);
                var resp = await transitAppSvc.ProcessIncomingInstructions(
                    new ProcessTransitInstructionRequest() { TargetDrive = recipientContext.TargetDrive });
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

                var fileResponse = await driveSvc.GetFileHeaderAsPost(uploadedFile);

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

                var payloadResponse = await driveSvc.GetPayloadAsPost(uploadedFile);
                Assert.That(payloadResponse.IsSuccessStatusCode, Is.True);
                Assert.That(payloadResponse.Content, Is.Not.Null);

                var payloadResponseCipher = await payloadResponse.Content.ReadAsByteArrayAsync();
                Assert.That(((MemoryStream)originalPayloadCipherBytes).ToArray(), Is.EqualTo(payloadResponseCipher));

                var aesKey = decryptedKeyHeader.AesKey;
                var decryptedPayloadBytes = Core.Cryptography.Crypto.AesCbc.Decrypt(
                    cipherText: payloadResponseCipher,
                    Key: ref aesKey,
                    IV: decryptedKeyHeader.Iv);

                var payloadBytes = System.Text.Encoding.UTF8.GetBytes(originalPayloadData);
                Assert.That(payloadBytes, Is.EqualTo(decryptedPayloadBytes));

                // var decryptedPayloadRaw = System.Text.Encoding.UTF8.GetString(decryptedPayloadBytes);

                var getThumbnailResponse = await driveSvc.GetThumbnailAsPost(new GetThumbnailRequest()
                {
                    File = uploadedFile,
                    Width = thumbnail1.PixelWidth,
                    Height = thumbnail1.PixelHeight
                });

                Assert.IsTrue(getThumbnailResponse.IsSuccessStatusCode);
                var getThumbnailResponseBytes = await getThumbnailResponse.Content!.ReadAsByteArrayAsync();
                Assert.IsNotNull(thumbnail1CipherBytes.Length == getThumbnailResponseBytes.Length);

                decryptedKeyHeader.AesKey.Wipe();
                // keyHeader.AesKey.Wipe();
            }


            // reupload the file on the recipient's identity and set the filemetadata to authenticated.
            // this is done because when files are received, they are set to owner only on the recipients identity
            instructionSet.StorageOptions = new StorageOptions()
            {
                OverwriteFileId = uploadedFile.FileId,
                Drive = uploadedFile.TargetDrive
            };

            instructionSet.TransitOptions = null;
            descriptor.FileMetadata.AllowDistribution = false;

            var reuploadedContext = await _scaffold.OldOwnerApi.UploadFile(recipient.OdinId, instructionSet, descriptor.FileMetadata, originalPayloadData, true,
                new ImageDataContent()
                {
                    ContentType = thumbnail1.ContentType,
                    PixelHeight = thumbnail1.PixelHeight,
                    PixelWidth = thumbnail1.PixelWidth,
                    Content = thumbnail1OriginalBytes
                }, keyHeader);

            //
            //  The final test - use transit query batch for the sender to get the file on the recipients identity over transit
            //
            using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sender, out var ownerSharedSecret))
            {
                var transitQueryService = RefitCreator.RestServiceFor<ITransitQueryHttpClientForOwner>(client, ownerSharedSecret);

                //
                // Test the file matches the original one sent
                //
                var getTransitFileResponse = await transitQueryService.GetFileHeader(new TransitExternalFileIdentifier()
                {
                    OdinId = recipient.OdinId,
                    File = uploadedFile
                });

                Assert.IsTrue(getTransitFileResponse.IsSuccessStatusCode);
                Assert.IsNotNull(getTransitFileResponse.Content);

                var fileResponse = getTransitFileResponse.Content;
                Assert.IsTrue(uploadedFile.FileId == fileResponse.FileId);

                var transitClientFileHeader = getTransitFileResponse.Content;

                Assert.That(transitClientFileHeader.FileMetadata, Is.Not.Null);
                Assert.That(transitClientFileHeader.FileMetadata.AppData, Is.Not.Null);

                Assert.That(transitClientFileHeader.FileMetadata.ContentType, Is.EqualTo(descriptor.FileMetadata.ContentType));
                CollectionAssert.AreEquivalent(transitClientFileHeader.FileMetadata.AppData.Tags, descriptor.FileMetadata.AppData.Tags);
                Assert.That(transitClientFileHeader.FileMetadata.AppData.JsonContent, Is.EqualTo(descriptor.FileMetadata.AppData.JsonContent));
                Assert.That(transitClientFileHeader.FileMetadata.AppData.ContentIsComplete, Is.EqualTo(descriptor.FileMetadata.AppData.ContentIsComplete));

                Assert.That(transitClientFileHeader.SharedSecretEncryptedKeyHeader, Is.Not.Null);
                Assert.That(transitClientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.Null);
                Assert.That(transitClientFileHeader.SharedSecretEncryptedKeyHeader.Iv.Length, Is.GreaterThanOrEqualTo(16));
                Assert.That(transitClientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.EqualTo(Guid.Empty.ToByteArray()), "Iv was all zeros");
                Assert.That(transitClientFileHeader.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes));

                var decryptedClientFileKeyHeader = transitClientFileHeader.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ownerSharedSecret);

                Assert.That(decryptedClientFileKeyHeader.AesKey.IsSet(), Is.True);
                Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(decryptedClientFileKeyHeader.AesKey.GetKey(), keyHeader.AesKey.GetKey()));

                //
                // Get the payload that was sent to the recipient via transit, test it
                // has the decrypted data content type
                // can be decrypted using the owner shared secret encrypted key header
                var getTransitPayloadResponse = await transitQueryService.GetPayload(new TransitExternalFileIdentifier()
                {
                    OdinId = recipient.OdinId,
                    File = uploadedFile
                });

                Assert.That(getTransitPayloadResponse.IsSuccessStatusCode, Is.True);
                Assert.That(getTransitPayloadResponse.Content, Is.Not.Null);

                var payloadIsEncrypted = bool.Parse(getTransitPayloadResponse.Headers.GetValues(HttpHeaderConstants.PayloadEncrypted).Single());
                Assert.IsTrue(payloadIsEncrypted);

                var payloadSharedSecretKeyHeaderValue = getTransitPayloadResponse.Headers.GetValues(HttpHeaderConstants.SharedSecretEncryptedHeader64).Single();
                var ownerSharedSecretEncryptedKeyHeaderForPayload = EncryptedKeyHeader.FromBase64(payloadSharedSecretKeyHeaderValue);

                var getTransitPayloadContentTypeHeader = getTransitPayloadResponse.Headers.GetValues(HttpHeaderConstants.DecryptedContentType).Single();
                Assert.IsTrue(descriptor.FileMetadata.ContentType == getTransitPayloadContentTypeHeader);

                var decryptedPayloadKeyHeader = ownerSharedSecretEncryptedKeyHeaderForPayload.DecryptAesToKeyHeader(ref ownerSharedSecret);
                var payloadResponseCipherBytes = await getTransitPayloadResponse.Content.ReadAsByteArrayAsync();
                Assert.IsTrue(reuploadedContext.PayloadCipher.Length == payloadResponseCipherBytes.Length);
                var decryptedPayloadBytes = decryptedPayloadKeyHeader.Decrypt(payloadResponseCipherBytes);
                Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(originalPayloadData.ToUtf8ByteArray(), decryptedPayloadBytes));

                //
                // Get the thumbnail that was sent to the recipient via transit, test it
                // can get the thumbnail that as uploaded and sent
                // can decrypt the thumbnail using the owner shared secret encrypted keyheader
                // 
                var getTransitThumbnailResponse = await transitQueryService.GetThumbnail(new TransitGetThumbRequest()
                {
                    OdinId = recipient.OdinId,
                    File = uploadedFile,
                    Width = thumbnail1.PixelWidth,
                    Height = thumbnail1.PixelHeight
                });

                Assert.IsTrue(getTransitThumbnailResponse.IsSuccessStatusCode);
                Assert.IsNotNull(getTransitThumbnailResponse.Content);

                var thumbnailIsEncrypted = bool.Parse(getTransitThumbnailResponse.Headers.GetValues(HttpHeaderConstants.PayloadEncrypted).Single());
                Assert.IsTrue(thumbnailIsEncrypted);

                var thumbnailSharedSecretKeyHeaderValue =
                    getTransitThumbnailResponse.Headers.GetValues(HttpHeaderConstants.SharedSecretEncryptedHeader64).Single();
                var ownerSharedSecretEncryptedKeyHeaderForThumbnail = EncryptedKeyHeader.FromBase64(thumbnailSharedSecretKeyHeaderValue);

                var getTransitThumbnailContentTypeHeader = getTransitThumbnailResponse.Headers.GetValues(HttpHeaderConstants.DecryptedContentType).Single();
                Assert.IsTrue(thumbnail1.ContentType == getTransitThumbnailContentTypeHeader);

                var decryptedThumbnailKeyHeader = ownerSharedSecretEncryptedKeyHeaderForThumbnail.DecryptAesToKeyHeader(ref ownerSharedSecret);
                var transitThumbnailResponse1CipherBytes = await getTransitThumbnailResponse!.Content!.ReadAsByteArrayAsync();
                var decryptedThumbnailBytes = decryptedThumbnailKeyHeader.Decrypt(transitThumbnailResponse1CipherBytes);
                Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnail1OriginalBytes, decryptedThumbnailBytes));
            }

            keyHeader.AesKey.Wipe();
            key.Wipe();

            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipientContext.Identity);
        }

        [Test]
        public async Task FailToTransferStandardFile_WhenNoWriteAccess()
        {
            var sender = TestIdentities.Samwise;
            var recipient = TestIdentities.Pippin;

            Guid appId = Guid.NewGuid();
            var targetDrive = TargetDrive.NewTargetDrive();
            var senderContext =
                await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, sender, canReadConnections: true, targetDrive, driveAllowAnonymousReads: true);
            var recipientContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(senderContext.AppId, recipient, canReadConnections: true, targetDrive);

            Guid fileTag = Guid.NewGuid();

            var senderCircleDef =
                await _scaffold.OldOwnerApi.CreateCircleWithDrive(sender.OdinId, "Sender Circle",
                    permissionKeys: new List<int>() { },
                    drive: new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.Read
                    });

            var recipientCircleDef =
                await _scaffold.OldOwnerApi.CreateCircleWithDrive(recipient.OdinId, "Recipient Circle",
                    permissionKeys: new List<int>() { },
                    drive: new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.Read
                    });

            await _scaffold.OldOwnerApi.CreateConnection(sender.OdinId, recipient.OdinId,
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
                    Drive = senderContext.TargetDrive,
                    OverwriteFileId = null
                },

                TransitOptions = new TransitOptions()
                {
                    Schedule = ScheduleOptions.SendNowAwaitResponse,
                    Recipients = new List<string>() { recipient.OdinId },
                    UseGlobalTransitId = true
                }
            };

            var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var key = senderContext.SharedSecret.ToSensitiveByteArray();
            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref key),
                FileMetadata = new()
                {
                    ContentType = "application/json",
                    AllowDistribution = true,
                    AppData = new()
                    {
                        Tags = new List<Guid>() { fileTag },
                        ContentIsComplete = false,
                        JsonContent = DotYouSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    },
                    PayloadIsEncrypted = true,
                    AccessControlList = new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected }
                },
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref key);

            var payloadData = "{payload:true, image:'b64 data'}";
            var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadData);

            //
            // upload and send the file 
            //
            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(senderContext))
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
                    Assert.IsTrue(transferResult.RecipientStatus[r] == TransferStatus.RecipientReturnedAccessDenied, $"transfer key not created for {r}");
                }
            }

        }

        [Test]
        public async Task CanTransferCommentFileAndRecipientCanQueryFilesByTag()
        {
            var sender = TestIdentities.Samwise;
            var recipient = TestIdentities.Pippin;

            Guid appId = Guid.NewGuid();
            var targetDrive = TargetDrive.NewTargetDrive();
            var senderContext =
                await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, sender, canReadConnections: true, targetDrive, driveAllowAnonymousReads: true);
            var recipientContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(senderContext.AppId, recipient, canReadConnections: true, targetDrive);

            Guid fileTag = Guid.NewGuid();

            var senderCircleDef =
                await _scaffold.OldOwnerApi.CreateCircleWithDrive(sender.OdinId, "Sender Circle",
                    permissionKeys: new List<int>() { },
                    drive: new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.Read | DrivePermission.WriteReactionsAndComments
                    });

            var recipientCircleDef =
                await _scaffold.OldOwnerApi.CreateCircleWithDrive(recipient.OdinId, "Recipient Circle",
                    permissionKeys: new List<int>() { },
                    drive: new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.Read | DrivePermission.WriteReactionsAndComments
                    });

            await _scaffold.OldOwnerApi.CreateConnection(sender.OdinId, recipient.OdinId,
                createConnectionOptions: new CreateConnectionOptions()
                {
                    CircleIdsGrantedToRecipient = new List<GuidId>() { senderCircleDef.Id },
                    CircleIdsGrantedToSender = new List<GuidId>() { recipientCircleDef.Id }
                });


            var samOwnerClient = _scaffold.CreateOwnerApiClient(recipient);
            var postFileMetadata = new UploadFileMetadata()
            {
                ContentType = "application/json",
                AllowDistribution = true,
                AppData = new()
                {
                    Tags = new List<Guid>() { fileTag },
                    ContentIsComplete = true,
                    JsonContent = DotYouSystemSerializer.Serialize(new { content = "some stuff about a thing" }),
                },
                PayloadIsEncrypted = false,
                AccessControlList = new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected }
            };

            var postUploadResult =
                await samOwnerClient.Drive.UploadFile(FileSystemType.Standard, targetDrive, postFileMetadata, "", null, overwriteFileId: null);

            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            var keyHeader = KeyHeader.NewRandom16();

            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = transferIv,
                StorageOptions = new StorageOptions()
                {
                    Drive = senderContext.TargetDrive,
                    OverwriteFileId = null
                },

                TransitOptions = new TransitOptions()
                {
                    Recipients = new List<string>() { recipient.OdinId },
                    UseGlobalTransitId = true
                }
            };

            var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var key = senderContext.SharedSecret.ToSensitiveByteArray();
            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref key),
                FileMetadata = new()
                {
                    ReferencedFile = postUploadResult.GlobalTransitIdFileIdentifier,
                    ContentType = "application/json",
                    AllowDistribution = true,
                    AppData = new()
                    {
                        Tags = new List<Guid>() { fileTag },
                        ContentIsComplete = false,
                        JsonContent = DotYouSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    },
                    PayloadIsEncrypted = true,
                    AccessControlList = new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected }
                },
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref key);

            var payloadData = "{payload:true, image:'b64 data'}";
            var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadData);

            //
            // upload and send the file 
            //
            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(senderContext, FileSystemType.Comment))
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

            await _scaffold.OldOwnerApi.ProcessOutbox(sender.OdinId);

            ExternalFileIdentifier uploadedFile;
            var fileTagQueryParams = new FileQueryParams()
            {
                TargetDrive = recipientContext.TargetDrive,
                TagsMatchAll = new List<Guid>() { fileTag }
            };


            //
            // validate recipient got the file
            //
            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(recipientContext, FileSystemType.Comment))
            {
                //First force transfers to be put into their long term location
                var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(client);
                var resp = await transitAppSvc.ProcessIncomingInstructions(
                    new ProcessTransitInstructionRequest() { TargetDrive = recipientContext.TargetDrive });
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

                var fileResponse = await driveSvc.GetFileHeaderAsPost(uploadedFile);

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

                var payloadResponse = await driveSvc.GetPayloadAsPost(uploadedFile);
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

            descriptor.FileMetadata.AllowDistribution = false;
            instructionSet.TransitOptions = null;

            await _scaffold.OldOwnerApi.UploadFile(recipient.OdinId, instructionSet, descriptor.FileMetadata, payloadData, true,
                fileSystemType: FileSystemType.Comment);


            //
            //  The final test - use transit query batch for the sender to get the file on the recipients identity over transit
            //
            using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sender, out var sharedSecret, FileSystemType.Comment))
            {
                var svc = RefitCreator.RestServiceFor<ITransitQueryHttpClientForOwner>(client, sharedSecret);

                var queryBatchResponse = await svc.GetBatch(new TransitQueryBatchRequest()
                {
                    OdinId = recipient.OdinId,
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

            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipientContext.Identity);
        }

        [Test]
        public async Task CanTransferCommentFileAndRecipientCanGetFileHeaderThumbAndPayload()
        {
            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            Guid appId = Guid.NewGuid();
            var targetDrive = TargetDrive.NewTargetDrive();
            var senderContext =
                await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, sender, canReadConnections: true, targetDrive, driveAllowAnonymousReads: true);
            var recipientContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(senderContext.AppId, recipient, canReadConnections: true, targetDrive);

            Guid fileTag = Guid.NewGuid();

            var senderCircleDef =
                await _scaffold.OldOwnerApi.CreateCircleWithDrive(sender.OdinId, "Sender Circle",
                    permissionKeys: new List<int>() { },
                    drive: new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.Read | DrivePermission.WriteReactionsAndComments
                    });

            var recipientCircleDef =
                await _scaffold.OldOwnerApi.CreateCircleWithDrive(recipient.OdinId, "Recipient Circle",
                    permissionKeys: new List<int>() { },
                    drive: new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.Read | DrivePermission.WriteReactionsAndComments
                    });

            await _scaffold.OldOwnerApi.CreateConnection(sender.OdinId, recipient.OdinId,
                createConnectionOptions: new CreateConnectionOptions()
                {
                    CircleIdsGrantedToRecipient = new List<GuidId>() { senderCircleDef.Id },
                    CircleIdsGrantedToSender = new List<GuidId>() { recipientCircleDef.Id }
                });

            //upload a post on sam's side on which frodo can comment
            var samOwnerClient = _scaffold.CreateOwnerApiClient(recipient);
            var postFileMetadata = new UploadFileMetadata()
            {
                ContentType = "application/json",
                AllowDistribution = true,
                AppData = new()
                {
                    Tags = new List<Guid>() { fileTag },
                    ContentIsComplete = true,
                    JsonContent = DotYouSystemSerializer.Serialize(new { content = "some stuff about a thing" }),
                },
                PayloadIsEncrypted = false,
                AccessControlList = new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected }
            };

            var postUploadResult = await samOwnerClient.Drive.UploadFile(FileSystemType.Standard, targetDrive, postFileMetadata, "");

            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            var keyHeader = KeyHeader.NewRandom16();

            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = transferIv,
                StorageOptions = new StorageOptions()
                {
                    Drive = senderContext.TargetDrive,
                    OverwriteFileId = null
                },

                TransitOptions = new TransitOptions()
                {
                    Recipients = new List<string>() { recipient.OdinId },
                    UseGlobalTransitId = true
                }
            };

            var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var key = senderContext.SharedSecret.ToSensitiveByteArray();
            var json = DotYouSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" });
            var encryptedJsonContent64 = keyHeader.EncryptDataAesAsStream(json).ToByteArray().ToBase64();

            var thumbnail1 = new ImageDataHeader()
            {
                PixelHeight = 300,
                PixelWidth = 300,
                ContentType = "image/jpeg"
            };
            var thumbnail1OriginalBytes = TestMedia.ThumbnailBytes300;
            var thumbnail1CipherBytes = keyHeader.EncryptDataAes(thumbnail1OriginalBytes);

            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref key),
                FileMetadata = new()
                {
                    ReferencedFile = postUploadResult.GlobalTransitIdFileIdentifier,
                    ContentType = "application/json",
                    AllowDistribution = true,
                    AppData = new()
                    {
                        Tags = new List<Guid>() { fileTag },
                        ContentIsComplete = false,
                        JsonContent = encryptedJsonContent64,
                        AdditionalThumbnails = new[] { thumbnail1 }
                    },
                    PayloadIsEncrypted = true,
                    AccessControlList = new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected }
                },
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref key);

            var originalPayloadData = "{payload:true, image:'b64 data'}";
            var originalPayloadCipherBytes = keyHeader.EncryptDataAesAsStream(originalPayloadData);

            //
            // upload and send the file 
            //
            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(senderContext, FileSystemType.Comment))
            {
                var transitSvc = RestService.For<IDriveTestHttpClientForApps>(client);
                var response = await transitSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(originalPayloadCipherBytes, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)),
                    new StreamPart(new MemoryStream(thumbnail1CipherBytes), thumbnail1.GetFilename(), thumbnail1.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail)));

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

            await _scaffold.OldOwnerApi.ProcessOutbox(sender.OdinId);

            ExternalFileIdentifier uploadedFile;
            var fileTagQueryParams = new FileQueryParams()
            {
                TargetDrive = recipientContext.TargetDrive,
                TagsMatchAll = new List<Guid>() { fileTag }
            };

            //
            // Validate recipient got the file
            //
            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(recipientContext, FileSystemType.Comment))
            {
                //First force transfers to be put into their long term location
                var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(client);
                var resp = await transitAppSvc.ProcessIncomingInstructions(
                    new ProcessTransitInstructionRequest() { TargetDrive = recipientContext.TargetDrive });
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

                var fileResponse = await driveSvc.GetFileHeaderAsPost(uploadedFile);

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

                var payloadResponse = await driveSvc.GetPayloadAsPost(uploadedFile);
                Assert.That(payloadResponse.IsSuccessStatusCode, Is.True);
                Assert.That(payloadResponse.Content, Is.Not.Null);

                var payloadResponseCipher = await payloadResponse.Content.ReadAsByteArrayAsync();
                Assert.That(((MemoryStream)originalPayloadCipherBytes).ToArray(), Is.EqualTo(payloadResponseCipher));

                var aesKey = decryptedKeyHeader.AesKey;
                var decryptedPayloadBytes = Core.Cryptography.Crypto.AesCbc.Decrypt(
                    cipherText: payloadResponseCipher,
                    Key: ref aesKey,
                    IV: decryptedKeyHeader.Iv);

                var payloadBytes = System.Text.Encoding.UTF8.GetBytes(originalPayloadData);
                Assert.That(payloadBytes, Is.EqualTo(decryptedPayloadBytes));

                // var decryptedPayloadRaw = System.Text.Encoding.UTF8.GetString(decryptedPayloadBytes);

                var getThumbnailResponse = await driveSvc.GetThumbnailAsPost(new GetThumbnailRequest()
                {
                    File = uploadedFile,
                    Width = thumbnail1.PixelWidth,
                    Height = thumbnail1.PixelHeight
                });

                Assert.IsTrue(getThumbnailResponse.IsSuccessStatusCode);
                var getThumbnailResponseBytes = await getThumbnailResponse.Content!.ReadAsByteArrayAsync();
                Assert.IsNotNull(thumbnail1CipherBytes.Length == getThumbnailResponseBytes.Length);

                decryptedKeyHeader.AesKey.Wipe();
                // keyHeader.AesKey.Wipe();
            }


            // reupload the file on the recipient's identity and set the filemetadata to authenticated.
            // this is done because when files are received, they are set to owner only on the recipients identity
            instructionSet.StorageOptions = new StorageOptions()
            {
                OverwriteFileId = uploadedFile.FileId,
                Drive = uploadedFile.TargetDrive
            };

            instructionSet.TransitOptions = null;
            descriptor.FileMetadata.AllowDistribution = false;

            var reuploadedContext = await _scaffold.OldOwnerApi.UploadFile(recipient.OdinId, instructionSet, descriptor.FileMetadata, originalPayloadData, true,
                new ImageDataContent()
                {
                    ContentType = thumbnail1.ContentType,
                    PixelHeight = thumbnail1.PixelHeight,
                    PixelWidth = thumbnail1.PixelWidth,
                    Content = thumbnail1OriginalBytes
                },
                keyHeader: keyHeader,
                fileSystemType: FileSystemType.Comment);

            //
            //  The final test - use transit query batch for the sender to get the file on the recipients identity over transit
            //
            using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sender, out var ownerSharedSecret, FileSystemType.Comment))
            {
                var transitQueryService = RefitCreator.RestServiceFor<ITransitQueryHttpClientForOwner>(client, ownerSharedSecret);

                //
                // Test the file matches the original one sent
                //
                var getTransitFileResponse = await transitQueryService.GetFileHeader(new TransitExternalFileIdentifier()
                {
                    OdinId = recipient.OdinId,
                    File = uploadedFile
                });

                Assert.IsTrue(getTransitFileResponse.IsSuccessStatusCode);
                Assert.IsNotNull(getTransitFileResponse.Content);

                var fileResponse = getTransitFileResponse.Content;
                Assert.IsTrue(uploadedFile.FileId == fileResponse.FileId);

                var transitClientFileHeader = getTransitFileResponse.Content;

                Assert.That(transitClientFileHeader.FileMetadata, Is.Not.Null);
                Assert.That(transitClientFileHeader.FileMetadata.AppData, Is.Not.Null);

                Assert.That(transitClientFileHeader.FileMetadata.ContentType, Is.EqualTo(descriptor.FileMetadata.ContentType));
                CollectionAssert.AreEquivalent(transitClientFileHeader.FileMetadata.AppData.Tags, descriptor.FileMetadata.AppData.Tags);
                Assert.That(transitClientFileHeader.FileMetadata.AppData.JsonContent, Is.EqualTo(descriptor.FileMetadata.AppData.JsonContent));
                Assert.That(transitClientFileHeader.FileMetadata.AppData.ContentIsComplete, Is.EqualTo(descriptor.FileMetadata.AppData.ContentIsComplete));

                Assert.That(transitClientFileHeader.SharedSecretEncryptedKeyHeader, Is.Not.Null);
                Assert.That(transitClientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.Null);
                Assert.That(transitClientFileHeader.SharedSecretEncryptedKeyHeader.Iv.Length, Is.GreaterThanOrEqualTo(16));
                Assert.That(transitClientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.EqualTo(Guid.Empty.ToByteArray()), "Iv was all zeros");
                Assert.That(transitClientFileHeader.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes));

                var decryptedClientFileKeyHeader = transitClientFileHeader.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ownerSharedSecret);

                Assert.That(decryptedClientFileKeyHeader.AesKey.IsSet(), Is.True);
                Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(decryptedClientFileKeyHeader.AesKey.GetKey(), keyHeader.AesKey.GetKey()));

                //
                // Get the payload that was sent to the recipient via transit, test it
                // has the decrypted data content type
                // can be decrypted using the owner shared secret encrypted key header
                var getTransitPayloadResponse = await transitQueryService.GetPayload(new TransitExternalFileIdentifier()
                {
                    OdinId = recipient.OdinId,
                    File = uploadedFile
                });

                Assert.That(getTransitPayloadResponse.IsSuccessStatusCode, Is.True);
                Assert.That(getTransitPayloadResponse.Content, Is.Not.Null);

                var payloadIsEncrypted = bool.Parse(getTransitPayloadResponse.Headers.GetValues(HttpHeaderConstants.PayloadEncrypted).Single());
                Assert.IsTrue(payloadIsEncrypted);

                var payloadSharedSecretKeyHeaderValue = getTransitPayloadResponse.Headers.GetValues(HttpHeaderConstants.SharedSecretEncryptedHeader64).Single();
                var ownerSharedSecretEncryptedKeyHeaderForPayload = EncryptedKeyHeader.FromBase64(payloadSharedSecretKeyHeaderValue);

                var getTransitPayloadContentTypeHeader = getTransitPayloadResponse.Headers.GetValues(HttpHeaderConstants.DecryptedContentType).Single();
                Assert.IsTrue(descriptor.FileMetadata.ContentType == getTransitPayloadContentTypeHeader);

                var decryptedPayloadKeyHeader = ownerSharedSecretEncryptedKeyHeaderForPayload.DecryptAesToKeyHeader(ref ownerSharedSecret);
                var payloadResponseCipherBytes = await getTransitPayloadResponse.Content.ReadAsByteArrayAsync();
                Assert.IsTrue(reuploadedContext.PayloadCipher.Length == payloadResponseCipherBytes.Length);
                var decryptedPayloadBytes = decryptedPayloadKeyHeader.Decrypt(payloadResponseCipherBytes);
                Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(originalPayloadData.ToUtf8ByteArray(), decryptedPayloadBytes));

                //
                // Get the thumbnail that was sent to the recipient via transit, test it
                // can get the thumbnail that as uploaded and sent
                // can decrypt the thumbnail using the owner shared secret encrypted keyheader
                // 
                var getTransitThumbnailResponse = await transitQueryService.GetThumbnail(new TransitGetThumbRequest()
                {
                    OdinId = recipient.OdinId,
                    File = uploadedFile,
                    Width = thumbnail1.PixelWidth,
                    Height = thumbnail1.PixelHeight
                });

                Assert.IsTrue(getTransitThumbnailResponse.IsSuccessStatusCode);
                Assert.IsNotNull(getTransitThumbnailResponse.Content);

                var thumbnailIsEncrypted = bool.Parse(getTransitThumbnailResponse.Headers.GetValues(HttpHeaderConstants.PayloadEncrypted).Single());
                Assert.IsTrue(thumbnailIsEncrypted);

                var thumbnailSharedSecretKeyHeaderValue =
                    getTransitThumbnailResponse.Headers.GetValues(HttpHeaderConstants.SharedSecretEncryptedHeader64).Single();
                var ownerSharedSecretEncryptedKeyHeaderForThumbnail = EncryptedKeyHeader.FromBase64(thumbnailSharedSecretKeyHeaderValue);

                var getTransitThumbnailContentTypeHeader = getTransitThumbnailResponse.Headers.GetValues(HttpHeaderConstants.DecryptedContentType).Single();
                Assert.IsTrue(thumbnail1.ContentType == getTransitThumbnailContentTypeHeader);

                var decryptedThumbnailKeyHeader = ownerSharedSecretEncryptedKeyHeaderForThumbnail.DecryptAesToKeyHeader(ref ownerSharedSecret);
                var transitThumbnailResponse1CipherBytes = await getTransitThumbnailResponse!.Content!.ReadAsByteArrayAsync();
                var decryptedThumbnailBytes = decryptedThumbnailKeyHeader.Decrypt(transitThumbnailResponse1CipherBytes);
                Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnail1OriginalBytes, decryptedThumbnailBytes));
            }

            keyHeader.AesKey.Wipe();
            key.Wipe();

            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipientContext.Identity);
        }

        [Test]
        public async Task FailToTransferComment_When_Missing_DrivePermission_WriteReactionAndComment()
        {
            var sender = TestIdentities.Samwise;
            var recipient = TestIdentities.Pippin;

            Guid appId = Guid.NewGuid();
            var targetDrive = TargetDrive.NewTargetDrive();
            var senderContext =
                await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, sender, canReadConnections: true, targetDrive, driveAllowAnonymousReads: true);
            var recipientContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(senderContext.AppId, recipient, canReadConnections: true, targetDrive);

            Guid fileTag = Guid.NewGuid();

            var senderCircleDef =
                await _scaffold.OldOwnerApi.CreateCircleWithDrive(sender.OdinId, "Sender Circle",
                    permissionKeys: new List<int>() { },
                    drive: new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.Read | DrivePermission.Write
                    });

            var recipientCircleDef =
                await _scaffold.OldOwnerApi.CreateCircleWithDrive(recipient.OdinId, "Recipient Circle",
                    permissionKeys: new List<int>() { },
                    drive: new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.Read | DrivePermission.Write
                    });

            await _scaffold.OldOwnerApi.CreateConnection(sender.OdinId, recipient.OdinId,
                createConnectionOptions: new CreateConnectionOptions()
                {
                    CircleIdsGrantedToRecipient = new List<GuidId>() { senderCircleDef.Id },
                    CircleIdsGrantedToSender = new List<GuidId>() { recipientCircleDef.Id }
                });


            var samOwnerClient = _scaffold.CreateOwnerApiClient(recipient);
            var postFileMetadata = new UploadFileMetadata()
            {
                ContentType = "application/json",
                AllowDistribution = true,
                AppData = new()
                {
                    Tags = new List<Guid>() { fileTag },
                    ContentIsComplete = true,
                    JsonContent = DotYouSystemSerializer.Serialize(new { content = "some stuff about a thing" }),
                },
                PayloadIsEncrypted = false,
                AccessControlList = new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected }
            };

            var postUploadResult =
                await samOwnerClient.Drive.UploadFile(FileSystemType.Standard, targetDrive, postFileMetadata, "", null, overwriteFileId: null);

            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            var keyHeader = KeyHeader.NewRandom16();

            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = transferIv,
                StorageOptions = new StorageOptions()
                {
                    Drive = senderContext.TargetDrive,
                    OverwriteFileId = null
                },

                TransitOptions = new TransitOptions()
                {
                    Schedule = ScheduleOptions.SendNowAwaitResponse,
                    Recipients = new List<string>() { recipient.OdinId },
                    UseGlobalTransitId = true
                }
            };

            var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var key = senderContext.SharedSecret.ToSensitiveByteArray();
            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref key),
                FileMetadata = new()
                {
                    ReferencedFile = postUploadResult.GlobalTransitIdFileIdentifier,
                    ContentType = "application/json",
                    AllowDistribution = true,
                    AppData = new()
                    {
                        Tags = new List<Guid>() { fileTag },
                        ContentIsComplete = false,
                        JsonContent = DotYouSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    },
                    PayloadIsEncrypted = true,
                    AccessControlList = new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected }
                },
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref key);

            var payloadData = "{payload:true, image:'b64 data'}";
            var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadData);

            //
            // upload and send the file 
            //
            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(senderContext, FileSystemType.Comment))
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
                    Assert.IsTrue(transferResult.RecipientStatus[r] == TransferStatus.RecipientReturnedAccessDenied, $"transfer key not created for {r}");
                }
            }

            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipientContext.Identity);
        }


        [Test]
        public async Task FailToTransferCommentFileWith_InvalidGlobalTransitId()
        {
            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            Guid appId = Guid.NewGuid();
            var targetDrive = TargetDrive.NewTargetDrive();
            var senderContext =
                await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, sender, canReadConnections: true, targetDrive, driveAllowAnonymousReads: true);
            var recipientContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(senderContext.AppId, recipient, canReadConnections: true, targetDrive);

            Guid fileTag = Guid.NewGuid();

            var senderCircleDef =
                await _scaffold.OldOwnerApi.CreateCircleWithDrive(sender.OdinId, "Sender Circle",
                    permissionKeys: new List<int>() { },
                    drive: new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.Read | DrivePermission.WriteReactionsAndComments
                    });

            var recipientCircleDef =
                await _scaffold.OldOwnerApi.CreateCircleWithDrive(recipient.OdinId, "Recipient Circle",
                    permissionKeys: new List<int>() { },
                    drive: new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.Read | DrivePermission.WriteReactionsAndComments
                    });

            await _scaffold.OldOwnerApi.CreateConnection(sender.OdinId, recipient.OdinId,
                createConnectionOptions: new CreateConnectionOptions()
                {
                    CircleIdsGrantedToRecipient = new List<GuidId>() { senderCircleDef.Id },
                    CircleIdsGrantedToSender = new List<GuidId>() { recipientCircleDef.Id }
                });

            //upload a post on sam's side on which frodo can comment
            var samOwnerClient = _scaffold.CreateOwnerApiClient(recipient);
            var postFileMetadata = new UploadFileMetadata()
            {
                ContentType = "application/json",
                AllowDistribution = true,
                AppData = new()
                {
                    Tags = new List<Guid>() { fileTag },
                    ContentIsComplete = true,
                    JsonContent = DotYouSystemSerializer.Serialize(new { content = "some stuff about a thing" }),
                },
                PayloadIsEncrypted = false,
                AccessControlList = new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected }
            };

            var postUploadResult = await samOwnerClient.Drive.UploadFile(FileSystemType.Standard, targetDrive, postFileMetadata, "");

            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            var keyHeader = KeyHeader.NewRandom16();

            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = transferIv,
                StorageOptions = new StorageOptions()
                {
                    Drive = senderContext.TargetDrive,
                    OverwriteFileId = null
                },

                TransitOptions = new TransitOptions()
                {
                    Schedule = ScheduleOptions.SendNowAwaitResponse,
                    Recipients = new List<string>() { recipient.OdinId },
                    UseGlobalTransitId = true
                }
            };

            var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var key = senderContext.SharedSecret.ToSensitiveByteArray();
            var json = DotYouSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" });
            var encryptedJsonContent64 = keyHeader.EncryptDataAesAsStream(json).ToByteArray().ToBase64();

            var thumbnail1 = new ImageDataHeader()
            {
                PixelHeight = 300,
                PixelWidth = 300,
                ContentType = "image/jpeg"
            };
            var thumbnail1OriginalBytes = TestMedia.ThumbnailBytes300;
            var thumbnail1CipherBytes = keyHeader.EncryptDataAes(thumbnail1OriginalBytes);

            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref key),
                FileMetadata = new()
                {
                    ReferencedFile = new GlobalTransitIdFileIdentifier() //write an invalid global transit id so the recipient server will reject
                    {
                        GlobalTransitId = Guid.NewGuid(),
                        TargetDrive = postUploadResult.GlobalTransitIdFileIdentifier.TargetDrive
                    },
                    ContentType = "application/json",
                    AllowDistribution = true,
                    AppData = new()
                    {
                        Tags = new List<Guid>() { fileTag },
                        ContentIsComplete = false,
                        JsonContent = encryptedJsonContent64,
                        AdditionalThumbnails = new[] { thumbnail1 }
                    },
                    PayloadIsEncrypted = true,
                    AccessControlList = new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected }
                },
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref key);

            var originalPayloadData = "{payload:true, image:'b64 data'}";
            var originalPayloadCipherBytes = keyHeader.EncryptDataAesAsStream(originalPayloadData);

            //
            // upload and send the comment file
            //
            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(senderContext, FileSystemType.Comment))
            {
                var transitSvc = RestService.For<IDriveTestHttpClientForApps>(client);
                var response = await transitSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(originalPayloadCipherBytes, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)),
                    new StreamPart(new MemoryStream(thumbnail1CipherBytes), thumbnail1.GetFilename(), thumbnail1.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail)));

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var transferResult = response.Content;

                Assert.That(transferResult.File, Is.Not.Null);
                Assert.That(transferResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                Assert.IsTrue(transferResult.File.TargetDrive.IsValid());

                foreach (var r in instructionSet.TransitOptions.Recipients)
                {
                    Assert.IsTrue(transferResult.RecipientStatus.ContainsKey(r), $"Could not find matching recipient {r}");
                    Assert.IsTrue(transferResult.RecipientStatus[r] == TransferStatus.DeliveredToInbox, $"message should have been delivered to {r}");
                }
            }

            //
            // Force transfers to be put into their long term location
            //
            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(recipientContext, FileSystemType.Comment))
            {
                var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(client);
                var resp = await transitAppSvc.ProcessIncomingInstructions(
                    new ProcessTransitInstructionRequest() { TargetDrive = recipientContext.TargetDrive });

                //Note: this will fail because the reference file is missing but there's no way to recover
                Assert.IsFalse(resp.IsSuccessStatusCode, resp.ReasonPhrase);
            }

            keyHeader.AesKey.Wipe();
            key.Wipe();

            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipientContext.Identity);
        }


        [Test]
        public async Task CanGetDrivesByType()
        {
            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            Guid appId = Guid.NewGuid();
            var expectedDriveType = Guid.NewGuid();

            var targetDriveReadOnly = new TargetDrive()
            {
                Alias = Guid.NewGuid(),
                Type = expectedDriveType
            };

            var targetDriveReadWrite = new TargetDrive()
            {
                Alias = Guid.NewGuid(),
                Type = expectedDriveType
            };

            var targetDriveWriteOnly = new TargetDrive()
            {
                Alias = Guid.NewGuid(),
                Type = expectedDriveType
            };

            var senderContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, sender, false, targetDriveReadOnly);
            var recipientContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, recipient, false, targetDriveReadOnly);

            //give add additional to recipient
            await _scaffold.OldOwnerApi.CreateDrive(recipient.OdinId, targetDriveReadWrite, "ReadWrite Drive", "", false);
            await _scaffold.OldOwnerApi.CreateDrive(recipient.OdinId, targetDriveWriteOnly, "Write Only Drive", "", false);

            var senderCircleDef =
                await _scaffold.OldOwnerApi.CreateCircleWithDrive(sender.OdinId, "Sender Circle",
                    permissionKeys: new List<int>() { },
                    drive: new PermissionedDrive()
                    {
                        Drive = targetDriveReadOnly,
                        Permission = DrivePermission.ReadWrite
                    });

            //grant sender access to the drives
            var recipientCircleDef =
                await _scaffold.OldOwnerApi.CreateCircleWithDrive(recipient.OdinId, "Recipient Circle",
                    permissionKeys: new List<int>() { },
                    drives: new List<PermissionedDrive>()
                    {
                        new()
                        {
                            Drive = targetDriveReadWrite,
                            Permission = DrivePermission.ReadWrite
                        },
                        new()
                        {
                            Drive = targetDriveReadOnly,
                            Permission = DrivePermission.Read
                        },
                        new()
                        {
                            Drive = targetDriveWriteOnly,
                            Permission = DrivePermission.Write
                        }
                    });

            await _scaffold.OldOwnerApi.CreateConnection(sender.OdinId, recipient.OdinId,
                createConnectionOptions: new CreateConnectionOptions()
                {
                    CircleIdsGrantedToRecipient = new List<GuidId>() { senderCircleDef.Id },
                    CircleIdsGrantedToSender = new List<GuidId>() { recipientCircleDef.Id }
                });

            //
            using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sender, out var ownerSharedSecret))
            {
                var transitQueryService = RefitCreator.RestServiceFor<ITransitQueryHttpClientForOwner>(client, ownerSharedSecret);

                var getTransitDrives = await transitQueryService.GetDrives(new TransitGetDrivesByTypeRequest()
                {
                    OdinId = recipient.OdinId,
                    DriveType = targetDriveReadOnly.Type
                });

                Assert.IsTrue(getTransitDrives.IsSuccessStatusCode);
                Assert.IsNotNull(getTransitDrives.Content);

                var drivesOnRecipientIdentityAccessibleToSender = getTransitDrives.Content.Results;

                Assert.IsTrue(drivesOnRecipientIdentityAccessibleToSender.All(d => d.TargetDrive.Type == expectedDriveType));
                Assert.IsTrue(drivesOnRecipientIdentityAccessibleToSender.Count == 2);
                Assert.IsNotNull(drivesOnRecipientIdentityAccessibleToSender.SingleOrDefault(d => d.TargetDrive == targetDriveReadOnly));
                Assert.IsNotNull(drivesOnRecipientIdentityAccessibleToSender.SingleOrDefault(d => d.TargetDrive == targetDriveReadWrite));
                Assert.IsNull(drivesOnRecipientIdentityAccessibleToSender.SingleOrDefault(d => d.TargetDrive == targetDriveWriteOnly),
                    "should not have access to write only drive");
            }

            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipientContext.Identity);
        }
    }
}