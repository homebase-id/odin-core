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
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Transit;
using Odin.Core.Services.Transit.Encryption;
using Odin.Core.Services.Transit.ReceivingHost;
using Odin.Core.Services.Transit.SendingHost;
using Odin.Hosting.Tests.AppAPI.Transit;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.Drive
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

        [Test]
        public async Task TransientFileIsDeletedAfterSending()
        {
            int someFiletype = 3892;
            var instructionSet = UploadInstructionSet.WithRecipients(TargetDrive.NewTargetDrive(), TestIdentities.Merry.OdinId);
            instructionSet.TransitOptions.IsTransient = true;
            instructionSet.TransitOptions.UseGlobalTransitId = true;

            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = true,
                AppData = new UploadAppFileMetaData()
                {
                    FileType = someFiletype,
                    JsonContent = "this is some content",
                }
            };

            var options = new TransitTestUtilsOptions()
            {
                ProcessOutbox = true,
                ProcessTransitBox = true,
                DisconnectIdentitiesAfterTransfer = true,
                EncryptPayload = false,
                IncludeThumbnail = true
            };

            instructionSet.TransitOptions.Schedule = ScheduleOptions.SendNowAwaitResponse;
            var ctx = await _scaffold.AppApi.CreateAppAndTransferFile(TestIdentities.Samwise, instructionSet, fileMetadata, options);

            var sentFile = ctx.UploadedFile;
            var recipientAppContext = ctx.RecipientContexts.FirstOrDefault().Value;

            // 
            // On recipient identity - see that file was transferred
            // 
            var getFileByTypeResponse = await _scaffold.AppApi.QueryBatch(recipientAppContext, FileQueryParams.FromFileType(recipientAppContext.TargetDrive, someFiletype),
                QueryBatchResultOptionsRequest.Default);

            Assert.IsTrue(getFileByTypeResponse.IsSuccessStatusCode);
            Assert.IsNotNull(getFileByTypeResponse.Content);
            var recipientFileRecord = getFileByTypeResponse.Content.SearchResults.SingleOrDefault();
            Assert.IsNotNull(recipientFileRecord);

            var recipientFile = new ExternalFileIdentifier()
            {
                FileId = recipientFileRecord.FileId,
                TargetDrive = recipientAppContext.TargetDrive
            };

            var sentThumbnail = ctx.UploadFileMetadata.AppData.AdditionalThumbnails.FirstOrDefault();
            Assert.IsNotNull(sentThumbnail);
            var thumbnailResponse = await _scaffold.AppApi.GetThumbnail(recipientAppContext, recipientFile, sentThumbnail.PixelWidth, sentThumbnail.PixelHeight);
            Assert.IsTrue(thumbnailResponse.IsSuccessStatusCode);
            Assert.IsNotNull(thumbnailResponse.Content);

            var payloadResponse = await _scaffold.AppApi.GetFilePayload(recipientAppContext, recipientFile);
            Assert.IsTrue(payloadResponse.IsSuccessStatusCode);
            Assert.IsNotNull(payloadResponse.Content);


            //
            // On sender identity - see that file is not indexed and not available by direct access
            //

            var getSenderFileResponse = await _scaffold.AppApi.GetFileHeader(ctx.TestAppContext, sentFile);
            Assert.IsTrue(getSenderFileResponse.StatusCode == HttpStatusCode.NotFound, "Sender should no longer have the file since we used IsTransient");

            var getSenderThumbnailResponse = await _scaffold.AppApi.GetThumbnail(ctx.TestAppContext, ctx.UploadedFile, sentThumbnail.PixelWidth, sentThumbnail.PixelHeight);
            Assert.IsTrue(getSenderThumbnailResponse.StatusCode == HttpStatusCode.NotFound);

            var getSenderPayloadResponse = await _scaffold.AppApi.GetFilePayload(ctx.TestAppContext, ctx.UploadedFile);
            Assert.IsTrue(getSenderPayloadResponse.StatusCode == HttpStatusCode.NotFound);
        }


        [Test]
        public async Task CanDeleteFileOnRecipientServerUsingGlobalTransitId()
        {
            //on recipient identity
            //validate: recipient should have same global unique id
            //validate: client file header should have global unique id

            int someFiletype = 89994;

            var instructionSet = UploadInstructionSet.WithRecipients(TargetDrive.NewTargetDrive(), TestIdentities.Merry.OdinId);
            instructionSet.TransitOptions.UseGlobalTransitId = true;

            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = true,
                AppData = new UploadAppFileMetaData()
                {
                    FileType = someFiletype,
                    JsonContent = "this is some content",
                }
            };

            var options = new TransitTestUtilsOptions()
            {
                ProcessOutbox = true,
                ProcessTransitBox = true,
                DisconnectIdentitiesAfterTransfer = false,
                EncryptPayload = false,
                IncludeThumbnail = true
            };

            // Send the first file
            var sendFileResult = await _scaffold.AppApi.CreateAppAndTransferFile(TestIdentities.Samwise, instructionSet, fileMetadata, options);
            var senderAppContext = sendFileResult.TestAppContext;

            Assert.IsNotNull(sendFileResult.GlobalTransitId);
            Assert.IsFalse(sendFileResult.GlobalTransitId.GetValueOrDefault() == Guid.Empty);

            var firstFileSent = sendFileResult.UploadedFile;
            var recipientAppContext = sendFileResult.RecipientContexts.FirstOrDefault().Value;

            // 
            // On recipient identity - see that file was transferred
            // 
            var filesByGlobalTransitId = new FileQueryParams()
            {
                TargetDrive = recipientAppContext.TargetDrive,
                GlobalTransitId = new List<Guid>() { sendFileResult.GlobalTransitId.GetValueOrDefault() }
            };

            var getFirstFileByGlobalTransitIdResponse = await _scaffold.AppApi.QueryBatch(recipientAppContext, filesByGlobalTransitId, QueryBatchResultOptionsRequest.Default);

            Assert.IsTrue(getFirstFileByGlobalTransitIdResponse.IsSuccessStatusCode);
            Assert.IsNotNull(getFirstFileByGlobalTransitIdResponse.Content);
            var recipientFileRecord = getFirstFileByGlobalTransitIdResponse.Content.SearchResults.SingleOrDefault();
            Assert.IsNotNull(recipientFileRecord);
            Assert.IsTrue(recipientFileRecord.FileMetadata.GlobalTransitId == sendFileResult.GlobalTransitId);
            Assert.IsTrue(recipientFileRecord.FileMetadata.AppData.FileType == sendFileResult.UploadFileMetadata.AppData.FileType);
            Assert.IsNotNull(recipientFileRecord);

            // Sender should now delete the file
            await _scaffold.AppApi.DeleteFile(senderAppContext, firstFileSent, new List<TestAppContext>() { recipientAppContext });

            //
            // sender server: Should still be in index and marked as deleted
            //
            var qbResponse = await _scaffold.AppApi.QueryBatch(senderAppContext, FileQueryParams.FromFileType(senderAppContext.TargetDrive), QueryBatchResultOptionsRequest.Default);
            Assert.IsTrue(qbResponse.IsSuccessStatusCode);
            Assert.IsNotNull(qbResponse.Content);
            var qbDeleteFileEntry = qbResponse.Content.SearchResults.SingleOrDefault();
            OdinTestAssertions.FileHeaderIsMarkedDeleted(qbDeleteFileEntry, shouldHaveGlobalTransitId: true);

            // recipient server: Should still be in index and marked as deleted

            var recipientQbResponse = await _scaffold.AppApi.QueryBatch(recipientAppContext, FileQueryParams.FromFileType(recipientAppContext.TargetDrive), QueryBatchResultOptionsRequest.Default);
            Assert.IsTrue(recipientQbResponse.IsSuccessStatusCode);
            Assert.IsNotNull(recipientQbResponse.Content);
            var recipientQbDeleteFileEntry = recipientQbResponse.Content.SearchResults.SingleOrDefault();
            OdinTestAssertions.FileHeaderIsMarkedDeleted(recipientQbDeleteFileEntry, shouldHaveGlobalTransitId: true);

            await _scaffold.OldOwnerApi.DisconnectIdentities(senderAppContext.Identity, recipientAppContext.Identity);
        }

        // [Test(Description = "Ensures only the original sender of a file with a global unique identifier can make changes")]
        // public async Task WillRejectChangesFromGlobalTransitIdWhenNotFromOriginalSender()
        // {
        //     Assert.Inconclusive("WIP - testing this requires me to hack the server side and set the same global transit id");
        // }

        [Test(Description = "")]
        public async Task CanSendTransferAndRecipientCanGetFilesByTag_SendNowAwaitResponse()
        {
            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            Guid appId = Guid.NewGuid();
            var targetDrive = TargetDrive.NewTargetDrive();
            var senderContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, sender, canReadConnections: true, targetDrive, driveAllowAnonymousReads: true);
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
                    Schedule = ScheduleOptions.SendNowAwaitResponse,
                    UseGlobalTransitId = true
                }
            };

            var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
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
                        JsonContent = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
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
                    AccessControlList = new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected }
                },
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref key);

            var payloadData = "{payload:true, image:'b64 data'}";
            var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadData);

            var client = _scaffold.AppApi.CreateAppApiHttpClient(senderContext);
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
                    Assert.IsTrue(transferResult.RecipientStatus[r] == TransferStatus.DeliveredToTargetDrive, $"file was not delivered to {r}");
                }
            }

            client = _scaffold.AppApi.CreateAppApiHttpClient(recipientContext);
            {
                //First force transfers to be put into their long term location
                var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(client);
                var resp = await transitAppSvc.ProcessInbox(new ProcessInboxRequest() { TargetDrive = recipientContext.TargetDrive });
                Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);

                var driveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, recipientContext.SharedSecret);

                //lookup the fileId by the fileTag from earlier

                var queryBatchResponse = await driveSvc.GetBatch(new QueryBatchRequest()
                {
                    QueryParams = new FileQueryParams()
                    {
                        TargetDrive = recipientContext.TargetDrive,
                        TagsMatchAll = new List<Guid>() { fileTag }
                    },
                    ResultOptionsRequest = new QueryBatchResultOptionsRequest()
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

                //validate preview thumbnail
                Assert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.ContentType == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.ContentType);
                Assert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.PixelHeight == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelHeight);
                Assert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.PixelWidth == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelWidth);
                Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(descriptor.FileMetadata.AppData.PreviewThumbnail.Content, clientFileHeader.FileMetadata.AppData.PreviewThumbnail.Content));

                Assert.IsTrue(clientFileHeader.FileMetadata.AppData.AdditionalThumbnails.Count() == 2);

                //
                // Get the payload that was uploaded, test it
                // 

                var payloadResponse = await driveSvc.GetPayloadAsPost(new GetPayloadRequest() { File = uploadedFile });
                Assert.That(payloadResponse.IsSuccessStatusCode, Is.True);
                Assert.That(payloadResponse.Content, Is.Not.Null);

                var payloadResponseCipher = await payloadResponse.Content.ReadAsByteArrayAsync();
                Assert.That(((MemoryStream)payloadCipher).ToArray(), Is.EqualTo(payloadResponseCipher));

                var aesKey = decryptedKeyHeader.AesKey;
                var decryptedPayloadBytes = AesCbc.Decrypt(
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

                var thumbnailResponse1 = await driveSvc.GetThumbnailAsPost(new GetThumbnailRequest()
                {
                    File = uploadedFile,
                    Height = thumbnail1.PixelHeight,
                    Width = thumbnail1.PixelWidth
                });

                Assert.IsTrue(thumbnailResponse1.IsSuccessStatusCode);
                Assert.IsNotNull(thumbnailResponse1.Content);

                var thumbnailResponse1CipherBytes = await thumbnailResponse1!.Content!.ReadAsByteArrayAsync();
                Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnail1CipherBytes, thumbnailResponse1CipherBytes));

                //validate thumbnail 2
                Assert.IsTrue(descriptorList[1].ContentType == clientFileHeaderList[1].ContentType);
                Assert.IsTrue(descriptorList[1].PixelWidth == clientFileHeaderList[1].PixelWidth);
                Assert.IsTrue(descriptorList[1].PixelHeight == clientFileHeaderList[1].PixelHeight);

                var thumbnailResponse2 = await driveSvc.GetThumbnailAsPost(new GetThumbnailRequest()
                {
                    File = uploadedFile,
                    Height = thumbnail2.PixelHeight,
                    Width = thumbnail2.PixelWidth
                });

                Assert.IsTrue(thumbnailResponse2.IsSuccessStatusCode);
                Assert.IsNotNull(thumbnailResponse2.Content);
                var thumbnailResponse2CipherBytes = await thumbnailResponse2.Content!.ReadAsByteArrayAsync();
                Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnail2CipherBytes, thumbnailResponse2CipherBytes));

                decryptedKeyHeader.AesKey.Wipe();
                keyHeader.AesKey.Wipe();
            }

            keyHeader.AesKey.Wipe();
            key.Wipe();

            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipientContext.Identity);
        }

        [Test(Description = "")]
        public async Task CanSendTransferAndRecipientCanGetFilesByTag_Queued()
        {
            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            Guid appId = Guid.NewGuid();
            var targetDrive = TargetDrive.NewTargetDrive();
            var senderContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, sender, canReadConnections: true, targetDrive, driveAllowAnonymousReads: true);
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

            var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
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
                        JsonContent = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
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
                    AccessControlList = new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected }
                },
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref key);

            var payloadData = "{payload:true, image:'b64 data'}";
            var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadData);

            var client = _scaffold.AppApi.CreateAppApiHttpClient(senderContext);
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

            await _scaffold.OldOwnerApi.ProcessOutbox(sender.OdinId);

            client = _scaffold.AppApi.CreateAppApiHttpClient(recipientContext);
            {
                //First force transfers to be put into their long term location
                var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(client);
                // client.DefaultRequestHeaders.Add("SY4829", Guid.Parse("a1224889-c0b1-4298-9415-76332a9af80e").ToString());
                var resp = await transitAppSvc.ProcessInbox(new ProcessInboxRequest() { TargetDrive = recipientContext.TargetDrive });
                Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);

                var driveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, recipientContext.SharedSecret);

                //lookup the fileId by the fileTag from earlier

                var queryBatchResponse = await driveSvc.GetBatch(new QueryBatchRequest()
                {
                    QueryParams = new FileQueryParams()
                    {
                        TargetDrive = recipientContext.TargetDrive,
                        TagsMatchAll = new List<Guid>() { fileTag }
                    },
                    ResultOptionsRequest = new QueryBatchResultOptionsRequest()
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

                //validate preview thumbnail
                Assert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.ContentType == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.ContentType);
                Assert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.PixelHeight == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelHeight);
                Assert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.PixelWidth == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelWidth);
                Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(descriptor.FileMetadata.AppData.PreviewThumbnail.Content, clientFileHeader.FileMetadata.AppData.PreviewThumbnail.Content));

                Assert.IsTrue(clientFileHeader.FileMetadata.AppData.AdditionalThumbnails.Count() == 2);

                //
                // Get the payload that was uploaded, test it
                // 

                var payloadResponse = await driveSvc.GetPayloadAsPost(new GetPayloadRequest() { File = uploadedFile });
                Assert.That(payloadResponse.IsSuccessStatusCode, Is.True);
                Assert.That(payloadResponse.Content, Is.Not.Null);

                var payloadResponseCipher = await payloadResponse.Content.ReadAsByteArrayAsync();
                Assert.That(((MemoryStream)payloadCipher).ToArray(), Is.EqualTo(payloadResponseCipher));

                var aesKey = decryptedKeyHeader.AesKey;
                var decryptedPayloadBytes = AesCbc.Decrypt(
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

                var thumbnailResponse1 = await driveSvc.GetThumbnailAsPost(new GetThumbnailRequest()
                {
                    File = uploadedFile,
                    Height = thumbnail1.PixelHeight,
                    Width = thumbnail1.PixelWidth
                });

                Assert.IsTrue(thumbnailResponse1.IsSuccessStatusCode);
                Assert.IsNotNull(thumbnailResponse1.Content);

                var thumbnailResponse1CipherBytes = await thumbnailResponse1!.Content!.ReadAsByteArrayAsync();
                Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnail1CipherBytes, thumbnailResponse1CipherBytes));

                //validate thumbnail 2
                Assert.IsTrue(descriptorList[1].ContentType == clientFileHeaderList[1].ContentType);
                Assert.IsTrue(descriptorList[1].PixelWidth == clientFileHeaderList[1].PixelWidth);
                Assert.IsTrue(descriptorList[1].PixelHeight == clientFileHeaderList[1].PixelHeight);

                var thumbnailResponse2 = await driveSvc.GetThumbnailAsPost(new GetThumbnailRequest()
                {
                    File = uploadedFile,
                    Height = thumbnail2.PixelHeight,
                    Width = thumbnail2.PixelWidth
                });

                Assert.IsTrue(thumbnailResponse2.IsSuccessStatusCode);
                Assert.IsNotNull(thumbnailResponse2.Content);
                var thumbnailResponse2CipherBytes = await thumbnailResponse2.Content!.ReadAsByteArrayAsync();
                Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnail2CipherBytes, thumbnailResponse2CipherBytes));

                decryptedKeyHeader.AesKey.Wipe();
                keyHeader.AesKey.Wipe();
            }

            keyHeader.AesKey.Wipe();
            key.Wipe();

            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipientContext.Identity);
        }

        // [Test(Description = "Updates a thumbnail")]
        public void UpdateThumbnail()
        {
            //upload a file with a thumbnail
        }

        // [Test(Description = "Updates a thumbnail; and transfer it")]
        public void UpdateThumbnailWithTransfer()
        {
            //upload a file with a thumbnail

            //transfer the thumbnail

            //upload an updated thumbnail

            //transfer that update

            //NOTE: I think this requires supporting file collaboration in transit where I can send you updates for an existing file
        }

        //[Test(Description = "")]
        public void RecipientCanGetReceivedTransferFromDriveAndIsSearchable()
        {
            Assert.Inconclusive("TODO");
        }
    }
}