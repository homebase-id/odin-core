using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming;
using Odin.Services.Peer.Incoming.Drive;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Peer.Outgoing;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Registry.Registration;
using Odin.Core.Storage;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Tests.AppAPI.ApiClient;
using Odin.Hosting.Tests.AppAPI.Drive;
using Odin.Hosting.Tests.AppAPI.Transit;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Transit;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.Utils
{
    //TODO: make this a base class when everything is
    //switched to the using the client pattern
    public class AppApiTestUtils
    {
        private readonly OwnerApiTestUtils _ownerApi;

        public AppApiTestUtils(OwnerApiTestUtils ownerApi)
        {
            _ownerApi = ownerApi;
        }


        /// <summary>
        /// Creates a client for use with the app API (/api/apps/v1/...)
        /// </summary>
        public HttpClient CreateAppApiHttpClient(OdinId identity, ClientAuthenticationToken token, byte[] sharedSecret, FileSystemType fileSystemType)
        {
            var client = WebScaffold.HttpClientFactory.CreateClient(
                $"{nameof(AppApiTestUtils)}:{identity}:{WebScaffold.HttpsPort}",
                config => config.MessageHandlerChain.Add(inner => new SharedSecretGetRequestHandler(inner)));

            //
            // SEB:NOTE below is a hack to make SharedSecretGetRequestHandler work without instance data.
            // DO NOT do this in production code!
            //
            {
                var cookieValue = $"{YouAuthConstants.AppCookieName}={token}";
                client.DefaultRequestHeaders.Add("Cookie", cookieValue);
                client.DefaultRequestHeaders.Add("X-HACK-COOKIE", cookieValue);
                client.DefaultRequestHeaders.Add("X-HACK-SHARED-SECRET", Convert.ToBase64String(sharedSecret));
            }

            client.DefaultRequestHeaders.Add(OdinHeaderNames.FileSystemTypeHeader, Enum.GetName(fileSystemType));
            client.Timeout = TimeSpan.FromMinutes(15);

            client.BaseAddress = new Uri($"https://{identity}:{WebScaffold.HttpsPort}");
            return client;
        }

        public HttpClient CreateAppApiHttpClient(TestAppContext appTestContext, FileSystemType fileSystemType = FileSystemType.Standard)
        {
            return CreateAppApiHttpClient(appTestContext.Identity, appTestContext.ClientAuthenticationToken, appTestContext.SharedSecret, fileSystemType);
        }

        public HttpClient CreateAppApiHttpClient(AppClientToken token, FileSystemType fileSystemType = FileSystemType.Standard)
        {
            return CreateAppApiHttpClient(token.OdinId, token.ClientAuthToken, token.SharedSecret, fileSystemType);
        }

        public async Task<AppTransitTestUtilsContext> CreateAppAndUploadFileMetadata(TestIdentity identity, UploadFileMetadata fileMetadata,
            TransitTestUtilsOptions options = null)
        {
            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            var targetDrive = new TargetDrive()
            {
                Alias = Guid.NewGuid(),
                Type = Guid.NewGuid()
            };

            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = transferIv,
                StorageOptions = new StorageOptions()
                {
                    Drive = targetDrive,
                    OverwriteFileId = null
                },
                TransitOptions = null,
                Manifest = new UploadManifest()
                {
                    PayloadDescriptors = new List<UploadManifestPayloadDescriptor>()
                }
            };

            return (AppTransitTestUtilsContext)await CreateAppAndTransferFile(identity, instructionSet, fileMetadata,
                options ?? TransitTestUtilsOptions.Default);
        }

        public async Task<AppTransitTestUtilsContext> TransferFile(TestAppContext senderAppContext,
            Dictionary<OdinId, TestAppContext> recipientContexts,
            UploadInstructionSet instructionSet,
            UploadFileMetadata fileMetadata, TransitTestUtilsOptions options)
        {
            var keyHeader = KeyHeader.NewRandom16();

            var recipients = instructionSet.TransitOptions?.Recipients ?? new List<string>();

            instructionSet.AssertIsValid();

            if (options.ProcessInboxBox & (recipients.Count == 0 || options.ProcessOutbox == false))
            {
                throw new Exception(
                    "Options not valid. There must be at least one recipient and" +
                    " ProcessOutbox must be true when ProcessTransitBox is set to true");
            }

            var payloadData = options.PayloadData ?? "{payload:true, image:'b64 data'}";
            var payloadKey = WebScaffold.PAYLOAD_KEY;
            var client = this.CreateAppApiHttpClient(senderAppContext);
            {
                fileMetadata.IsEncrypted = true;

                if (options.IncludeThumbnail && string.IsNullOrEmpty(payloadData))
                {
                    throw new Exception("Test data error - you cannot add thumbnails w/o a payload");
                }

                var thumbnails = new List<StreamPart>();
                var thumbnailsAdded = new List<ThumbnailDescriptor>();
                var payloadDescriptors = new List<UploadManifestPayloadDescriptor>();

                if (payloadData.Length > 0)
                {
                    var thumbs = new List<UploadedManifestThumbnailDescriptor>();
                    var uploadManifestPayloadDescriptor = new UploadManifestPayloadDescriptor()
                    {
                        Iv = ByteArrayUtil.GetRndByteArray(16),
                        PayloadKey = WebScaffold.PAYLOAD_KEY,
                        Thumbnails = thumbs
                    };

                    if (options.IncludeThumbnail)
                    {
                        var thumbnail1 = new ThumbnailDescriptor()
                        {
                            PixelHeight = 300,
                            PixelWidth = 300,
                            ContentType = "image/jpeg"
                        };

                        thumbs.Add(new UploadedManifestThumbnailDescriptor()
                        {
                            PixelHeight = thumbnail1.PixelHeight,
                            PixelWidth = thumbnail1.PixelWidth,
                            ThumbnailKey = thumbnail1.GetFilename(payloadKey)
                        });

                        var thumbnail1CipherBytes = keyHeader.EncryptDataAes(TestMedia.ThumbnailBytes300);
                        thumbnails.Add(new StreamPart(new MemoryStream(thumbnail1CipherBytes), thumbnail1.GetFilename(payloadKey), thumbnail1.ContentType,
                            Enum.GetName(MultipartUploadParts.Thumbnail)));
                        thumbnailsAdded.Add(thumbnail1);
                    }

                    payloadDescriptors.Add(uploadManifestPayloadDescriptor);
                }

                var transferIv = instructionSet.TransferIv;

                instructionSet.Manifest.PayloadDescriptors ??= new List<UploadManifestPayloadDescriptor>();
                instructionSet.Manifest.PayloadDescriptors.AddRange(payloadDescriptors);

                var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
                var instructionStream = new MemoryStream(bytes);

                var sharedSecret = senderAppContext.SharedSecret.ToSensitiveByteArray();

                var descriptor = new UploadFileDescriptor()
                {
                    EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sharedSecret),
                    FileMetadata = fileMetadata
                };

                var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref sharedSecret);

                var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadData);

                var transitSvc = RestService.For<IDriveTestHttpClientForApps>(client);

                var response = await transitSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, payloadKey, "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)),
                    thumbnails.ToArray());

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var transferResult = response.Content;

                Assert.That(transferResult.File, Is.Not.Null);
                Assert.That(transferResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(transferResult.File.TargetDrive.IsValid(), Is.True);

                int batchSize = 1;
                if (instructionSet.TransitOptions?.Recipients?.Any() ?? false)
                {
                    ClassicAssert.IsTrue(transferResult.RecipientStatus.Count == instructionSet.TransitOptions?.Recipients.Count,
                        "expected recipient count does not match");

                    foreach (var recipient in instructionSet.TransitOptions?.Recipients)
                    {
                        ClassicAssert.IsTrue(transferResult.RecipientStatus.ContainsKey(recipient), $"Could not find matching recipient {recipient}");
                        ClassicAssert.IsTrue(transferResult.RecipientStatus[recipient] == TransferStatus.Enqueued);
                    }

                    batchSize = instructionSet.TransitOptions?.Recipients?.Count ?? 1;
                }

                if (options is { ProcessOutbox: true })
                {
                    var c = new TransitApiClient(_ownerApi, TestIdentities.InitializedIdentities[senderAppContext.Identity]);
                    await c.WaitForEmptyOutbox(instructionSet.StorageOptions.Drive);
                    // await _ownerApi.ProcessOutbox(senderAppContext.Identity, batchSize);
                }

                if (options is { ProcessInboxBox: true })
                {
                    foreach (var rCtx in recipientContexts)
                    {
                        var rClient = this.CreateAppApiHttpClient(rCtx.Value);
                        {
                            var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(rClient);
                            var resp = await transitAppSvc.ProcessInbox(new ProcessInboxRequest() { TargetDrive = rCtx.Value.TargetDrive });
                            ClassicAssert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
                        }
                    }
                }

                keyHeader.AesKey.Wipe();

                return new AppTransitTestUtilsContext()
                {
                    InstructionSet = instructionSet,
                    UploadFileMetadata = fileMetadata,
                    RecipientContexts = recipientContexts,
                    PayloadData = payloadData,
                    TestAppContext = senderAppContext,
                    UploadResult = transferResult,
                    GlobalTransitId = transferResult.GlobalTransitId,
                    Thumbnails = thumbnailsAdded
                };
            }
        }


        public async Task<UploadTestUtilsContext> UploadFile(TestAppContext identityAppContext, UploadInstructionSet instructionSet,
            UploadFileMetadata fileMetadata, bool includeThumbnail,
            string payloadData)
        {
            instructionSet.AssertIsValid();

            var client = this.CreateAppApiHttpClient(identityAppContext);
            {
                var keyHeader = KeyHeader.NewRandom16();
                var transferIv = instructionSet.TransferIv;

                var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
                var instructionStream = new MemoryStream(bytes);

                var sharedSecret = identityAppContext.SharedSecret.ToSensitiveByteArray();

                var thumbnails = new List<StreamPart>();
                var thumbnailsAdded = new List<ThumbnailDescriptor>();
                if (includeThumbnail)
                {
                    var thumbnail1 = new ThumbnailDescriptor()
                    {
                        PixelHeight = 300,
                        PixelWidth = 300,
                        ContentType = "image/jpeg"
                    };

                    var thumbnail1CipherBytes = keyHeader.EncryptDataAes(TestMedia.ThumbnailBytes300);
                    thumbnails.Add(new StreamPart(new MemoryStream(thumbnail1CipherBytes), thumbnail1.GetFilename(), thumbnail1.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail)));
                    thumbnailsAdded.Add(thumbnail1);
                }

                fileMetadata.IsEncrypted = true;

                if (payloadData.Length > 0)
                {
                }

                var descriptor = new UploadFileDescriptor()
                {
                    EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sharedSecret),
                    FileMetadata = fileMetadata
                };

                var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref sharedSecret);

                var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadData);

                var transitSvc = RestService.For<IDriveTestHttpClientForApps>(client);

                var response = await transitSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, WebScaffold.PAYLOAD_KEY, "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)),
                    thumbnails.ToArray());

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var transferResult = response.Content;

                Assert.That(transferResult.File, Is.Not.Null);
                Assert.That(transferResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(transferResult.File.TargetDrive.IsValid(), Is.True);

                int batchSize = 1;
                if (instructionSet.TransitOptions?.Recipients?.Any() ?? false)
                {
                    ClassicAssert.IsTrue(transferResult.RecipientStatus.Count == instructionSet.TransitOptions?.Recipients.Count,
                        "expected recipient count does not match");

                    foreach (var recipient in instructionSet.TransitOptions?.Recipients)
                    {
                        ClassicAssert.IsTrue(transferResult.RecipientStatus.ContainsKey(recipient), $"Could not find matching recipient {recipient}");
                        ClassicAssert.IsTrue(transferResult.RecipientStatus[recipient] == TransferStatus.Enqueued,
                            $"transfer key not created for {recipient}");
                    }

                    batchSize = instructionSet.TransitOptions?.Recipients?.Count ?? 1;
                }

                keyHeader.AesKey.Wipe();

                return new UploadTestUtilsContext()
                {
                    InstructionSet = instructionSet,
                    UploadFileMetadata = fileMetadata,
                    PayloadData = payloadData,
                    UploadResult = transferResult
                };
            }
        }

        public async Task<AppTransitTestUtilsContext> CreateAppAndTransferFile(TestIdentity sender, UploadInstructionSet instructionSet,
            UploadFileMetadata fileMetadata,
            TransitTestUtilsOptions options)
        {
            var recipients = instructionSet.TransitOptions?.Recipients ?? new List<string>();

            if (options.ProcessInboxBox & (recipients.Count == 0 || options.ProcessOutbox == false))
            {
                throw new Exception(
                    "Options not valid.  There must be at least one recipient and ProcessOutbox must be true when ProcessTransitBox is set to true");
            }

            var targetDrive = instructionSet.StorageOptions.Drive;

            Guid appId = Guid.NewGuid();
            var testAppContext = await _ownerApi.SetupTestSampleApp(appId, sender, canReadConnections: true, instructionSet.StorageOptions.Drive,
                options.DriveAllowAnonymousReads);

            var senderCircleDef =
                await _ownerApi.CreateCircleWithDrive(sender.OdinId, $"Sender ({sender.OdinId}) Circle",
                    permissionKeys: new List<int>() { PermissionKeys.ReadConnections },
                    drive: new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.ReadWrite
                    });

            //Setup the app on all recipient DIs
            var recipientContexts = new Dictionary<OdinId, TestAppContext>();
            foreach (var r in instructionSet.TransitOptions?.Recipients ?? new List<string>())
            {
                var recipient = TestIdentities.InitializedIdentities[r];
                var ctx = await _ownerApi.SetupTestSampleApp(testAppContext.AppId, recipient, false, testAppContext.TargetDrive);
                recipientContexts.Add(recipient.OdinId, ctx);

                var recipientCircleDef =
                    await _ownerApi.CreateCircleWithDrive(recipient.OdinId, $"Circle on {recipient} identity",
                        permissionKeys: new List<int>() { PermissionKeys.ReadConnections },
                        drive: new PermissionedDrive()
                        {
                            Drive = targetDrive,
                            Permission = DrivePermission.ReadWrite
                        });

                await _ownerApi.CreateConnection(sender.OdinId, recipient.OdinId, new CreateConnectionOptions()
                {
                    CircleIdsGrantedToSender = new List<GuidId>() { recipientCircleDef.Id },
                    CircleIdsGrantedToRecipient = new List<GuidId>() { senderCircleDef.Id }
                });
            }

            return await TransferFile(testAppContext, recipientContexts, instructionSet, fileMetadata, options);
        }

        public async Task DeleteFile(TestAppContext testAppContext, ExternalFileIdentifier fileId, List<TestAppContext> recipients = null)
        {
            var recipients2 = recipients ?? new List<TestAppContext>();
            var client = this.CreateAppApiHttpClient(testAppContext);

            var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, testAppContext.SharedSecret);
            var deleteFileResponse = await svc.DeleteFile(new DeleteFileRequest()
            {
                File = fileId,
                Recipients = recipients2.Select(x => x.Identity.ToString()).ToList()
            });

            ClassicAssert.IsTrue(deleteFileResponse.IsSuccessStatusCode);
            var deleteStatus = deleteFileResponse.Content;
            ClassicAssert.IsNotNull(deleteStatus);
            ClassicAssert.IsFalse(deleteStatus.LocalFileNotFound);
            ClassicAssert.IsTrue(deleteStatus.RecipientStatus.Count() == recipients2.Count());

            foreach (var (key, value) in deleteStatus.RecipientStatus)
            {
                ClassicAssert.IsTrue(value == DeleteLinkedFileStatus.Enqueued, $"Delete request failed for {key}");
            }

            await this._ownerApi.WaitForEmptyOutbox(testAppContext.Identity, testAppContext.TargetDrive);

            //process the instructions on the recipients servers
            if (recipients2.Any())
            {
                foreach (var rCtx in recipients2)
                {
                    var rClient = this.CreateAppApiHttpClient(rCtx);
                    {
                        var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(rClient);
                        var resp = await transitAppSvc.ProcessInbox(new ProcessInboxRequest() { TargetDrive = rCtx.TargetDrive });
                        ClassicAssert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
                    }
                }
            }
        }

        public async Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader(TestAppContext appContext, ExternalFileIdentifier file)
        {
            var client = this.CreateAppApiHttpClient(appContext);
            {
                var driveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, appContext.SharedSecret);
                var fileResponse = await driveSvc.GetFileHeaderAsPost(file);
                return fileResponse;
            }
        }

        public async Task<ApiResponse<HttpContent>> GetFilePayload(TestAppContext appContext, ExternalFileIdentifier file)
        {
            var client = this.CreateAppApiHttpClient(appContext);
            {
                var driveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, appContext.SharedSecret);
                var payloadResponse = await driveSvc.GetPayloadAsPost(new GetPayloadRequest() { File = file, Key = WebScaffold.PAYLOAD_KEY });
                return payloadResponse;
            }
        }

        public async Task<ApiResponse<HttpContent>> GetThumbnail(TestAppContext appContext, ExternalFileIdentifier file, int width, int height,
            string payloadKey)
        {
            var client = this.CreateAppApiHttpClient(appContext);
            {
                var driveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, appContext.SharedSecret);

                var thumbnailResponse = await driveSvc.GetThumbnailAsPost(new GetThumbnailRequest()
                {
                    File = file,
                    Height = height,
                    Width = width,
                    PayloadKey = payloadKey
                });

                return thumbnailResponse;
            }
        }

        public async Task<ApiResponse<QueryBatchResponse>> QueryBatch(TestAppContext appContext, FileQueryParamsV1 queryParams,
            QueryBatchResultOptionsRequest options)
        {
            var client = this.CreateAppApiHttpClient(appContext);
            {
                var driveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, appContext.SharedSecret);

                var queryBatchResponse = await driveSvc.GetBatch(new QueryBatchRequest()
                {
                    QueryParams = queryParams,
                    ResultOptionsRequest = options
                });
                return queryBatchResponse;
            }
        }
    }
}