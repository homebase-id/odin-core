using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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
using Odin.Services.JobManagement;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Peer.Outgoing.Jobs;
using Odin.Services.Util;
using Quartz;
using Refit;

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
        JobManager jobManager,
        ILogger<PeerOutgoingOutgoingTransferService> logger)
        : PeerServiceBase(odinHttpClientFactory, circleNetworkService,
            contextAccessor, fileSystemResolver), IPeerOutgoingTransferService
    {
        private readonly FileSystemResolver _fileSystemResolver = fileSystemResolver;
        private readonly TransferKeyEncryptionQueueService _transferKeyEncryptionQueueService = new(tenantSystemStorage);
        private readonly OdinContextAccessor _contextAccessor = contextAccessor;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory = odinHttpClientFactory;

        public async Task<Dictionary<string, TransferStatus>> SendFile(InternalDriveFileId internalFile,
            TransitOptions options, TransferFileType transferFileType, FileSystemType fileSystemType)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitWrite);

            OdinValidationUtils.AssertIsTrue(options.Recipients.TrueForAll(r => r != tenantContext.HostOdinId), "You cannot send a file to yourself");
            OdinValidationUtils.AssertValidRecipientList(options.Recipients);

            var sfo = new FileTransferOptions()
            {
                TransferFileType = transferFileType,
                FileSystemType = fileSystemType
            };

            if (options.Schedule == ScheduleOptions.SendNowAwaitResponse)
            {
                //send now
                return await SendFileNow(internalFile, options, sfo);
            }

            return await SendFileLater(internalFile, options, sfo);
        }

        public async Task ProcessOutbox()
        {
            var batchSize = odinConfiguration.Transit.OutboxBatchSize;

            //Note: here we can prioritize outbox processing by drive if need be
            var drives = await driveManager.GetDrives(PageOptions.All);

            //TODO: prioritize by drive using job manager schedulePriority

            foreach (var drive in drives.Results)
            {
                var batch = await peerOutbox.GetBatchForProcessing(drive.Id, batchSize);
                var schedulePriority = SchedulerGroup.Default;

                var jobKeys = new List<JobKey>();

                //Schedule one job per outbox item
                foreach (var item in batch)
                {
                    var jobKey = await CreateJob(item, schedulePriority);
                    jobKeys.Add(jobKey);
                    //TODO: could store the jobKey in the outbox item so we know what job is running it
                }
            }
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

        // 

        private async Task<JobKey> CreateJob(TransitOutboxItem item, SchedulerGroup schedulePriority)
        {
            var jobKey = await jobManager.Schedule<OutboxItemProcessorJob>(
                new OutboxProcessingJob(_contextAccessor.GetCurrent().Tenant,
                    item,
                    odinConfiguration,
                    schedulePriority));

            return jobKey;
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

        private async Task<List<OutboxProcessingResult>> SendOutboxItemsBatchToPeers(IEnumerable<TransitOutboxItem> items)
        {
            var sendFileTasks = new List<Task<OutboxProcessingResult>>();
            var results = new List<OutboxProcessingResult>();

            sendFileTasks.AddRange(items.Select(SendOutboxItemAsync));

            await Task.WhenAll(sendFileTasks);

            //check results

            List<TransitOutboxItem> filesForDeletion = new List<TransitOutboxItem>();
            sendFileTasks.ForEach(task =>
            {
                var sendResult = task.Result;
                results.Add(sendResult);

                if (sendResult.TransferResult == TransferResult.Success)
                {
                    if (sendResult.OutboxItem.IsTransientFile)
                    {
                        filesForDeletion.Add(sendResult.OutboxItem);
                    }

                    peerOutbox.MarkComplete(sendResult.OutboxItem.Marker);
                }
                else
                {
                    peerOutbox.MarkFailure(sendResult.OutboxItem.Marker, sendResult.TransferResult);
                }
            });

            //TODO: optimization point; I need to see if this sort of deletion code is needed anymore; now that we have the transient temp drive
            foreach (var item in filesForDeletion)
            {
                var fs = _fileSystemResolver.ResolveFileSystem(item.TransferInstructionSet.FileSystemType);
                await fs.Storage.HardDeleteLongTermFile(item.File);
            }

            return results;
        }

        private async Task<OutboxProcessingResult> SendOutboxItemAsync(TransitOutboxItem outboxItem)
        {
            IDriveFileSystem fs = _fileSystemResolver.ResolveFileSystem(outboxItem.TransferInstructionSet.FileSystemType);

            OdinId recipient = outboxItem.Recipient;
            var file = outboxItem.File;
            var options = outboxItem.OriginalTransitOptions;

            var header = await fs.Storage.GetServerFileHeader(outboxItem.File);

            // Enforce ACL at the last possible moment before shipping the file out of the identity; in case it changed
            if (!await driveAclAuthorizationService.IdentityHasPermission(recipient, header.ServerMetadata.AccessControlList))
            {
                return new OutboxProcessingResult()
                {
                    File = file,
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
                VersionTag = sourceMetadata.VersionTag,
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
                    async () => { (peerCode, transferResult) = OutboxProcessingUtils.MapPeerResponseCode(await TrySendFile()); });

                return new OutboxProcessingResult()
                {
                    File = file,
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
                    Recipient = recipient,
                    RecipientPeerResponseCode = null,
                    TransferResult = tr,
                    Timestamp = UnixTimeUtc.Now().milliseconds,
                    OutboxItem = outboxItem
                };
            }
        }

        private async Task<(Dictionary<string, bool> transferStatus, IEnumerable<TransitOutboxItem>)> CreateOutboxItems(
            InternalDriveFileId internalFile,
            TransitOptions options,
            FileTransferOptions fileTransferOptions)
        {
            var fs = _fileSystemResolver.ResolveFileSystem(fileTransferOptions.FileSystemType);

            TargetDrive targetDrive = options.RemoteTargetDrive ?? (await driveManager.GetDrive(internalFile.DriveId, failIfInvalid: true)).TargetDriveInfo;

            var status = new Dictionary<string, bool>();
            var outboxItems = new List<TransitOutboxItem>();

            if (options.Recipients?.Contains(tenantContext.HostOdinId) ?? false)
            {
                throw new OdinClientException("Cannot transfer a file to the sender; what's the point?", OdinClientErrorCode.InvalidRecipient);
            }

            var header = await fs.Storage.GetServerFileHeader(internalFile);
            var storageKey = _contextAccessor.GetCurrent().PermissionsContext.GetDriveStorageKey(internalFile.DriveId);

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

                    outboxItems.Add(new TransitOutboxItem()
                    {
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
                    });

                    status.Add(recipient, true);
                }
                catch (Exception ex)
                {
                    logger.LogError("Failed while creating outbox item {msg}", ex.Message);
                    AddToTransferKeyEncryptionQueue(recipient, internalFile);
                    status.Add(recipient, false);
                }
            }

            return (status, outboxItems);
        }

        private async Task<Dictionary<string, TransferStatus>> SendFileLater(InternalDriveFileId internalFile,
            TransitOptions options, FileTransferOptions fileTransferOptions)
        {
            //Since the owner is online (in this request) we can prepare a transfer key.  the outbox processor
            //will read the transfer key during the background send process

            var (outboxStatus, outboxItems) = await CreateOutboxItems(internalFile, options, fileTransferOptions);
            await peerOutbox.Add(outboxItems);

            return await MapOutboxCreationResult(outboxStatus);
        }

        private async Task<Dictionary<string, TransferStatus>> SendFileNow(InternalDriveFileId internalFile,
            TransitOptions transitOptions, FileTransferOptions fileTransferOptions)
        {
            var (outboxCreationStatus, outboxItems) = await CreateOutboxItems(internalFile, transitOptions, fileTransferOptions);

            //TODO: we want to send these as but it means i have to while loop and wait for the job to inish
            // foreach (var item in outboxItems)
            // {
            //     var jobKey = await CreateJob(item, SchedulerGroup.Default);
            // }

            // while (true)
            // {
            //     var response = await jobManager.GetResponse(jobKey);
            //      ...?
            // }

            // First map the outbox creation status for any that might have failed
            var transferStatus = await MapOutboxCreationResult(outboxCreationStatus);
            var sendResults = await SendOutboxItemsBatchToPeers(outboxItems);

            foreach (var result in sendResults)
            {
                if (result.TransferResult == TransferResult.Success)
                {
                    switch (result.RecipientPeerResponseCode)
                    {
                        case PeerResponseCode.AcceptedIntoInbox:
                            transferStatus[result.Recipient.DomainName] = TransferStatus.DeliveredToInbox;
                            break;

                        case PeerResponseCode.AcceptedDirectWrite:
                            transferStatus[result.Recipient.DomainName] = TransferStatus.DeliveredToTargetDrive;
                            break;

                        default:
                            throw new OdinSystemException("Unhandled success scenario in peer transfer");
                    }
                }
                else
                {
                    // Map to something to tell the client
                    switch (result.TransferResult)
                    {
                        case TransferResult.EncryptedTransferInstructionSetNotAvailable:
                            //enqueue the failures into the outbox
                            await peerOutbox.Add(result.OutboxItem);
                            transferStatus[result.Recipient.DomainName] = TransferStatus.PendingRetry;
                            break;

                        case TransferResult.RecipientServerError:
                        case TransferResult.RecipientServerNotResponding:
                        case TransferResult.UnknownError:
                            transferStatus[result.Recipient.DomainName] = TransferStatus.TotalRejectionClientShouldRetry;
                            break;

                        case TransferResult.RecipientDoesNotHavePermissionToFileAcl:
                            transferStatus[result.Recipient.DomainName] = TransferStatus.RecipientDoesNotHavePermissionToFileAcl;
                            break;

                        case TransferResult.FileDoesNotAllowDistribution:
                            transferStatus[result.Recipient.DomainName] = TransferStatus.FileDoesNotAllowDistribution;
                            break;

                        case TransferResult.RecipientServerReturnedAccessDenied:
                            transferStatus[result.Recipient.DomainName] = TransferStatus.RecipientReturnedAccessDenied;
                            break;

                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            return transferStatus;
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