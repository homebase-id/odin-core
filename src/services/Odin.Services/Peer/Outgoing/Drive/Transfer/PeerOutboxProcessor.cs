using System;
using System.Collections.Generic;
using System.IO;
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
using Odin.Services.Mediator.Outbox;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer
{
    public class PeerOutboxProcessor(
        OdinContextAccessor contextAccessor,
        PeerOutbox peerOutbox,
        IOdinHttpClientFactory odinHttpClientFactory,
        CircleNetworkService circleNetworkService,
        FileSystemResolver fileSystemResolver,
        OdinConfiguration odinConfiguration,
        IDriveAclAuthorizationService driveAclAuthorizationService,
        ILogger<PeerOutboxProcessor> logger,
        IMediator mediator)
        : PeerServiceBase(odinHttpClientFactory, circleNetworkService, contextAccessor, fileSystemResolver)
    {
        private readonly FileSystemResolver _fileSystemResolver = fileSystemResolver;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory = odinHttpClientFactory;

        public async Task ProcessOutbox()
        {
            var item = await peerOutbox.GetNextItem();
            
            //Temporary method until i talk with @Seb about threading, etc
            while (item != null)
            {
                logger.LogDebug("Processing outbox item type: {type}", item.Type);

                //TODO: add benchmark
                switch (item.Type)
                {
                    case OutboxItemType.File:
                        await SendFileOutboxItem(item);
                        break;

                    case OutboxItemType.Reaction:
                        await SendReactionItem(item);
                        break;

                    case OutboxItemType.PushNotification:
                        await SendPushNotification(item); //TODO: send in separate thread?
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }

                item = await peerOutbox.GetNextItem();
            }
        }

        //

        private async Task SendReactionItem(OutboxItem item)
        {
            //TODO:
            await peerOutbox.MarkComplete(item.Marker);
        }

        private async Task SendPushNotification(OutboxItem item)
        {
            //TODO: 
            await peerOutbox.MarkComplete(item.Marker);
        }

        private async Task SendFileOutboxItem(OutboxItem item)
        {
            List<OutboxItem> filesForDeletion = new List<OutboxItem>();
            try
            {
                var versionTag = await SendOutboxFileItemAsync(item);

                if (item.IsTransientFile)
                {
                    filesForDeletion.Add(item);
                }

                await UpdateTransferHistory(item, versionTag, null);
                await peerOutbox.MarkComplete(item.Marker);
                await mediator.Publish(new OutboxFileItemDeliverySuccessNotification
                {
                    Recipient = item.Recipient,
                    File = item.File,
                    VersionTag = versionTag,
                    FileSystemType = item.TransferInstructionSet.FileSystemType
                });
            }
            catch (OdinOutboxProcessingException e)
            {
                await UpdateTransferHistory(item, null, e.ProblemStatus);
                await peerOutbox.MarkFailure(item.Marker, GetNextRunTime(item));
                await mediator.Publish(new OutboxFileItemDeliveryFailedNotification
                {
                    Recipient = item.Recipient,
                    File = item.File,
                    FileSystemType = item.TransferInstructionSet.FileSystemType,
                    ProblemStatus = e.ProblemStatus
                });
            }
            catch
            {
                await peerOutbox.MarkComplete(item.Marker);
                await mediator.Publish(new OutboxFileItemDeliveryFailedNotification
                {
                    Recipient = item.Recipient,
                    File = item.File,
                    FileSystemType = item.TransferInstructionSet.FileSystemType,
                    ProblemStatus = LatestProblemStatus.UnknownServerError
                });
            }

            //TODO: optimization point; I need to see if this sort of deletion code is needed anymore; now that we have the transient temp drive
            foreach (var itemToDelete in filesForDeletion)
            {
                var fs = _fileSystemResolver.ResolveFileSystem(itemToDelete.TransferInstructionSet.FileSystemType);
                await fs.Storage.HardDeleteLongTermFile(itemToDelete.File);
            }
        }

        private UnixTimeUtc GetNextRunTime(OutboxItem item)
        {
            //TODO: expand logic as needed
            // item.AddedTimestamp
            // item.AttemptCount > someValueInConfig
            switch (item.Type)
            {
                case OutboxItemType.File:
                    return UnixTimeUtc.Now().AddMinutes(5);
                
                case OutboxItemType.Reaction:
                    return UnixTimeUtc.Now().AddMinutes(5);

                case OutboxItemType.PushNotification:
                    return UnixTimeUtc.Now().AddMinutes(5);
            }

            return UnixTimeUtc.Now().AddMinutes(5);
        }

        private async Task<Guid> SendOutboxFileItemAsync(OutboxItem outboxItem)
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
                throw new OdinOutboxProcessingException($"Recipient does not have permission to file ACL during send process")
                {
                    ProblemStatus = LatestProblemStatus.RecipientDoesNotHavePermissionToSourceFile,
                    VersionTag = versionTag,
                    Recipient = recipient,
                    File = file
                };
            }

            if (header.ServerMetadata.AllowDistribution == false)
            {
                throw new OdinOutboxProcessingException("File does not allow distribution")
                {
                    ProblemStatus = LatestProblemStatus.SourceFileDoesNotAllowDistribution,
                    VersionTag = versionTag,
                    Recipient = recipient,
                    File = file
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

            var sourceMetadata = header.FileMetadata;

            // redact the info by explicitly stating what we will keep
            // therefore, if a new attribute is added, it must be considered
            // if it should be sent to the recipient
            // DO NOT randomly add attributes here
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
                ApiResponse<PeerTransferResponse> response = null;

                await TryRetry.WithDelayAsync(
                    odinConfiguration.Host.PeerOperationMaxAttempts,
                    odinConfiguration.Host.PeerOperationDelayMs,
                    CancellationToken.None,
                    async () => { response = await TrySendFile(); });

                if (response.IsSuccessStatusCode)
                {
                    return versionTag;
                }

                throw new OdinOutboxProcessingException("Failed while sending the request")
                {
                    ProblemStatus = MapPeerResponseHttpStatus(response),
                    VersionTag = versionTag,
                    Recipient = recipient,
                    File = file
                };
            }
            catch (TryRetryException ex)
            {
                var e = ex.InnerException;
                var problemStatus = (e is TaskCanceledException or HttpRequestException or OperationCanceledException)
                    ? LatestProblemStatus.RecipientServerNotResponding
                    : LatestProblemStatus.UnknownServerError;

                throw new OdinOutboxProcessingException("Failed sending to recipient")
                {
                    ProblemStatus = problemStatus,
                    VersionTag = versionTag,
                    Recipient = recipient,
                    File = file
                };
            }
        }

        private async Task UpdateTransferHistory(OutboxItem item, Guid? versionTag, LatestProblemStatus? problemStatus)
        {
            var file = item.File;
            var fs = _fileSystemResolver.ResolveFileSystem(item.TransferInstructionSet.FileSystemType);
            var header = await fs.Storage.GetServerFileHeader(file);

            //TODO: consider the structure here, should i use a dictionary instead?
            var recipient = item.Recipient.ToString().ToLower();
            var history = header.ServerMetadata.TransferHistory ?? new RecipientTransferHistory();
            history.Recipients ??= new Dictionary<string, RecipientTransferHistoryItem>(StringComparer.InvariantCultureIgnoreCase);

            if (!history.Recipients.TryGetValue(recipient, out var recipientItem))
            {
                recipientItem = new RecipientTransferHistoryItem();
                history.Recipients.Add(recipient, recipientItem);
            }

            recipientItem.LastUpdated = UnixTimeUtc.Now();
            recipientItem.LatestProblemStatus = problemStatus;
            if (problemStatus == null)
            {
                recipientItem.LatestSuccessfullyDeliveredVersionTag = versionTag.GetValueOrDefault();
            }

            header.ServerMetadata.TransferHistory = history;
            await fs.Storage.UpdateActiveFileHeader(item.File, header, true);
        }

        private LatestProblemStatus MapPeerResponseHttpStatus(ApiResponse<PeerTransferResponse> response)
        {
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return LatestProblemStatus.RecipientIdentityReturnedAccessDenied;
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                return LatestProblemStatus.RecipientIdentityReturnedBadRequest;
            }
            
            // if (response.StatusCode == HttpStatusCode.InternalServerError) // or HttpStatusCode.ServiceUnavailable
            {
                return LatestProblemStatus.RecipientIdentityReturnedServerError;
            }
        }
    }
}