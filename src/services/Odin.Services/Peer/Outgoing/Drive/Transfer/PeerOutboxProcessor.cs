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
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator.Outbox;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Refit;
using Serilog;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer
{
    public class PeerOutboxProcessor(
        OdinContextAccessor contextAccessor,
        PeerOutbox peerOutbox,
        IOdinHttpClientFactory odinHttpClientFactory,
        CircleNetworkService circleNetworkService,
        DriveManager driveManager,
        FileSystemResolver fileSystemResolver,
        OdinConfiguration odinConfiguration,
        IDriveAclAuthorizationService driveAclAuthorizationService,
        ILogger<PeerOutboxProcessor> logger,
        IMediator mediator)
        : PeerServiceBase(odinHttpClientFactory, circleNetworkService,
            contextAccessor, fileSystemResolver)
    {
        private readonly FileSystemResolver _fileSystemResolver = fileSystemResolver;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory = odinHttpClientFactory;

        public async Task ProcessOutbox()
        {
            //Note: here we can prioritize outbox processing by drive if need be
            var page = await driveManager.GetDrives(PageOptions.All);
            foreach (var drive in page.Results)
            {
                await ProcessDriveOutbox(drive.Id);
            }
        }

        public async Task ProcessDriveOutbox(Guid driveId)
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

        //

        private async Task SendOutboxItemsBatchToRecipient(IEnumerable<OutboxItem> items)
        {
            List<OutboxItem> filesForDeletion = new List<OutboxItem>();

            // Executed one at a time so we respect FIFO for this recipient
            foreach (var item in items)
            {
                OutboxProcessingResult result;
                try
                {
                    result = await SendOutboxItemAsync(item);
                }
                catch (Exception e)
                {
                    //TODO: ensure we continue processing other items
                    throw;
                }

                if (result.TransferResult == TransferResult.Success)
                {
                    if (result.OutboxItem.IsTransientFile)
                    {
                        filesForDeletion.Add(result.OutboxItem);
                    }

                    await this.MarkSuccess(result);
                }
                else
                {
                    switch (result.TransferResult)
                    {
                        case TransferResult.RecipientServerNotResponding:
                        case TransferResult.RecipientDoesNotHavePermissionToFileAcl:
                        case TransferResult.RecipientServerError:
                            await this.MarkFailure(result);
                            break;

                        case TransferResult.FileDoesNotAllowDistribution:
                        case TransferResult.RecipientServerReturnedAccessDenied:
                        case TransferResult.UnknownError:
                            await this.MarkSuccess(result);
                            break;

                        default:
                            await MarkProcessingError(result);
                            break;
                    }
                }
            }

            //TODO: optimization point; I need to see if this sort of deletion code is needed anymore; now that we have the transient temp drive
            foreach (var item in filesForDeletion)
            {
                var fs = _fileSystemResolver.ResolveFileSystem(item.TransferInstructionSet.FileSystemType);
                await fs.Storage.HardDeleteLongTermFile(item.File);
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

            var shouldSendPayload = options.SendContents.HasFlag(SendContents.Payload);

            var decryptedClientAuthTokenBytes = outboxItem.EncryptedClientAuthToken;
            var clientAuthToken = ClientAuthenticationToken.FromPortableBytes(decryptedClientAuthTokenBytes);
            decryptedClientAuthTokenBytes.WriteZeros(); //never send the client auth token; even if encrypted

            if (options.UseAppNotification)
            {
                outboxItem.TransferInstructionSet.AppNotificationOptions = options.AppNotificationOptions;
            }

            var transferInstructionSetBytes = OdinSystemSerializer.Serialize(outboxItem.TransferInstructionSet).ToUtf8ByteArray();
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
                    async () => { (peerCode, transferResult) = MapPeerResponseHttpStatus(await TrySendFile()); });

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

        private async Task MarkSuccess(OutboxProcessingResult result)
        {
            await UpdateTransferHistory(result);

            await peerOutbox.MarkComplete(result.OutboxItem.Marker);

            await mediator.Publish(new OutboxItemDeliverySuccessNotification
            {
                Recipient = result.Recipient,
                File = result.File,
                VersionTag = result.VersionTag,
                FileSystemType = result.OutboxItem.TransferInstructionSet.FileSystemType,
                TransferStatus =
                    MapTransferResultToStatus(result.TransferResult, result.RecipientPeerResponseCode)
            });
        }

        private async Task MarkFailure(OutboxProcessingResult result)
        {
            await UpdateTransferHistory(result);

            await peerOutbox.MarkFailure(result.OutboxItem.Marker, result.TransferResult);

            await mediator.Publish(new OutboxItemDeliveryFailedNotification
            {
                Recipient = result.Recipient,
                File = result.File,
                VersionTag = result.VersionTag,
                FileSystemType = result.OutboxItem.TransferInstructionSet.FileSystemType,
                TransferStatus =
                    MapTransferResultToStatus(result.TransferResult, result.RecipientPeerResponseCode) //TODO: i think i can drop this transfer status
            });
        }

        private async Task MarkProcessingError(OutboxProcessingResult result)
        {
            await peerOutbox.MarkComplete(result.OutboxItem.Marker);
            logger.LogError("Unhandled transfer result was found {tr}", result.TransferResult);
        }

        private TransferStatus MapTransferResultToStatus(TransferResult transferResult, PeerResponseCode? responseCode)
        {
            switch (transferResult)
            {
                case TransferResult.Success:
                    return responseCode == PeerResponseCode.AcceptedDirectWrite ? TransferStatus.Delivered : TransferStatus.DeliveredToInbox;

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

        private async Task UpdateTransferHistory(OutboxProcessingResult result)
        {
            var fs = _fileSystemResolver.ResolveFileSystem(result.OutboxItem.TransferInstructionSet.FileSystemType);
            var header = await fs.Storage.GetServerFileHeader(result.File);

            if (null == header)
            {
                logger.LogWarning("OutboxItemProcessedNotification raised for a file that " +
                                  "does not exist (File [{file}] on drive [{driveId}])",
                    result.File.FileId,
                    result.File.DriveId);
                return;
            }

            //TODO: consider the structure here, should i use a dictionary instead?
            var recipient = result.Recipient.ToString().ToLower();
            var history = header.ServerMetadata.TransferHistory ?? new RecipientTransferHistory();
            history.Items ??= new Dictionary<string, RecipientTransferHistoryItem>(StringComparer.InvariantCultureIgnoreCase);

            if (!history.Items.TryGetValue(recipient, out var recipientItem))
            {
                recipientItem = new RecipientTransferHistoryItem();
                history.Items.Add(recipient, recipientItem);
            }

            var problemStatus = MapProblemStatus(MapTransferResultToStatus(result.TransferResult, PeerResponseCode.Unknown));
            recipientItem.LastUpdated = UnixTimeUtc.Now();
            recipientItem.LatestProblemStatus = problemStatus;
            if (problemStatus == null)
            {
                recipientItem.LatestSuccessfullyDeliveredVersionTag = result.VersionTag;
            }

            header.ServerMetadata.TransferHistory = history;
            await fs.Storage.UpdateActiveFileHeader(result.File, header, true);
        }

        private LatestProblemStatus? MapProblemStatus(TransferStatus status)
        {
            switch (status)
            {
                case TransferStatus.PendingRetry:
                    return LatestProblemStatus.ServerPendingRetry;

                case TransferStatus.TotalRejectionClientShouldRetry:
                    return LatestProblemStatus.ClientMustRetry;

                case TransferStatus.FileDoesNotAllowDistribution:
                case TransferStatus.RecipientDoesNotHavePermissionToFileAcl:
                    return LatestProblemStatus.LocalFileDistributionDenied;

                case TransferStatus.RecipientReturnedAccessDenied:
                    return LatestProblemStatus.AccessDenied;

                case TransferStatus.AwaitingTransferKey:
                case TransferStatus.TransferKeyCreated:
                case TransferStatus.DeliveredToInbox:
                case TransferStatus.Delivered:
                    return null;
            }

            return null;
        }

        private (PeerResponseCode peerCode, TransferResult transferResult) MapPeerResponseHttpStatus(ApiResponse<PeerTransferResponse> response)
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
    }
}