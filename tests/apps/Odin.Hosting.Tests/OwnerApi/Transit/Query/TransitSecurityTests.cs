using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.OwnerApi.Transit.Query
{
    public class TransitSecurityTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var folder = GetType().Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Frodo, TestIdentities.Samwise });
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
        public async Task ExchangeGrantHasNoStorageKey_WhenDrivePermissionReadIsNotGranted()
        {
            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClient(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClient(recipient);

            var senderChatDrive = await senderOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Chat drive",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false);

            var expectedPermissionedDrive = new PermissionedDrive()
            {
                Drive = senderChatDrive.TargetDriveInfo,
                Permission = DrivePermission.Write & DrivePermission.WriteReactionsAndComments
            };

            var senderChatCircle = await senderOwnerClient.Membership.CreateCircle("Chat Participants", new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = expectedPermissionedDrive
                    }
                }
            });

            await senderOwnerClient.Network.SendConnectionRequestTo(recipient, new List<GuidId>() { senderChatCircle.Id });
            await recipientOwnerClient.Network.AcceptConnectionRequest(sender, new List<GuidId>() { });

            // Test
            // At this point: recipient should have an ICR record on sender's identity that does not have a key
            // 

            var recipientConnectionInfo = await senderOwnerClient.Network.GetConnectionInfo(recipient);

            ClassicAssert.IsNotNull(recipientConnectionInfo.AccessGrant.CircleGrants.SingleOrDefault(cg =>
                cg.DriveGrants.Any(dg => dg.PermissionedDrive == senderChatCircle.DriveGrants.Single().PermissionedDrive)));

            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipient.OdinId);
        }

        [Test]
        public async Task WhenSenderOnlyHasWriteAccess_RecipientSendsToInbox()
        {
            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClient(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClient(recipient);

            var senderChatDrive = await senderOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Chat drive",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false);

            var expectedPermissionedDrive = new PermissionedDrive()
            {
                Drive = senderChatDrive.TargetDriveInfo,
                Permission = DrivePermission.Write & DrivePermission.WriteReactionsAndComments
            };

            var senderChatCircle = await senderOwnerClient.Membership.CreateCircle("Chat Participants", new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = expectedPermissionedDrive
                    }
                }
            });

            await senderOwnerClient.Network.SendConnectionRequestTo(recipient, new List<GuidId>() { senderChatCircle.Id });
            await recipientOwnerClient.Network.AcceptConnectionRequest(sender, new List<GuidId>() { });

            // Test
            // At this point: recipient should have an ICR record on sender's identity that does not have a key
            // 

            var recipientConnectionInfo = await senderOwnerClient.Network.GetConnectionInfo(recipient);

            ClassicAssert.IsNotNull(recipientConnectionInfo.AccessGrant.CircleGrants.SingleOrDefault(cg =>
                cg.DriveGrants.Any(dg => dg.PermissionedDrive == senderChatCircle.DriveGrants.Single().PermissionedDrive)));

            //await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, sender, canReadConnections: true, targetDrive, driveAllowAnonymousReads: true);
            // var recipientContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(senderContext.AppId, recipient, canReadConnections: true, targetDrive);

            //
            // var transferIv = ByteArrayUtil.GetRndByteArray(16);
            // var keyHeader = KeyHeader.NewRandom16();
            //
            // var instructionSet = new UploadInstructionSet()
            // {
            //     TransferIv = transferIv,
            //     StorageOptions = new StorageOptions()
            //     {
            //         Drive = senderChatDrive.TargetDrive,
            //         OverwriteFileId = null
            //     },
            //
            //     TransitOptions = new TransitOptions()
            //     {
            //         Recipients = new List<string>() { recipient.OdinId },
            //     }
            // };
            //
            // var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
            // var instructionStream = new MemoryStream(bytes);
            //
            // var key = senderContext.SharedSecret.ToSensitiveByteArray();
            // var json = DotYouSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" });
            // var encryptedJsonContent64 = keyHeader.EncryptDataAesAsStream(json).ToByteArray().ToBase64();
            //
            // var thumbnail1 = new ImageDataHeader()
            // {
            //     PixelHeight = 300,
            //     PixelWidth = 300,
            //     ContentType = "image/jpeg"
            // };
            // var thumbnail1OriginalBytes = TestMedia.ThumbnailBytes300;
            // var thumbnail1CipherBytes = keyHeader.EncryptDataAes(thumbnail1OriginalBytes);
            //
            // var descriptor = new UploadFileDescriptor()
            // {
            //     EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref key),
            //     FileMetadata = new()
            //     {
            //         ContentType = "application/json",
            //         AllowDistribution = true,
            //         AppData = new()
            //         {
            //             Tags = new List<Guid>() { fileTag },
            //             ContentIsComplete = false,
            //             JsonContent = encryptedJsonContent64,
            //             AdditionalThumbnails = new[] { thumbnail1 }
            //         },
            //         PayloadIsEncrypted = true,
            //         AccessControlList = new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected }
            //     },
            // };
            //
            // var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref key);
            //
            // var originalPayloadData = "{payload:true, image:'b64 data'}";
            // var originalPayloadCipherBytes = keyHeader.EncryptDataAesAsStream(originalPayloadData);
            //
            // //
            // // upload and send the file 
            // //
            // var client = _scaffold.AppApi.CreateAppApiHttpClient(senderContext))
            // {
            //     var transitSvc = RestService.For<IDriveTestHttpClientForApps>(client);
            //     var response = await transitSvc.Upload(
            //         new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
            //         new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
            //         new StreamPart(originalPayloadCipherBytes, "", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)),
            //         new StreamPart(new MemoryStream(thumbnail1CipherBytes), thumbnail1.GetFilename(), thumbnail1.ContentType,
            //             Enum.GetName(MultipartUploadParts.Thumbnail)));
            //
            //     Assert.That(response.IsSuccessStatusCode, Is.True);
            //     Assert.That(response.Content, Is.Not.Null);
            //     var transferResult = response.Content;
            //
            //     Assert.That(transferResult.File, Is.Not.Null);
            //     Assert.That(transferResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
            //     ClassicAssert.IsTrue(transferResult.File.TargetDrive.IsValid());
            //
            //     foreach (var r in instructionSet.TransitOptions.Recipients)
            //     {
            //         ClassicAssert.IsTrue(transferResult.RecipientStatus.ContainsKey(r), $"Could not find matching recipient {r}");
            //         ClassicAssert.IsTrue(transferResult.RecipientStatus[r] == TransferStatus.TransferKeyCreated, $"transfer key not created for {r}");
            //     }
            // }
            //
            // await _scaffold.OldOwnerApi.ProcessOutbox(sender.OdinId);
            //
            // ExternalFileIdentifier uploadedFile;
            // var fileTagQueryParams = new FileQueryParams()
            // {
            //     TargetDrive = recipientContext.TargetDrive,
            //     TagsMatchAll = new List<Guid>() { fileTag }
            // };
            //
            // //
            // // Validate recipient got the file
            // //
            // var client = _scaffold.AppApi.CreateAppApiHttpClient(recipientContext))
            // {
            //     //First force transfers to be put into their long term location
            //     var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(client);
            //     var resp = await transitAppSvc.ProcessIncomingInstructions(
            //         new ProcessTransitInstructionRequest() { TargetDrive = recipientContext.TargetDrive });
            //     ClassicAssert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
            //
            //     var driveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, recipientContext.SharedSecret);
            //
            //     //lookup the fileId by the fileTag from earlier
            //     var queryBatchResponse = await driveSvc.QueryBatch(new QueryBatchRequest()
            //     {
            //         QueryParams = fileTagQueryParams,
            //         ResultOptionsRequest = new QueryBatchResultOptionsRequest()
            //         {
            //             MaxRecords = 1,
            //             IncludeMetadataHeader = true
            //         }
            //     });
            //
            //     ClassicAssert.IsTrue(queryBatchResponse.IsSuccessStatusCode);
            //     ClassicAssert.IsNotNull(queryBatchResponse.Content);
            //     ClassicAssert.IsTrue(queryBatchResponse.Content.SearchResults.Count() == 1);
            //
            //     uploadedFile = new ExternalFileIdentifier()
            //     {
            //         TargetDrive = recipientContext.TargetDrive,
            //         FileId = queryBatchResponse.Content.SearchResults.Single().FileId
            //     };
            //
            //     var fileResponse = await driveSvc.GetFileHeaderAsPost(uploadedFile);
            //
            //     Assert.That(fileResponse.IsSuccessStatusCode, Is.True);
            //     Assert.That(fileResponse.Content, Is.Not.Null);
            //
            //     var clientFileHeader = fileResponse.Content;
            //
            //     Assert.That(clientFileHeader.FileMetadata, Is.Not.Null);
            //     Assert.That(clientFileHeader.FileMetadata.AppData, Is.Not.Null);
            //
            //     CollectionAssert.AreEquivalent(clientFileHeader.FileMetadata.AppData.Tags, descriptor.FileMetadata.AppData.Tags);
            //     Assert.That(clientFileHeader.FileMetadata.AppData.JsonContent, Is.EqualTo(descriptor.FileMetadata.AppData.JsonContent));
            //     Assert.That(clientFileHeader.FileMetadata.AppData.ContentIsComplete, Is.EqualTo(descriptor.FileMetadata.AppData.ContentIsComplete));
            //
            //     Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader, Is.Not.Null);
            //     Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.Null);
            //     Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv.Length, Is.GreaterThanOrEqualTo(16));
            //     Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.EqualTo(Guid.Empty.ToByteArray()), "Iv was all zeros");
            //     Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes));
            //
            //     var ss = recipientContext.SharedSecret.ToSensitiveByteArray();
            //     var decryptedKeyHeader = clientFileHeader.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ss);
            //
            //     Assert.That(decryptedKeyHeader.AesKey.IsSet(), Is.True);
            //     ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(decryptedKeyHeader.AesKey.GetKey(), keyHeader.AesKey.GetKey()));
            //
            //     //
            //     // Get the payload that was uploaded, test it
            //     // 
            //
            //     var payloadResponse = await driveSvc.GetPayloadAsPost(uploadedFile);
            //     Assert.That(payloadResponse.IsSuccessStatusCode, Is.True);
            //     Assert.That(payloadResponse.Content, Is.Not.Null);
            //
            //     var payloadResponseCipher = await payloadResponse.Content.ReadAsByteArrayAsync();
            //     Assert.That(((MemoryStream)originalPayloadCipherBytes).ToArray(), Is.EqualTo(payloadResponseCipher));
            //
            //     var aesKey = decryptedKeyHeader.AesKey;
            //     var decryptedPayloadBytes = Core.Cryptography.Crypto.AesCbc.Decrypt(
            //         cipherText: payloadResponseCipher,
            //         Key: ref aesKey,
            //         IV: decryptedKeyHeader.Iv);
            //
            //     var payloadBytes = System.Text.Encoding.UTF8.GetBytes(originalPayloadData);
            //     Assert.That(payloadBytes, Is.EqualTo(decryptedPayloadBytes));
            //
            //     // var decryptedPayloadRaw = System.Text.Encoding.UTF8.GetString(decryptedPayloadBytes);
            //
            //     var getThumbnailResponse = await driveSvc.GetThumbnailAsPost(new GetThumbnailRequest()
            //     {
            //         File = uploadedFile,
            //         Width = thumbnail1.PixelWidth,
            //         Height = thumbnail1.PixelHeight
            //     });
            //
            //     ClassicAssert.IsTrue(getThumbnailResponse.IsSuccessStatusCode);
            //     var getThumbnailResponseBytes = await getThumbnailResponse.Content!.ReadAsByteArrayAsync();
            //     ClassicAssert.IsNotNull(thumbnail1CipherBytes.Length == getThumbnailResponseBytes.Length);
            //
            //     decryptedKeyHeader.AesKey.Wipe();
            //     // keyHeader.AesKey.Wipe();
            // }
            //
            //
            // // reupload the file on the recipient's identity and set the filemetadata to authenticated.
            // // this is done because when files are received, they are set to owner only on the recipients identity
            // instructionSet.StorageOptions = new StorageOptions()
            // {
            //     OverwriteFileId = uploadedFile.FileId,
            //     Drive = uploadedFile.TargetDrive
            // };
            //
            // instructionSet.TransitOptions = null;
            // descriptor.FileMetadata.AllowDistribution = false;
            //
            // var reuploadedContext = await _scaffold.OldOwnerApi.UploadFile(recipient.OdinId, instructionSet, descriptor.FileMetadata, originalPayloadData, true,
            //     new ImageDataContent()
            //     {
            //         ContentType = thumbnail1.ContentType,
            //         PixelHeight = thumbnail1.PixelHeight,
            //         PixelWidth = thumbnail1.PixelWidth,
            //         Content = thumbnail1OriginalBytes
            //     }, keyHeader);
            //
            // //
            // //  The final test - use transit query batch for the sender to get the file on the recipients identity over transit
            // //
            // var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sender, out var ownerSharedSecret))
            // {
            //     var transitQueryService = RefitCreator.RestServiceFor<ITransitQueryHttpClientForOwner>(client, ownerSharedSecret);
            //
            //     //
            //     // Test the file matches the original one sent
            //     //
            //     var getTransitFileResponse = await transitQueryService.GetFileHeader(new TransitExternalFileIdentifier()
            //     {
            //         OdinId = recipient.OdinId,
            //         File = uploadedFile
            //     });
            //
            //     ClassicAssert.IsTrue(getTransitFileResponse.IsSuccessStatusCode);
            //     ClassicAssert.IsNotNull(getTransitFileResponse.Content);
            //
            //     var fileResponse = getTransitFileResponse.Content;
            //     ClassicAssert.IsTrue(uploadedFile.FileId == fileResponse.FileId);
            //
            //     var transitClientFileHeader = getTransitFileResponse.Content;
            //
            //     Assert.That(transitClientFileHeader.FileMetadata, Is.Not.Null);
            //     Assert.That(transitClientFileHeader.FileMetadata.AppData, Is.Not.Null);
            //
            //     CollectionAssert.AreEquivalent(transitClientFileHeader.FileMetadata.AppData.Tags, descriptor.FileMetadata.AppData.Tags);
            //     Assert.That(transitClientFileHeader.FileMetadata.AppData.JsonContent, Is.EqualTo(descriptor.FileMetadata.AppData.JsonContent));
            //     Assert.That(transitClientFileHeader.FileMetadata.AppData.ContentIsComplete, Is.EqualTo(descriptor.FileMetadata.AppData.ContentIsComplete));
            //
            //     Assert.That(transitClientFileHeader.SharedSecretEncryptedKeyHeader, Is.Not.Null);
            //     Assert.That(transitClientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.Null);
            //     Assert.That(transitClientFileHeader.SharedSecretEncryptedKeyHeader.Iv.Length, Is.GreaterThanOrEqualTo(16));
            //     Assert.That(transitClientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.EqualTo(Guid.Empty.ToByteArray()), "Iv was all zeros");
            //     Assert.That(transitClientFileHeader.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes));
            //
            //     var decryptedClientFileKeyHeader = transitClientFileHeader.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ownerSharedSecret);
            //
            //     Assert.That(decryptedClientFileKeyHeader.AesKey.IsSet(), Is.True);
            //     ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(decryptedClientFileKeyHeader.AesKey.GetKey(), keyHeader.AesKey.GetKey()));
            //
            //     //
            //     // Get the payload that was sent to the recipient via transit, test it
            //     // has the decrypted data content type
            //     // can be decrypted using the owner shared secret encrypted key header
            //     var getTransitPayloadResponse = await transitQueryService.GetPayload(new TransitExternalFileIdentifier()
            //     {
            //         OdinId = recipient.OdinId,
            //         File = uploadedFile
            //     });
            //
            //     Assert.That(getTransitPayloadResponse.IsSuccessStatusCode, Is.True);
            //     Assert.That(getTransitPayloadResponse.Content, Is.Not.Null);
            //
            //     var payloadIsEncrypted = bool.Parse(getTransitPayloadResponse.Headers.GetValues(HttpHeaderConstants.PayloadEncrypted).Single());
            //     ClassicAssert.IsTrue(payloadIsEncrypted);
            //
            //     var payloadSharedSecretKeyHeaderValue = getTransitPayloadResponse.Headers.GetValues(HttpHeaderConstants.SharedSecretEncryptedHeader64).Single();
            //     var ownerSharedSecretEncryptedKeyHeaderForPayload = EncryptedKeyHeader.FromBase64(payloadSharedSecretKeyHeaderValue);
            //
            //     var getTransitPayloadContentTypeHeader = getTransitPayloadResponse.Headers.GetValues(HttpHeaderConstants.DecryptedContentType).Single();
            //     ClassicAssert.IsTrue(descriptor.FileMetadata.ContentType == getTransitPayloadContentTypeHeader);
            //
            //     var decryptedPayloadKeyHeader = ownerSharedSecretEncryptedKeyHeaderForPayload.DecryptAesToKeyHeader(ref ownerSharedSecret);
            //     var payloadResponseCipherBytes = await getTransitPayloadResponse.Content.ReadAsByteArrayAsync();
            //     ClassicAssert.IsTrue(reuploadedContext.PayloadCipher.Length == payloadResponseCipherBytes.Length);
            //     var decryptedPayloadBytes = decryptedPayloadKeyHeader.Decrypt(payloadResponseCipherBytes);
            //     ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(originalPayloadData.ToUtf8ByteArray(), decryptedPayloadBytes));
            //
            //     //
            //     // Get the thumbnail that was sent to the recipient via transit, test it
            //     // can get the thumbnail that as uploaded and sent
            //     // can decrypt the thumbnail using the owner shared secret encrypted keyheader
            //     // 
            //     var getTransitThumbnailResponse = await transitQueryService.GetThumbnail(new TransitGetThumbRequest()
            //     {
            //         OdinId = recipient.OdinId,
            //         File = uploadedFile,
            //         Width = thumbnail1.PixelWidth,
            //         Height = thumbnail1.PixelHeight
            //     });
            //
            //     ClassicAssert.IsTrue(getTransitThumbnailResponse.IsSuccessStatusCode);
            //     ClassicAssert.IsNotNull(getTransitThumbnailResponse.Content);
            //
            //     var thumbnailIsEncrypted = bool.Parse(getTransitThumbnailResponse.Headers.GetValues(HttpHeaderConstants.PayloadEncrypted).Single());
            //     ClassicAssert.IsTrue(thumbnailIsEncrypted);
            //
            //     var thumbnailSharedSecretKeyHeaderValue =
            //         getTransitThumbnailResponse.Headers.GetValues(HttpHeaderConstants.SharedSecretEncryptedHeader64).Single();
            //     var ownerSharedSecretEncryptedKeyHeaderForThumbnail = EncryptedKeyHeader.FromBase64(thumbnailSharedSecretKeyHeaderValue);
            //
            //     var getTransitThumbnailContentTypeHeader = getTransitThumbnailResponse.Headers.GetValues(HttpHeaderConstants.DecryptedContentType).Single();
            //     ClassicAssert.IsTrue(thumbnail1.ContentType == getTransitThumbnailContentTypeHeader);
            //
            //     var decryptedThumbnailKeyHeader = ownerSharedSecretEncryptedKeyHeaderForThumbnail.DecryptAesToKeyHeader(ref ownerSharedSecret);
            //     var transitThumbnailResponse1CipherBytes = await getTransitThumbnailResponse!.Content!.ReadAsByteArrayAsync();
            //     var decryptedThumbnailBytes = decryptedThumbnailKeyHeader.Decrypt(transitThumbnailResponse1CipherBytes);
            //     ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(thumbnail1OriginalBytes, decryptedThumbnailBytes));
            // }
            //
            // keyHeader.AesKey.Wipe();
            // key.Wipe();
            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipient.OdinId);
        }
    }
}