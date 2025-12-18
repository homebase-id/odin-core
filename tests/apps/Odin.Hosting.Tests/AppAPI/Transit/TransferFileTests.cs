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
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Hosting.Tests.AppAPI.Drive;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.Transit
{
    public class TransferFileTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Frodo, TestIdentities.Samwise, TestIdentities.Merry });
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


        [Test(Description = "")]
        public async Task FailToTransferWithoutUseTransitPermission()
        {
            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            Guid appId = Guid.NewGuid();
            var targetDrive = TargetDrive.NewTargetDrive();
            var senderContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, sender, canReadConnections: true, targetDrive,
                driveAllowAnonymousReads: true, canUseTransit: false);
            var recipientContext =
                await _scaffold.OldOwnerApi.SetupTestSampleApp(senderContext.AppId, recipient, canReadConnections: true, targetDrive, canUseTransit: false);

            Guid fileTag = Guid.NewGuid();

            var senderCircleDef =
                await _scaffold.OldOwnerApi.CreateCircleWithDrive(sender.OdinId, "Sender Circle",
                    permissionKeys: [],
                    drive: new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.ReadWrite
                    });

            var recipientCircleDef =
                await _scaffold.OldOwnerApi.CreateCircleWithDrive(recipient.OdinId, "Recipient Circle",
                    permissionKeys: [],
                    drive: new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.ReadWrite
                    });

            await _scaffold.OldOwnerApi.CreateConnection(sender.OdinId, recipient.OdinId,
                createConnectionOptions: new CreateConnectionOptions()
                {
                    CircleIdsGrantedToRecipient = [senderCircleDef.Id],
                    CircleIdsGrantedToSender = [recipientCircleDef.Id]
                });

            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            var keyHeader = KeyHeader.NewRandom16();

            var payloadIv = ByteArrayUtil.GetRndByteArray(16);
            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = transferIv,
                StorageOptions = new StorageOptions()
                {
                    Drive = senderContext.TargetDrive,
                    OverwriteFileId = null
                },

                //Add recipients so system will try to send it
                TransitOptions = new TransitOptions()
                {
                    Recipients = new List<string>() { recipient.OdinId }
                },
                Manifest = new UploadManifest()
                {
                    PayloadDescriptors = new List<UploadManifestPayloadDescriptor>()
                    {
                        new UploadManifestPayloadDescriptor()
                        {
                            Iv = payloadIv,
                            PayloadKey = WebScaffold.PAYLOAD_KEY
                        }
                    }
                }
            };

            var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var key = senderContext.SharedSecret.ToSensitiveByteArray();
            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref key),
                FileMetadata = new()
                {
                    AllowDistribution = true,
                    AppData = new()
                    {
                        Tags = new List<Guid>() { fileTag },
                        Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                        PreviewThumbnail = new ThumbnailContent()
                        {
                            PixelHeight = 100,
                            PixelWidth = 100,
                            ContentType = "image/png",
                            Content = keyHeader.EncryptDataAes(TestMedia.PreviewPngThumbnailBytes)
                        }
                    },
                    IsEncrypted = true,
                    AccessControlList = new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected }
                },
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref key);

            var payloadKeyHeader = new KeyHeader()
            {
                Iv = payloadIv,
                AesKey = keyHeader.AesKey
            };

            var payloadData = "{payload:true, image:'b64 data'}";
            var payloadCipher = payloadKeyHeader.EncryptDataAesAsStream(payloadData);

            var client = _scaffold.AppApi.CreateAppApiHttpClient(senderContext);
            {
                var transitSvc = RestService.For<IDriveTestHttpClientForApps>(client);
                var response = await transitSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, WebScaffold.PAYLOAD_KEY, "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                Assert.That(response.StatusCode == HttpStatusCode.Forbidden);
            }

            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipientContext.Identity);
        }


        [Test]
        public async Task TransientFileIsDeletedAfterSending()
        {
            int someFiletype = 3892;
            var instructionSet = UploadInstructionSet.WithRecipients(TargetDrive.NewTargetDrive(), TestIdentities.Merry.OdinId);
            instructionSet.TransitOptions.IsTransient = true;

            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = true,
                AppData = new UploadAppFileMetaData()
                {
                    FileType = someFiletype,
                    Content = "this is some content",
                },
                AccessControlList = AccessControlList.Connected
            };

            var options = new TransitTestUtilsOptions()
            {
                ProcessOutbox = true,
                ProcessInboxBox = true,
                DisconnectIdentitiesAfterTransfer = true,
                EncryptPayload = false,
                IncludeThumbnail = true
            };

            var ctx = await _scaffold.AppApi.CreateAppAndTransferFile(TestIdentities.Samwise, instructionSet, fileMetadata, options);

            var sentFile = ctx.UploadedFile;
            var recipientAppContext = ctx.RecipientContexts.FirstOrDefault().Value;

            // 
            // On recipient identity - see that file was transferred
            // 
            var getFileByTypeResponse = await _scaffold.AppApi.QueryBatch(recipientAppContext,
                FileQueryParamsV1.FromFileType(recipientAppContext.TargetDrive, someFiletype),
                QueryBatchResultOptionsRequest.Default);

            ClassicAssert.IsTrue(getFileByTypeResponse.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(getFileByTypeResponse.Content);
            var recipientFileRecord = getFileByTypeResponse.Content.SearchResults.SingleOrDefault();
            ClassicAssert.IsNotNull(recipientFileRecord);

            var recipientFile = new ExternalFileIdentifier()
            {
                FileId = recipientFileRecord.FileId,
                TargetDrive = recipientAppContext.TargetDrive
            };

            var sentThumbnail = ctx.Thumbnails.FirstOrDefault();
            ClassicAssert.IsNotNull(sentThumbnail);
            var thumbnailResponse =
                await _scaffold.AppApi.GetThumbnail(recipientAppContext, recipientFile, sentThumbnail.PixelWidth, sentThumbnail.PixelHeight,
                    WebScaffold.PAYLOAD_KEY);
            ClassicAssert.IsTrue(thumbnailResponse.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(thumbnailResponse.Content);

            var payloadResponse = await _scaffold.AppApi.GetFilePayload(recipientAppContext, recipientFile);
            ClassicAssert.IsTrue(payloadResponse.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(payloadResponse.Content);


            //
            // On sender identity - see that file is not indexed and not available by direct access
            //

            var getSenderFileResponse = await _scaffold.AppApi.GetFileHeader(ctx.TestAppContext, sentFile);
            ClassicAssert.IsTrue(getSenderFileResponse.StatusCode == HttpStatusCode.NotFound, "Sender should no longer have the file since we used IsTransient");

            var getSenderThumbnailResponse =
                await _scaffold.AppApi.GetThumbnail(ctx.TestAppContext, ctx.UploadedFile, sentThumbnail.PixelWidth, sentThumbnail.PixelHeight,
                    WebScaffold.PAYLOAD_KEY);
            ClassicAssert.IsTrue(getSenderThumbnailResponse.StatusCode == HttpStatusCode.NotFound);

            var getSenderPayloadResponse = await _scaffold.AppApi.GetFilePayload(ctx.TestAppContext, ctx.UploadedFile);
            ClassicAssert.IsTrue(getSenderPayloadResponse.StatusCode == HttpStatusCode.NotFound);
        }


        [Test]
        public async Task CanDeleteFileOnRecipientServerUsingGlobalTransitId()
        {
            //on recipient identity
            //validate: recipient should have same global unique id
            //validate: client file header should have global unique id

            int someFiletype = 89994;

            var instructionSet = UploadInstructionSet.WithRecipients(TargetDrive.NewTargetDrive(), TestIdentities.Merry.OdinId);

            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = true,
                AppData = new UploadAppFileMetaData()
                {
                    FileType = someFiletype,
                    Content = "this is some content",
                },
                AccessControlList = AccessControlList.Connected
            };

            var options = new TransitTestUtilsOptions()
            {
                ProcessOutbox = true,
                ProcessInboxBox = true,
                DisconnectIdentitiesAfterTransfer = false,
                EncryptPayload = false,
                IncludeThumbnail = true
            };

            // Send the first file
            var sendFileResult = await _scaffold.AppApi.CreateAppAndTransferFile(TestIdentities.Samwise, instructionSet, fileMetadata, options);
            var senderAppContext = sendFileResult.TestAppContext;

            ClassicAssert.IsNotNull(sendFileResult.GlobalTransitId);
            ClassicAssert.IsFalse(sendFileResult.GlobalTransitId.GetValueOrDefault() == Guid.Empty);

            var firstFileSent = sendFileResult.UploadedFile;
            var recipientAppContext = sendFileResult.RecipientContexts.FirstOrDefault().Value;

            // 
            // On recipient identity - see that file was transferred
            // 
            var filesByGlobalTransitId = new FileQueryParamsV1()
            {
                TargetDrive = recipientAppContext.TargetDrive,
                GlobalTransitId = new List<Guid>() { sendFileResult.GlobalTransitId.GetValueOrDefault() }
            };

            var getFirstFileByGlobalTransitIdResponse =
                await _scaffold.AppApi.QueryBatch(recipientAppContext, filesByGlobalTransitId, QueryBatchResultOptionsRequest.Default);

            ClassicAssert.IsTrue(getFirstFileByGlobalTransitIdResponse.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(getFirstFileByGlobalTransitIdResponse.Content);
            var recipientFileRecord = getFirstFileByGlobalTransitIdResponse.Content.SearchResults.SingleOrDefault();
            ClassicAssert.IsNotNull(recipientFileRecord);
            ClassicAssert.IsTrue(recipientFileRecord.FileMetadata.GlobalTransitId == sendFileResult.GlobalTransitId);
            ClassicAssert.IsTrue(recipientFileRecord.FileMetadata.AppData.FileType == sendFileResult.UploadFileMetadata.AppData.FileType);
            ClassicAssert.IsNotNull(recipientFileRecord);

            // Sender should now delete the file
            await _scaffold.AppApi.DeleteFile(senderAppContext, firstFileSent, new List<TestAppContext>() { recipientAppContext });

            //
            // sender server: Should still be in index and marked as deleted
            //
            var qbResponse = await _scaffold.AppApi.QueryBatch(senderAppContext, FileQueryParamsV1.FromFileType(senderAppContext.TargetDrive),
                QueryBatchResultOptionsRequest.Default);
            ClassicAssert.IsTrue(qbResponse.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(qbResponse.Content);
            var qbDeleteFileEntry = qbResponse.Content.SearchResults.SingleOrDefault();
            OdinTestAssertions.FileHeaderIsMarkedDeleted(qbDeleteFileEntry, shouldHaveGlobalTransitId: true,
                SecurityGroupType.Connected); //security group should be cause that's how we sent it

            // recipient server: Should still be in index and marked as deleted

            var recipientQbResponse = await _scaffold.AppApi.QueryBatch(recipientAppContext, FileQueryParamsV1.FromFileType(recipientAppContext.TargetDrive),
                QueryBatchResultOptionsRequest.Default);
            ClassicAssert.IsTrue(recipientQbResponse.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(recipientQbResponse.Content);
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
            var senderContext =
                await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, sender, canReadConnections: true, targetDrive, driveAllowAnonymousReads: true);
            var recipientContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(senderContext.AppId, recipient, canReadConnections: true, targetDrive);

            Guid fileTag = Guid.NewGuid();

            var senderCircleDef =
                await _scaffold.OldOwnerApi.CreateCircleWithDrive(sender.OdinId, "Sender Circle",
                    permissionKeys: [],
                    drive: new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.ReadWrite
                    });

            var recipientCircleDef =
                await _scaffold.OldOwnerApi.CreateCircleWithDrive(recipient.OdinId, "Recipient Circle",
                    permissionKeys: [],
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

            var payloadIv = ByteArrayUtil.GetRndByteArray(16);
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
                    Recipients = new List<string>() { recipient.OdinId }
                },
                Manifest = new UploadManifest()
            };

            var thumbnail1 = new ThumbnailDescriptor()
            {
                PixelHeight = 300,
                PixelWidth = 300,
                ContentType = "image/jpeg"
            };
            var thumbnail1CipherBytes = keyHeader.EncryptDataAes(TestMedia.ThumbnailBytes300);

            var thumbnail2 = new ThumbnailDescriptor()
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
                    AllowDistribution = true,
                    AppData = new()
                    {
                        Tags = new List<Guid>() { fileTag },
                        Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                        PreviewThumbnail = new ThumbnailContent()
                        {
                            PixelHeight = 100,
                            PixelWidth = 100,
                            ContentType = "image/png",
                            Content = keyHeader.EncryptDataAes(TestMedia.PreviewPngThumbnailBytes)
                        }
                    },
                    IsEncrypted = true,
                    AccessControlList = new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected }
                },
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref key);

            var payloadData = "{payload:true, image:'b64 data'}";

            var payloadKeyHeader = new KeyHeader()
            {
                Iv = payloadIv,
                AesKey = keyHeader.AesKey
            };

            var payloadCipher = payloadKeyHeader.EncryptDataAesAsStream(payloadData);

            instructionSet.Manifest.PayloadDescriptors.Add(new UploadManifestPayloadDescriptor()
            {
                Iv = payloadIv,
                PayloadKey = WebScaffold.PAYLOAD_KEY,
                Thumbnails = new List<UploadedManifestThumbnailDescriptor>()
                {
                    new()
                    {
                        ThumbnailKey = thumbnail1.GetFilename(WebScaffold.PAYLOAD_KEY),
                        PixelHeight = thumbnail1.PixelHeight,
                        PixelWidth = thumbnail1.PixelWidth
                    },
                    new()
                    {
                        ThumbnailKey = thumbnail2.GetFilename(WebScaffold.PAYLOAD_KEY),
                        PixelHeight = thumbnail2.PixelHeight,
                        PixelWidth = thumbnail2.PixelWidth
                    }
                }
            });

            var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var client = _scaffold.AppApi.CreateAppApiHttpClient(senderContext);
            {
                var transitSvc = RestService.For<IDriveTestHttpClientForApps>(client);
                var response = await transitSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, WebScaffold.PAYLOAD_KEY, "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)),
                    new StreamPart(new MemoryStream(thumbnail1CipherBytes), thumbnail1.GetFilename(), thumbnail1.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail)),
                    new StreamPart(new MemoryStream(thumbnail2CipherBytes), thumbnail2.GetFilename(), thumbnail2.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail)));

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var transferResult = response.Content;

                Assert.That(transferResult.File, Is.Not.Null);
                Assert.That(transferResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                ClassicAssert.IsTrue(transferResult.File.TargetDrive.IsValid());

                foreach (var r in instructionSet.TransitOptions.Recipients)
                {
                    ClassicAssert.IsTrue(transferResult.RecipientStatus.ContainsKey(r), $"Could not find matching recipient {r}");
                    ClassicAssert.IsTrue(transferResult.RecipientStatus[r] == TransferStatus.Enqueued, $"file was not enqued for {r}");
                }
            }

            await _scaffold.OldOwnerApi.WaitForEmptyOutbox(sender.OdinId, targetDrive);

            client = _scaffold.AppApi.CreateAppApiHttpClient(recipientContext);
            {
                //First force transfers to be put into their long term location
                var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(client);
                var resp = await transitAppSvc.ProcessInbox(new ProcessInboxRequest() { TargetDrive = recipientContext.TargetDrive });
                ClassicAssert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);

                var driveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, recipientContext.SharedSecret);

                //lookup the fileId by the fileTag from earlier

                var queryBatchResponse = await driveSvc.GetBatch(new QueryBatchRequest()
                {
                    QueryParams = new FileQueryParamsV1()
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

                ClassicAssert.IsTrue(queryBatchResponse.IsSuccessStatusCode);
                ClassicAssert.IsNotNull(queryBatchResponse.Content);
                ClassicAssert.IsTrue(queryBatchResponse.Content.SearchResults.Count() == 1);

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


                CollectionAssert.AreEquivalent(clientFileHeader.FileMetadata.AppData.Tags, descriptor.FileMetadata.AppData.Tags);
                Assert.That(clientFileHeader.FileMetadata.AppData.Content, Is.EqualTo(descriptor.FileMetadata.AppData.Content));
                Assert.That(clientFileHeader.FileMetadata.Payloads.Count == 1);

                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader, Is.Not.Null);
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.Null);
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv.Length, Is.GreaterThanOrEqualTo(16));
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.EqualTo(Guid.Empty.ToByteArray()), "Iv was all zeros");
                Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes));

                var ss = recipientContext.SharedSecret.ToSensitiveByteArray();
                var decryptedKeyHeader = clientFileHeader.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ss);

                Assert.That(decryptedKeyHeader.AesKey.IsSet(), Is.True);
                ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(decryptedKeyHeader.AesKey.GetKey(), keyHeader.AesKey.GetKey()));

                //validate preview thumbnail
                ClassicAssert.IsTrue(
                    descriptor.FileMetadata.AppData.PreviewThumbnail.ContentType == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.ContentType);
                ClassicAssert.IsTrue(
                    descriptor.FileMetadata.AppData.PreviewThumbnail.PixelHeight == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelHeight);
                ClassicAssert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.PixelWidth == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelWidth);
                ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(descriptor.FileMetadata.AppData.PreviewThumbnail.Content,
                    clientFileHeader.FileMetadata.AppData.PreviewThumbnail.Content));


                ClassicAssert.IsTrue(clientFileHeader.FileMetadata.GetPayloadDescriptor(WebScaffold.PAYLOAD_KEY).Thumbnails.Count() == 2);

                //
                // Get the payload that was uploaded, test it
                // 

                var payloadResponse = await driveSvc.GetPayloadAsPost(new GetPayloadRequest() { File = uploadedFile, Key = WebScaffold.PAYLOAD_KEY });
                Assert.That(payloadResponse.IsSuccessStatusCode, Is.True);
                Assert.That(payloadResponse.Content, Is.Not.Null);

                var payloadResponseCipher = await payloadResponse.Content.ReadAsByteArrayAsync();
                Assert.That(((MemoryStream)payloadCipher).ToArray(), Is.EqualTo(payloadResponseCipher));

                var aesKey = decryptedKeyHeader.AesKey;
                var decryptedPayloadBytes = AesCbc.Decrypt(
                    cipherText: payloadResponseCipher,
                    key: aesKey,
                    iv: payloadIv);

                var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payloadData);
                Assert.That(payloadBytes, Is.EqualTo(decryptedPayloadBytes));

                // var decryptedPayloadRaw = System.Text.Encoding.UTF8.GetString(decryptedPayloadBytes);


                //
                // Validate additional thumbnails
                //

                var expectedThumbnails = new List<ThumbnailDescriptor>() { thumbnail1, thumbnail2 };
                var clientFileHeaderList = clientFileHeader.FileMetadata.GetPayloadDescriptor(WebScaffold.PAYLOAD_KEY).Thumbnails.ToList();

                //validate thumbnail 1
                ClassicAssert.IsTrue(expectedThumbnails[0].ContentType == clientFileHeaderList[0].ContentType);
                ClassicAssert.IsTrue(expectedThumbnails[0].PixelWidth == clientFileHeaderList[0].PixelWidth);
                ClassicAssert.IsTrue(expectedThumbnails[0].PixelHeight == clientFileHeaderList[0].PixelHeight);

                var thumbnailResponse1 = await driveSvc.GetThumbnailAsPost(new GetThumbnailRequest()
                {
                    File = uploadedFile,
                    Height = thumbnail1.PixelHeight,
                    Width = thumbnail1.PixelWidth,
                    PayloadKey = WebScaffold.PAYLOAD_KEY
                });

                ClassicAssert.IsTrue(thumbnailResponse1.IsSuccessStatusCode);
                ClassicAssert.IsNotNull(thumbnailResponse1.Content);

                var thumbnailResponse1CipherBytes = await thumbnailResponse1!.Content!.ReadAsByteArrayAsync();
                ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnail1CipherBytes, thumbnailResponse1CipherBytes));

                //validate thumbnail 2
                ClassicAssert.IsTrue(expectedThumbnails[1].ContentType == clientFileHeaderList[1].ContentType);
                ClassicAssert.IsTrue(expectedThumbnails[1].PixelWidth == clientFileHeaderList[1].PixelWidth);
                ClassicAssert.IsTrue(expectedThumbnails[1].PixelHeight == clientFileHeaderList[1].PixelHeight);

                var thumbnailResponse2 = await driveSvc.GetThumbnailAsPost(new GetThumbnailRequest()
                {
                    File = uploadedFile,
                    Height = thumbnail2.PixelHeight,
                    Width = thumbnail2.PixelWidth,
                    PayloadKey = WebScaffold.PAYLOAD_KEY
                });

                ClassicAssert.IsTrue(thumbnailResponse2.IsSuccessStatusCode);
                ClassicAssert.IsNotNull(thumbnailResponse2.Content);
                var thumbnailResponse2CipherBytes = await thumbnailResponse2.Content!.ReadAsByteArrayAsync();
                ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnail2CipherBytes, thumbnailResponse2CipherBytes));

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
            var senderContext =
                await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, sender, canReadConnections: true, targetDrive, driveAllowAnonymousReads: true);
            var recipientContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(senderContext.AppId, recipient, canReadConnections: true, targetDrive);

            Guid fileTag = Guid.NewGuid();

            var senderCircleDef =
                await _scaffold.OldOwnerApi.CreateCircleWithDrive(sender.OdinId, "Sender Circle",
                    permissionKeys: [],
                    drive: new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.ReadWrite
                    });

            var recipientCircleDef =
                await _scaffold.OldOwnerApi.CreateCircleWithDrive(recipient.OdinId, "Recipient Circle",
                    permissionKeys: [],
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
                    Recipients = new List<string>() { recipient.OdinId }
                },
                Manifest = new UploadManifest()
            };

            const string payloadKey = "abc333yc";

            instructionSet.Manifest.PayloadDescriptors = new List<UploadManifestPayloadDescriptor>();

            var thumbnail1 = new ThumbnailDescriptor()
            {
                PixelHeight = 300,
                PixelWidth = 300,
                ContentType = "image/jpeg"
            };
            var thumbnail1CipherBytes = keyHeader.EncryptDataAes(TestMedia.ThumbnailBytes300);

            var thumbnail2 = new ThumbnailDescriptor()
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
                    AllowDistribution = true,
                    AppData = new()
                    {
                        Tags = new List<Guid>() { fileTag },
                        Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                        PreviewThumbnail = new ThumbnailContent()
                        {
                            PixelHeight = 100,
                            PixelWidth = 100,
                            ContentType = "image/png",
                            Content = keyHeader.EncryptDataAes(TestMedia.PreviewPngThumbnailBytes)
                        }
                    },
                    IsEncrypted = true,
                    AccessControlList = new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected }
                },
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref key);

            var payloadIv = ByteArrayUtil.GetRndByteArray(16);

            var payloadKeyHeader = new KeyHeader()
            {
                Iv = payloadIv,
                AesKey = keyHeader.AesKey
            };

            var payloadData = "{payload:true, image:'b64 data'}";
            var payloadCipher = payloadKeyHeader.EncryptDataAesAsStream(payloadData);

            instructionSet.Manifest.PayloadDescriptors.Add(new UploadManifestPayloadDescriptor()
            {
                Iv = payloadIv,
                PayloadKey = payloadKey,
                Thumbnails = new List<UploadedManifestThumbnailDescriptor>()
                {
                    new()
                    {
                        ThumbnailKey = thumbnail1.GetFilename(payloadKey),
                        PixelHeight = thumbnail1.PixelHeight,
                        PixelWidth = thumbnail1.PixelWidth
                    },
                    new()
                    {
                        ThumbnailKey = thumbnail2.GetFilename(payloadKey),
                        PixelHeight = thumbnail2.PixelHeight,
                        PixelWidth = thumbnail2.PixelWidth
                    }
                }
            });

            var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var client = _scaffold.AppApi.CreateAppApiHttpClient(senderContext);
            {
                var transitSvc = RestService.For<IDriveTestHttpClientForApps>(client);
                var response = await transitSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, payloadKey, "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)),
                    new StreamPart(new MemoryStream(thumbnail1CipherBytes), thumbnail1.GetFilename(payloadKey), thumbnail1.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail)),
                    new StreamPart(new MemoryStream(thumbnail2CipherBytes), thumbnail2.GetFilename(payloadKey), thumbnail2.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail)));

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var transferResult = response.Content;

                Assert.That(transferResult.File, Is.Not.Null);
                Assert.That(transferResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                ClassicAssert.IsTrue(transferResult.File.TargetDrive.IsValid());

                foreach (var r in instructionSet.TransitOptions.Recipients)
                {
                    ClassicAssert.IsTrue(transferResult.RecipientStatus.ContainsKey(r), $"Could not find matching recipient {r}");
                    ClassicAssert.IsTrue(transferResult.RecipientStatus[r] == TransferStatus.Enqueued, $"transfer key not created for {r}");
                }
            }

            await _scaffold.OldOwnerApi.WaitForEmptyOutbox(sender.OdinId, targetDrive);

            client = _scaffold.AppApi.CreateAppApiHttpClient(recipientContext);
            {
                //First force transfers to be put into their long term location
                var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(client);
                var resp = await transitAppSvc.ProcessInbox(new ProcessInboxRequest() { TargetDrive = recipientContext.TargetDrive });
                ClassicAssert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);

                var driveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, recipientContext.SharedSecret);

                //lookup the fileId by the fileTag from earlier

                var queryBatchResponse = await driveSvc.GetBatch(new QueryBatchRequest()
                {
                    QueryParams = new FileQueryParamsV1()
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

                ClassicAssert.IsTrue(queryBatchResponse.IsSuccessStatusCode);
                ClassicAssert.IsNotNull(queryBatchResponse.Content);
                ClassicAssert.IsTrue(queryBatchResponse.Content.SearchResults.Count() == 1,
                    $"result should have been 1 but was {queryBatchResponse.Content.SearchResults.Count()}");
                var theFile = queryBatchResponse.Content.SearchResults.Single();
                var uploadedFile = new ExternalFileIdentifier()
                {
                    TargetDrive = recipientContext.TargetDrive,
                    FileId = theFile.FileId
                };

                var fileResponse = await driveSvc.GetFileHeaderAsPost(uploadedFile);

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

                var ss = recipientContext.SharedSecret.ToSensitiveByteArray();
                var decryptedKeyHeader = clientFileHeader.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ss);

                Assert.That(decryptedKeyHeader.AesKey.IsSet(), Is.True);
                ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(decryptedKeyHeader.AesKey.GetKey(), keyHeader.AesKey.GetKey()));

                //validate preview thumbnail
                ClassicAssert.IsTrue(
                    descriptor.FileMetadata.AppData.PreviewThumbnail.ContentType == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.ContentType);
                ClassicAssert.IsTrue(
                    descriptor.FileMetadata.AppData.PreviewThumbnail.PixelHeight == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelHeight);
                ClassicAssert.IsTrue(descriptor.FileMetadata.AppData.PreviewThumbnail.PixelWidth == clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelWidth);
                ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(descriptor.FileMetadata.AppData.PreviewThumbnail.Content,
                    clientFileHeader.FileMetadata.AppData.PreviewThumbnail.Content));

                ClassicAssert.IsTrue(clientFileHeader.FileMetadata.GetPayloadDescriptor(payloadKey).Thumbnails.Count() == 2);

                //
                // Get the payload that was uploaded, test it
                // 

                var payloadResponse = await driveSvc.GetPayloadAsPost(new GetPayloadRequest() { Key = payloadKey, File = uploadedFile });
                Assert.That(payloadResponse.IsSuccessStatusCode, Is.True);
                Assert.That(payloadResponse.Content, Is.Not.Null);

                var payloadResponseCipher = await payloadResponse.Content.ReadAsByteArrayAsync();
                Assert.That(((MemoryStream)payloadCipher).ToArray(), Is.EqualTo(payloadResponseCipher));

                var aesKey = decryptedKeyHeader.AesKey;
                var decryptedPayloadBytes = AesCbc.Decrypt(
                    cipherText: payloadResponseCipher,
                    key: aesKey,
                    iv: payloadIv);

                var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payloadData);
                Assert.That(payloadBytes, Is.EqualTo(decryptedPayloadBytes));

                // var decryptedPayloadRaw = System.Text.Encoding.UTF8.GetString(decryptedPayloadBytes);


                //
                // Validate additional thumbnails
                //

                var descriptorList = new List<ThumbnailDescriptor>() { thumbnail1, thumbnail2 };
                var clientFileHeaderList = clientFileHeader.FileMetadata.GetPayloadDescriptor(payloadKey).Thumbnails.ToList();

                //validate thumbnail 1
                ClassicAssert.IsTrue(descriptorList[0].ContentType == clientFileHeaderList[0].ContentType);
                ClassicAssert.IsTrue(descriptorList[0].PixelWidth == clientFileHeaderList[0].PixelWidth);
                ClassicAssert.IsTrue(descriptorList[0].PixelHeight == clientFileHeaderList[0].PixelHeight);

                var thumbnailResponse1 = await driveSvc.GetThumbnailAsPost(new GetThumbnailRequest()
                {
                    File = uploadedFile,
                    Height = thumbnail1.PixelHeight,
                    Width = thumbnail1.PixelWidth,
                    PayloadKey = payloadKey
                });

                ClassicAssert.IsTrue(thumbnailResponse1.IsSuccessStatusCode);
                ClassicAssert.IsNotNull(thumbnailResponse1.Content);

                var thumbnailResponse1CipherBytes = await thumbnailResponse1!.Content!.ReadAsByteArrayAsync();
                ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnail1CipherBytes, thumbnailResponse1CipherBytes));

                //validate thumbnail 2
                ClassicAssert.IsTrue(descriptorList[1].ContentType == clientFileHeaderList[1].ContentType);
                ClassicAssert.IsTrue(descriptorList[1].PixelWidth == clientFileHeaderList[1].PixelWidth);
                ClassicAssert.IsTrue(descriptorList[1].PixelHeight == clientFileHeaderList[1].PixelHeight);

                var thumbnailResponse2 = await driveSvc.GetThumbnailAsPost(new GetThumbnailRequest()
                {
                    File = uploadedFile,
                    Height = thumbnail2.PixelHeight,
                    Width = thumbnail2.PixelWidth,
                    PayloadKey = payloadKey
                });

                ClassicAssert.IsTrue(thumbnailResponse2.IsSuccessStatusCode);
                ClassicAssert.IsNotNull(thumbnailResponse2.Content);
                var thumbnailResponse2CipherBytes = await thumbnailResponse2.Content!.ReadAsByteArrayAsync();
                ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnail2CipherBytes, thumbnailResponse2CipherBytes));

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