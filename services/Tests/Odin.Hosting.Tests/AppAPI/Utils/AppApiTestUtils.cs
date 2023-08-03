using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Dawn;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Registry.Registration;
using Odin.Core.Services.Transit;
using Odin.Core.Services.Transit.Encryption;
using Odin.Core.Services.Transit.ReceivingHost;
using Odin.Core.Services.Transit.SendingHost;
using Odin.Core.Storage;
using Odin.Hosting.Authentication.ClientToken;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Tests.AppAPI.ApiClient;
using Odin.Hosting.Tests.AppAPI.Drive;
using Odin.Hosting.Tests.AppAPI.Transit;
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
            var client = WebScaffold.CreateHttpClient<AppApiTestUtils>();

            //
            // SEB:NOTE below is a hack to make SharedSecretGetRequestHandler work without instance data.
            // DO NOT do this in production code!
            //
            {
                var cookieValue = $"{ClientTokenConstants.ClientAuthTokenCookieName}={token}";
                client.DefaultRequestHeaders.Add("Cookie", cookieValue);
                client.DefaultRequestHeaders.Add("X-HACK-COOKIE", cookieValue);
                client.DefaultRequestHeaders.Add("X-HACK-SHARED-SECRET", Convert.ToBase64String(sharedSecret));
            }
            
            client.DefaultRequestHeaders.Add(OdinHeaderNames.FileSystemTypeHeader, Enum.GetName(fileSystemType));
            client.Timeout = TimeSpan.FromMinutes(15);
            
            client.BaseAddress = new Uri($"https://{identity}");
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
                Alias = Guid.Parse("99888555-0000-0000-0000-000000004445"),
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
                TransitOptions = null
            };

            return (AppTransitTestUtilsContext)await CreateAppAndTransferFile(identity, instructionSet, fileMetadata,
                options ?? TransitTestUtilsOptions.Default);
        }

        public async Task<AppTransitTestUtilsContext> TransferFile(TestAppContext senderAppContext,
            Dictionary<OdinId, TestAppContext> recipientContexts,
            UploadInstructionSet instructionSet,
            UploadFileMetadata fileMetadata, TransitTestUtilsOptions options)
        {
            var recipients = instructionSet.TransitOptions?.Recipients ?? new List<string>();

            Guard.Argument(instructionSet, nameof(instructionSet)).NotNull();
            instructionSet?.AssertIsValid();

            if (options.ProcessTransitBox & (recipients.Count == 0 || options.ProcessOutbox == false))
            {
                throw new Exception(
                    "Options not valid.  There must be at least one recipient and ProcessOutbox must be true when ProcessTransitBox is set to true");
            }

            var payloadData = options?.PayloadData ?? "{payload:true, image:'b64 data'}";

            var client = this.CreateAppApiHttpClient(senderAppContext);
            {
                var keyHeader = KeyHeader.NewRandom16();
                var transferIv = instructionSet.TransferIv;

                var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
                var instructionStream = new MemoryStream(bytes);

                var sharedSecret = senderAppContext.SharedSecret.ToSensitiveByteArray();

                var thumbnails = new List<StreamPart>();
                var thumbnailsAdded = new List<ImageDataHeader>();
                if (options.IncludeThumbnail)
                {
                    var thumbnail1 = new ImageDataHeader()
                    {
                        PixelHeight = 300,
                        PixelWidth = 300,
                        ContentType = "image/jpeg"
                    };

                    var thumbnail1CipherBytes = keyHeader.EncryptDataAes(TestMedia.ThumbnailBytes300);
                    thumbnails.Add(new StreamPart(new MemoryStream(thumbnail1CipherBytes), thumbnail1.GetFilename(), thumbnail1.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail)));
                    thumbnailsAdded.Add(thumbnail1);
                    fileMetadata.AppData.AdditionalThumbnails = thumbnailsAdded;
                }

                fileMetadata.PayloadIsEncrypted = true;

                payloadData = options?.PayloadData ?? payloadData;
                if (payloadData.Length > 0)
                {
                    fileMetadata.AppData.ContentIsComplete = false;
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
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)),
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
                    Assert.IsTrue(transferResult.RecipientStatus.Count == instructionSet.TransitOptions?.Recipients.Count,
                        "expected recipient count does not match");

                    foreach (var recipient in instructionSet.TransitOptions?.Recipients)
                    {
                        Assert.IsTrue(transferResult.RecipientStatus.ContainsKey(recipient), $"Could not find matching recipient {recipient}");

                        if (instructionSet!.TransitOptions!.Schedule == ScheduleOptions.SendNowAwaitResponse)
                        {
                            Assert.IsTrue(transferResult.RecipientStatus[recipient] == TransferStatus.DeliveredToTargetDrive,
                                $"file was not delivered to {recipient}");
                        }

                        if (instructionSet.TransitOptions.Schedule == ScheduleOptions.SendLater)
                        {
                            Assert.IsTrue(transferResult.RecipientStatus[recipient] == TransferStatus.TransferKeyCreated,
                                $"transfer key not created for {recipient}");
                        }
                    }

                    batchSize = instructionSet.TransitOptions?.Recipients?.Count ?? 1;
                }

                if (options is { ProcessOutbox: true })
                {
                    await _ownerApi.ProcessOutbox(senderAppContext.Identity, batchSize);
                }

                if (options is { ProcessTransitBox: true })
                {
                    //wait for process outbox to run
                    Task.Delay(2000).Wait();

                    foreach (var rCtx in recipientContexts)
                    {
                        var rClient = this.CreateAppApiHttpClient(rCtx.Value);
                        {
                            var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(rClient);
                            var resp = await transitAppSvc.ProcessInbox(new ProcessInboxRequest() { TargetDrive = rCtx.Value.TargetDrive });
                            Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
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
                };
            }
        }


        public async Task<UploadTestUtilsContext> UploadFile(TestAppContext identityAppContext, UploadInstructionSet instructionSet,
            UploadFileMetadata fileMetadata, bool includeThumbnail,
            string payloadData)
        {
            Guard.Argument(instructionSet, nameof(instructionSet)).NotNull();
            instructionSet?.AssertIsValid();

            var client = this.CreateAppApiHttpClient(identityAppContext);
            {
                var keyHeader = KeyHeader.NewRandom16();
                var transferIv = instructionSet.TransferIv;

                var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
                var instructionStream = new MemoryStream(bytes);

                var sharedSecret = identityAppContext.SharedSecret.ToSensitiveByteArray();

                var thumbnails = new List<StreamPart>();
                var thumbnailsAdded = new List<ImageDataHeader>();
                if (includeThumbnail)
                {
                    var thumbnail1 = new ImageDataHeader()
                    {
                        PixelHeight = 300,
                        PixelWidth = 300,
                        ContentType = "image/jpeg"
                    };

                    var thumbnail1CipherBytes = keyHeader.EncryptDataAes(TestMedia.ThumbnailBytes300);
                    thumbnails.Add(new StreamPart(new MemoryStream(thumbnail1CipherBytes), thumbnail1.GetFilename(), thumbnail1.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail)));
                    thumbnailsAdded.Add(thumbnail1);
                    fileMetadata.AppData.AdditionalThumbnails = thumbnailsAdded;
                }

                fileMetadata.PayloadIsEncrypted = true;

                if (payloadData.Length > 0)
                {
                    fileMetadata.AppData.ContentIsComplete = false;
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
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)),
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
                    Assert.IsTrue(transferResult.RecipientStatus.Count == instructionSet.TransitOptions?.Recipients.Count,
                        "expected recipient count does not match");

                    foreach (var recipient in instructionSet.TransitOptions?.Recipients)
                    {
                        Assert.IsTrue(transferResult.RecipientStatus.ContainsKey(recipient), $"Could not find matching recipient {recipient}");
                        Assert.IsTrue(transferResult.RecipientStatus[recipient] == TransferStatus.TransferKeyCreated,
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

            if (options.ProcessTransitBox & (recipients.Count == 0 || options.ProcessOutbox == false))
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
                var recipient = TestIdentities.All[r];
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
            {
                var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, testAppContext.SharedSecret);
                var deleteFileResponse = await svc.DeleteFile(new DeleteFileRequest()
                {
                    File = fileId,
                    DeleteLinkedFiles = recipients2.Any(),
                    Recipients = recipients2.Select(x => x.Identity.ToString()).ToList()
                });

                Assert.IsTrue(deleteFileResponse.IsSuccessStatusCode);
                var deleteStatus = deleteFileResponse.Content;
                Assert.IsNotNull(deleteStatus);
                Assert.IsFalse(deleteStatus.LocalFileNotFound);
                Assert.IsTrue(deleteStatus.RecipientStatus.Count() == recipients2.Count());

                foreach (var (key, value) in deleteStatus.RecipientStatus)
                {
                    Assert.IsTrue(value == DeleteLinkedFileStatus.RequestAccepted, $"Delete request failed for {key}");
                }
            }

            //process the instructions on the recipients servers
            if (recipients2.Any())
            {
                foreach (var rCtx in recipients2)
                {
                    var rClient = this.CreateAppApiHttpClient(rCtx);
                    {
                        var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(rClient);
                        var resp = await transitAppSvc.ProcessInbox(new ProcessInboxRequest() { TargetDrive = rCtx.TargetDrive });
                        Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
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
                var payloadResponse = await driveSvc.GetPayloadAsPost(new GetPayloadRequest() { File = file });
                return payloadResponse;
            }
        }

        public async Task<ApiResponse<HttpContent>> GetThumbnail(TestAppContext appContext, ExternalFileIdentifier file, int width, int height)
        {
            var client = this.CreateAppApiHttpClient(appContext);
            {
                var driveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, appContext.SharedSecret);

                var thumbnailResponse = await driveSvc.GetThumbnailAsPost(new GetThumbnailRequest()
                {
                    File = file,
                    Height = height,
                    Width = width
                });

                return thumbnailResponse;
            }
        }

        public async Task<ApiResponse<QueryBatchResponse>> QueryBatch(TestAppContext appContext, FileQueryParams queryParams,
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