using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base;
using Refit;
using SQLitePCL;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox
{
    public class PeerOutboxProcessor(
        PeerOutbox peerOutbox,
        IOdinHttpClientFactory odinHttpClientFactory,
        OdinConfiguration odinConfiguration,
        ILogger<PeerOutboxProcessor> logger,
        IDriveFileSystem fileSystem)
    {
        public async Task ProcessItemsSync(IEnumerable<OutboxItem> items)
        {
            foreach (var item in items)
            {
                await ProcessItem(item);
            }
        }
        
        public async Task ProcessItem(OutboxItem item)
        {
            _ = new ProcessOutboxItemWorker(item,
                fileSystem,
                logger,
                peerOutbox,
                odinConfiguration,
                odinHttpClientFactory).ProcessOutboxItem();
        }

        public async Task StartOutboxProcessing()
        {
            var item = await peerOutbox.GetNextItem();

            while (item != null)
            {
                _ = this.ProcessItem(item);

                item = await peerOutbox.GetNextItem();
            }
        }
    }

    public class ProcessOutboxItemWorker(
        OutboxItem item,
        IDriveFileSystem fileSystem,
        ILogger<PeerOutboxProcessor> logger,
        PeerOutbox peerOutbox,
        OdinConfiguration odinConfiguration,
        IOdinHttpClientFactory odinHttpClientFactory)
    {
        public async Task ProcessOutboxItem()
        {
            //TODO: add benchmark
            logger.LogDebug("Processing outbox item type: {type}", item.Type);

            switch (item.Type)
            {
                case OutboxItemType.PushNotification:
                    await SendPushNotification();
                    break;

                case OutboxItemType.File:
                    await SendFileOutboxItem();
                    break;

                case OutboxItemType.Reaction:
                    await SendReactionItem();
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private async Task SendReactionItem()
        {
            await Task.CompletedTask;
            throw new NotImplementedException("todo - support reactions in the outbox");
        }

        private async Task SendPushNotification()
        {
            await peerOutbox.MarkComplete(item.Marker);
        }

        private async Task SendFileOutboxItem()
        {
            try
            {
                var versionTag = await SendOutboxFileItemAsync(item);
                await peerOutbox.MarkComplete(item.Marker);
            }
            catch (OdinOutboxProcessingException e)
            {
                var nextRun = UnixTimeUtc.Now().AddSeconds(-5);
                await peerOutbox.MarkFailure(item.Marker, nextRun);
            }
            catch
            {
                await peerOutbox.MarkComplete(item.Marker);
            }
        }

        private async Task<Guid> SendOutboxFileItemAsync(OutboxItem outboxItem)
        {
            OdinId recipient = outboxItem.Recipient;
            var file = outboxItem.File;
            var options = outboxItem.OriginalTransitOptions;

            var header = await fileSystem.Storage.GetServerFileHeader(outboxItem.File);
            var versionTag = header.FileMetadata.VersionTag.GetValueOrDefault();

            // Enforce ACL at the last possible moment before shipping the file out of the identity; in case it changed
            // if (!await driveAclAuthorizationService.IdentityHasPermission(recipient, header.ServerMetadata.AccessControlList))
            // {
            //     throw new OdinOutboxProcessingException($"Recipient does not have permission to file ACL during send process")
            //     {
            //         ProblemStatus = LatestProblemStatus.RecipientDoesNotHavePermissionToSourceFile,
            //         VersionTag = versionTag,
            //         Recipient = recipient,
            //         File = file
            //     };
            // }

            if (header.ServerMetadata.AllowDistribution == false)
            {
                throw new OdinOutboxProcessingException("File does not allow distribution")
                {
                    // ProblemStatus = LatestProblemStatus.SourceFileDoesNotAllowDistribution,
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
                    var p = await fileSystem.Storage.GetPayloadStream(file, payloadKey, null);
                    var payloadStream = p.Stream;

                    var payload = new StreamPart(payloadStream, payloadKey, contentType, Enum.GetName(MultipartHostTransferParts.Payload));
                    additionalStreamParts.Add(payload);

                    foreach (var thumb in descriptor.Thumbnails ?? new List<ThumbnailDescriptor>())
                    {
                        var (thumbStream, thumbHeader) =
                            await fileSystem.Storage.GetThumbnailPayloadStream(file, thumb.PixelWidth, thumb.PixelHeight, descriptor.Key, descriptor.Uid);

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
                var client = odinHttpClientFactory.CreateClientUsingAccessToken<IPeerTransferHttpClient>(recipient, clientAuthToken);
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
                    // ProblemStatus = MapPeerResponseHttpStatus(response),
                    VersionTag = versionTag,
                    Recipient = recipient,
                    File = file
                };
            }
            catch (TryRetryException ex)
            {
                // var e = ex.InnerException;
                // var problemStatus = (e is TaskCanceledException or HttpRequestException or OperationCanceledException)
                //     ? LatestProblemStatus.RecipientServerNotResponding
                //     : LatestProblemStatus.UnknownServerError;

                throw new OdinOutboxProcessingException("Failed sending to recipient")
                {
                    // ProblemStatus = problemStatus,
                    VersionTag = versionTag,
                    Recipient = recipient,
                    File = file
                };
            }
        }
    }
}