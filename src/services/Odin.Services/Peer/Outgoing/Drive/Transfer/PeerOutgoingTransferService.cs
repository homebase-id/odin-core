using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator.Outbox;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Util;
using Refit;
using Serilog;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer
{
    public class PeerOutgoingOutgoingTransferService(
        OdinContextAccessor contextAccessor,
        PeerOutbox peerOutbox,
        TenantSystemStorage tenantSystemStorage,
        IOdinHttpClientFactory odinHttpClientFactory,
        TenantContext tenantContext,
        CircleNetworkService circleNetworkService,
        DriveManager driveManager,
        FileSystemResolver fileSystemResolver,
        OdinConfiguration odinConfiguration,
        IDriveAclAuthorizationService driveAclAuthorizationService,
        ServerSystemStorage serverSystemStorage,
        ILogger<PeerOutgoingOutgoingTransferService> logger,
        IMediator mediator)
        : PeerServiceBase(odinHttpClientFactory, circleNetworkService,
            contextAccessor, fileSystemResolver), IPeerOutgoingTransferService
    {
        private readonly FileSystemResolver _fileSystemResolver = fileSystemResolver;
        private readonly TransferKeyEncryptionQueueService _transferKeyEncryptionQueueService = new(tenantSystemStorage);
        private readonly IOdinHttpClientFactory _odinHttpClientFactory = odinHttpClientFactory;

        public async Task<Dictionary<string, TransferStatus>> SendFile(InternalDriveFileId internalFile, TransitOptions options,
            TransferFileType transferFileType, FileSystemType fileSystemType)
        {
            OdinContext.PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitWrite);

            OdinValidationUtils.AssertIsTrue(options.Recipients.TrueForAll(r => r != tenantContext.HostOdinId), "You cannot send a file to yourself");
            OdinValidationUtils.AssertValidRecipientList(options.Recipients);

            var sfo = new FileTransferOptions()
            {
                TransferFileType = transferFileType,
                FileSystemType = fileSystemType
            };

            var tenant = OdinContext.Tenant;
            serverSystemStorage.EnqueueJob(tenant, CronJobType.ReconcileInboxOutbox, tenant.DomainName.ToLower().ToUtf8ByteArray(), UnixTimeUtc.Now());

            var priority = options.Schedule == ScheduleOptions.SendNowAwaitResponse ? 100 : 200;
            var outboxStatus = await EnqueueOutboxItems(internalFile, options, sfo, priority, OutboxItemType.File);

            // note: if this fires in a background thread, i lose access to context so i need t pass it all in
            // var _ = ProcessDriveOutbox(internalFile.DriveId);
            //TODO: need to send these in parallel threads now
            await ProcessDriveOutbox(internalFile.DriveId); //TODO work with seb to sort out how to multi-thread this

            return await MapOutboxCreationResult(outboxStatus);
        }

        public async Task<Dictionary<string, DeleteLinkedFileStatus>> SendDeleteFileRequest(GlobalTransitIdFileIdentifier remoteGlobalTransitIdentifier,
            FileTransferOptions fileTransferOptions, IEnumerable<string> recipients)
        {
            var result = new Dictionary<string, DeleteLinkedFileStatus>();

            foreach (var recipient in recipients)
            {
                var r = (OdinId)recipient;

                var clientAccessToken = await ResolveClientAccessToken(r);

                var client = _odinHttpClientFactory.CreateClientUsingAccessToken<IPeerTransferHttpClient>(r, clientAccessToken.ToAuthenticationToken(),
                    fileSystemType: fileTransferOptions.FileSystemType);

                ApiResponse<PeerTransferResponse> httpResponse = null;

                await TryRetry.WithDelayAsync(
                    odinConfiguration.Host.PeerOperationMaxAttempts,
                    odinConfiguration.Host.PeerOperationDelayMs,
                    CancellationToken.None,
                    async () =>
                    {
                        httpResponse = await client.DeleteLinkedFile(new DeleteRemoteFileRequest()
                        {
                            RemoteGlobalTransitIdFileIdentifier = remoteGlobalTransitIdentifier,
                            FileSystemType = fileTransferOptions.FileSystemType
                        });
                    });

                if (httpResponse.IsSuccessStatusCode)
                {
                    var transitResponse = httpResponse.Content;
                    switch (transitResponse.Code)
                    {
                        case PeerResponseCode.AcceptedIntoInbox:
                        case PeerResponseCode.AcceptedDirectWrite:
                            result.Add(recipient, DeleteLinkedFileStatus.RequestAccepted);
                            break;

                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
                else
                {
                    result.Add(recipient, DeleteLinkedFileStatus.RemoteServerFailed);
                }
            }

            return result;
        }

        public async Task ProcessOutbox()
        {
            //Note: here we can prioritize outbox processing by drive if need be
            var page = await driveManager.GetDrives(PageOptions.All);
            foreach (var drive in page.Results)
            {
                await ProcessDriveOutbox(drive.Id);
            }
        }

        //

        private async Task ProcessDriveOutbox(Guid driveId)
        {
            try
            {
                var batchSize = odinConfiguration.Transit.OutboxBatchSize;
                var batch = await peerOutbox.GetBatchForProcessing(driveId, batchSize);

                //
                // Send by recipient in parallel
                //
                var recipientSendTasks = new List<Task>();
                var groups = batch.GroupBy(b => b.Recipient);
                recipientSendTasks.AddRange(groups.Select(SendOutboxItemsBatchToRecipient));
                await Task.WhenAll(recipientSendTasks);
            }
            catch (Exception e)
            {
                Log.Error(e, "Unhandled Exception occured while processing the drive outbox for drive {driveId}", driveId);
            }
        }

        private EncryptedRecipientTransferInstructionSet CreateTransferInstructionSet(KeyHeader keyHeaderToBeEncrypted,
            ClientAccessToken clientAccessToken,
            TargetDrive targetDrive,
            TransferFileType transferFileType,
            FileSystemType fileSystemType, TransitOptions transitOptions)
        {
            var sharedSecret = clientAccessToken.SharedSecret;
            var iv = ByteArrayUtil.GetRndByteArray(16);
            var sharedSecretEncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeaderToBeEncrypted, iv, ref sharedSecret);

            return new EncryptedRecipientTransferInstructionSet()
            {
                TargetDrive = targetDrive,
                TransferFileType = transferFileType,
                FileSystemType = fileSystemType,
                ContentsProvided = transitOptions.SendContents,
                SharedSecretEncryptedKeyHeader = sharedSecretEncryptedKeyHeader,
            };
        }

        private void AddToTransferKeyEncryptionQueue(OdinId recipient, InternalDriveFileId file)
        {
            var now = UnixTimeUtc.Now().milliseconds;
            var item = new PeerKeyEncryptionQueueItem()
            {
                Id = GuidId.NewId(),
                FileId = file.FileId,
                Recipient = recipient,
                FirstAddedTimestampMs = now,
                Attempts = 1,
                LastAttemptTimestampMs = now
            };

            _transferKeyEncryptionQueueService.Enqueue(item);
        }

        private async Task SendOutboxItemsBatchToRecipient(IEnumerable<OutboxItem> items)
        {
            List<OutboxItem> filesForDeletion = new List<OutboxItem>();

            // Executed one at a time so we respect FIFO for this recipient
            foreach (var item in items)
            {
                var result = await SendOutboxItemAsync(item);
                if (result.TransferResult == TransferResult.Success)
                {
                    if (result.OutboxItem.IsTransientFile)
                    {
                        filesForDeletion.Add(result.OutboxItem);
                    }

                    await peerOutbox.MarkComplete(result.OutboxItem.Marker);
                }
                else
                {
                    switch (result.TransferResult)
                    {
                        case TransferResult.RecipientServerNotResponding:
                        case TransferResult.RecipientDoesNotHavePermissionToFileAcl:
                        case TransferResult.RecipientServerError:
                            await peerOutbox.MarkFailure(result.OutboxItem.Marker, result.TransferResult);
                            break;

                        case TransferResult.FileDoesNotAllowDistribution:
                        case TransferResult.RecipientServerReturnedAccessDenied:
                        case TransferResult.EncryptedTransferInstructionSetNotAvailable:
                        case TransferResult.UnknownError:
                            await peerOutbox.MarkComplete(result.OutboxItem.Marker);
                            break;

                        default:
                            await peerOutbox.MarkComplete(result.OutboxItem.Marker);
                            logger.LogError("Unhandled transfer result was found {tr}", result.TransferResult);
                            break;
                    }
                }

                await mediator.Publish(new OutboxItemProcessedNotification
                {
                    Recipient = result.Recipient,
                    File = result.File,
                    VersionTag = result.VersionTag,
                    FileSystemType = result.OutboxItem.TransferInstructionSet.FileSystemType,
                    TransferStatus =
                        MapTransferResultToStatus(result.TransferResult, result.RecipientPeerResponseCode) //TODO: i think i can drop this transfer status
                });
            }

            //TODO: optimization point; I need to see if this sort of deletion code is needed anymore; now that we have the transient temp drive
            foreach (var item in filesForDeletion)
            {
                var fs = _fileSystemResolver.ResolveFileSystem(item.TransferInstructionSet.FileSystemType);
                await fs.Storage.HardDeleteLongTermFile(item.File);
            }
        }

        private TransferStatus MapTransferResultToStatus(TransferResult transferResult, PeerResponseCode? responseCode)
        {
            switch (transferResult)
            {
                case TransferResult.Success:
                    return responseCode == PeerResponseCode.AcceptedDirectWrite ? TransferStatus.DeliveredToTargetDrive : TransferStatus.DeliveredToInbox;

                case TransferResult.EncryptedTransferInstructionSetNotAvailable:
                    return TransferStatus.PendingRetry;

                case TransferResult.RecipientServerError:
                case TransferResult.RecipientServerNotResponding:
                case TransferResult.UnknownError:
                    return TransferStatus.TotalRejectionClientShouldRetry;

                case TransferResult.RecipientDoesNotHavePermissionToFileAcl:
                    return TransferStatus.RecipientDoesNotHavePermissionToFileAcl;

                case TransferResult.FileDoesNotAllowDistribution:
                    return TransferStatus.FileDoesNotAllowDistribution;

                case TransferResult.RecipientServerReturnedAccessDenied:
                    return TransferStatus.RecipientReturnedAccessDenied;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private async Task<OutboxProcessingResult> SendOutboxItemAsync(OutboxItem outboxItem)
        {
            IDriveFileSystem fs = _fileSystemResolver.ResolveFileSystem(outboxItem.TransferInstructionSet.FileSystemType);

            OdinId recipient = outboxItem.Recipient;
            var file = outboxItem.File;
            var options = outboxItem.OriginalTransitOptions;

            var header = await fs.Storage.GetServerFileHeader(outboxItem.File);
            var versionTag = header.FileMetadata.VersionTag.GetValueOrDefault();

            // Enforce ACL at the last possible moment before shipping the file out of the identity; in case it changed
            if (!await driveAclAuthorizationService.IdentityHasPermission(recipient, header.ServerMetadata.AccessControlList))
            {
                return new OutboxProcessingResult()
                {
                    File = file,
                    VersionTag = versionTag,
                    Recipient = recipient,
                    Timestamp = UnixTimeUtc.Now().milliseconds,
                    TransferResult = TransferResult.RecipientDoesNotHavePermissionToFileAcl,
                    OutboxItem = outboxItem
                };
            }

            //look up transfer key
            var transferInstructionSet = outboxItem.TransferInstructionSet;
            if (null == transferInstructionSet)
            {
                return new OutboxProcessingResult()
                {
                    File = file,
                    VersionTag = versionTag,
                    Recipient = recipient,
                    Timestamp = UnixTimeUtc.Now().milliseconds,
                    TransferResult = TransferResult.EncryptedTransferInstructionSetNotAvailable,
                    OutboxItem = outboxItem
                };
            }

            var shouldSendPayload = options.SendContents.HasFlag(SendContents.Payload);

            var decryptedClientAuthTokenBytes = outboxItem.EncryptedClientAuthToken;
            var clientAuthToken = ClientAuthenticationToken.FromPortableBytes(decryptedClientAuthTokenBytes);
            decryptedClientAuthTokenBytes.WriteZeros(); //never send the client auth token; even if encrypted

            if (options.UseAppNotification)
            {
                transferInstructionSet.AppNotificationOptions = options.AppNotificationOptions;
            }

            var transferInstructionSetBytes = OdinSystemSerializer.Serialize(transferInstructionSet).ToUtf8ByteArray();
            var transferKeyHeaderStream = new StreamPart(
                new MemoryStream(transferInstructionSetBytes),
                "transferInstructionSet.encrypted", "application/json",
                Enum.GetName(MultipartHostTransferParts.TransferKeyHeader));

            if (header.ServerMetadata.AllowDistribution == false)
            {
                return new OutboxProcessingResult()
                {
                    File = file,
                    VersionTag = versionTag,
                    Recipient = recipient,
                    Timestamp = UnixTimeUtc.Now().milliseconds,
                    TransferResult = TransferResult.FileDoesNotAllowDistribution,
                    OutboxItem = outboxItem
                };
            }

            var sourceMetadata = header.FileMetadata;

            //redact the info by explicitly stating what we will keep
            //therefore, if a new attribute is added, it must be considered if it should be sent to the recipient
            var redactedMetadata = new FileMetadata()
            {
                //TODO: here I am removing the file and drive id from the stream but we need
                // to resolve this by moving the file information to the server header
                File = InternalDriveFileId.Redacted(),
                Created = sourceMetadata.Created,
                Updated = sourceMetadata.Updated,
                AppData = sourceMetadata.AppData,
                IsEncrypted = sourceMetadata.IsEncrypted,
                GlobalTransitId = options.OverrideRemoteGlobalTransitId.GetValueOrDefault(sourceMetadata.GlobalTransitId.GetValueOrDefault()),
                ReactionPreview = sourceMetadata.ReactionPreview,
                SenderOdinId = sourceMetadata.SenderOdinId,
                ReferencedFile = sourceMetadata.ReferencedFile,
                VersionTag = versionTag,
                Payloads = sourceMetadata.Payloads,
                FileState = sourceMetadata.FileState,
            };

            var json = OdinSystemSerializer.Serialize(redactedMetadata);
            var stream = new MemoryStream(json.ToUtf8ByteArray());
            var metaDataStream = new StreamPart(stream, "metadata.encrypted", "application/json", Enum.GetName(MultipartHostTransferParts.Metadata));

            var additionalStreamParts = new List<StreamPart>();

            if (shouldSendPayload)
            {
                foreach (var descriptor in redactedMetadata.Payloads ?? new List<PayloadDescriptor>())
                {
                    var payloadKey = descriptor.Key;

                    string contentType = "application/unknown";

                    //TODO: consider what happens if the payload has been delete from disk
                    var p = await fs.Storage.GetPayloadStream(file, payloadKey, null);
                    var payloadStream = p.Stream;

                    var payload = new StreamPart(payloadStream, payloadKey, contentType, Enum.GetName(MultipartHostTransferParts.Payload));
                    additionalStreamParts.Add(payload);

                    foreach (var thumb in descriptor.Thumbnails ?? new List<ThumbnailDescriptor>())
                    {
                        var (thumbStream, thumbHeader) =
                            await fs.Storage.GetThumbnailPayloadStream(file, thumb.PixelWidth, thumb.PixelHeight, descriptor.Key, descriptor.Uid);

                        var thumbnailKey =
                            $"{payloadKey}" +
                            $"{DriveFileUtility.TransitThumbnailKeyDelimiter}" +
                            $"{thumb.PixelWidth}" +
                            $"{DriveFileUtility.TransitThumbnailKeyDelimiter}" +
                            $"{thumb.PixelHeight}";

                        additionalStreamParts.Add(new StreamPart(thumbStream, thumbnailKey, thumbHeader.ContentType,
                            Enum.GetName(MultipartUploadParts.Thumbnail)));
                    }
                }
            }

            async Task<ApiResponse<PeerTransferResponse>> TrySendFile()
            {
                var client = _odinHttpClientFactory.CreateClientUsingAccessToken<IPeerTransferHttpClient>(recipient, clientAuthToken);
                var response = await client.SendHostToHost(transferKeyHeaderStream, metaDataStream, additionalStreamParts.ToArray());
                return response;
            }

            try
            {
                PeerResponseCode peerCode = PeerResponseCode.Unknown;
                TransferResult transferResult = TransferResult.UnknownError;

                await TryRetry.WithDelayAsync(
                    odinConfiguration.Host.PeerOperationMaxAttempts,
                    odinConfiguration.Host.PeerOperationDelayMs,
                    CancellationToken.None,
                    async () => { (peerCode, transferResult) = MapPeerResponseCode(await TrySendFile()); });

                return new OutboxProcessingResult()
                {
                    File = file,
                    VersionTag = versionTag,
                    Recipient = recipient,
                    RecipientPeerResponseCode = peerCode,
                    TransferResult = transferResult,
                    Timestamp = UnixTimeUtc.Now().milliseconds,
                    OutboxItem = outboxItem
                };
            }
            catch (TryRetryException ex)
            {
                var e = ex.InnerException;
                var tr = (e is TaskCanceledException or HttpRequestException or OperationCanceledException)
                    ? TransferResult.RecipientServerNotResponding
                    : TransferResult.UnknownError;

                return new OutboxProcessingResult()
                {
                    File = file,
                    VersionTag = versionTag,
                    Recipient = recipient,
                    RecipientPeerResponseCode = null,
                    TransferResult = tr,
                    Timestamp = UnixTimeUtc.Now().milliseconds,
                    OutboxItem = outboxItem
                };
            }
        }

        private (PeerResponseCode peerCode, TransferResult transferResult) MapPeerResponseCode(ApiResponse<PeerTransferResponse> response)
        {
            //TODO: needs more work to bring clarity to response code

            if (response.IsSuccessStatusCode)
            {
                return (response!.Content!.Code, TransferResult.Success);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return (PeerResponseCode.Unknown, TransferResult.RecipientServerReturnedAccessDenied);
            }

            if (response.StatusCode == HttpStatusCode.InternalServerError)
            {
                return (PeerResponseCode.Unknown, TransferResult.RecipientServerError);
            }

            return (PeerResponseCode.Unknown, TransferResult.UnknownError);
        }

        private async Task<Dictionary<string, bool>> EnqueueOutboxItems(
            InternalDriveFileId internalFile,
            TransitOptions options,
            FileTransferOptions fileTransferOptions,
            int priority,
            OutboxItemType outboxItemType)
        {
            var fs = _fileSystemResolver.ResolveFileSystem(fileTransferOptions.FileSystemType);

            TargetDrive targetDrive = options.RemoteTargetDrive ?? (await driveManager.GetDrive(internalFile.DriveId, failIfInvalid: true)).TargetDriveInfo;

            var status = new Dictionary<string, bool>();

            if (options.Recipients?.Contains(tenantContext.HostOdinId) ?? false)
            {
                throw new OdinClientException("Cannot transfer a file to the sender; what's the point?", OdinClientErrorCode.InvalidRecipient);
            }

            var header = await fs.Storage.GetServerFileHeader(internalFile);
            var storageKey = OdinContext.PermissionsContext.GetDriveStorageKey(internalFile.DriveId);

            var keyHeader = header.FileMetadata.IsEncrypted ? header.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey) : KeyHeader.Empty();
            storageKey.Wipe();

            foreach (var r in options.Recipients!)
            {
                var recipient = (OdinId)r;
                try
                {
                    //TODO: i need to resolve the token outside of transit, pass it in as options instead
                    var clientAuthToken = await ResolveClientAccessToken(recipient);

                    //TODO: apply encryption before storing in the outbox
                    var encryptedClientAccessToken = clientAuthToken.ToAuthenticationToken().ToPortableBytes();

                    var item = new OutboxItem()
                    {
                        Priority = priority,
                        Type = outboxItemType,
                        IsTransientFile = options.IsTransient,
                        File = internalFile,
                        Recipient = recipient,
                        OriginalTransitOptions = options,
                        EncryptedClientAuthToken = encryptedClientAccessToken,
                        TransferInstructionSet = CreateTransferInstructionSet(
                            keyHeader,
                            clientAuthToken,
                            targetDrive,
                            fileTransferOptions.TransferFileType,
                            fileTransferOptions.FileSystemType,
                            options)
                    };

                    await peerOutbox.Add(item);
                    status.Add(recipient, true);
                }
                catch (Exception ex)
                {
                    logger.LogError("Failed while creating outbox item {msg}", ex.Message);
                    AddToTransferKeyEncryptionQueue(recipient, internalFile);
                    status.Add(recipient, false);
                }
            }

            return status;
        }

        private Task<Dictionary<string, TransferStatus>> MapOutboxCreationResult(Dictionary<string, bool> outboxStatus)
        {
            var transferStatus = new Dictionary<string, TransferStatus>();

            foreach (var s in outboxStatus)
            {
                transferStatus.Add(s.Key, s.Value ? TransferStatus.TransferKeyCreated : TransferStatus.AwaitingTransferKey);
            }

            return Task.FromResult(transferStatus);
        }
    }
}