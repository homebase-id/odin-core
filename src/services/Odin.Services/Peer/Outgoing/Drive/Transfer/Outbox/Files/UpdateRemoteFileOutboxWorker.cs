using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files;

public class UpdateRemoteFileOutboxWorker(
    OutboxFileItem fileItem,
    FileSystemResolver fileSystemResolver,
    ILogger<UpdateRemoteFileOutboxWorker> logger,
    OdinConfiguration odinConfiguration,
    IOdinHttpClientFactory odinHttpClientFactory
) : OutboxWorkerBase(fileItem, logger)
{
    public async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> Send(IOdinContext odinContext, DatabaseConnection cn, CancellationToken cancellationToken)
    {
        try
        {
            if (FileItem.AttemptCount > odinConfiguration.Host.PeerOperationMaxAttempts)
            {
                throw new OdinOutboxProcessingException("Too many attempts")
                {
                    File = FileItem.File,
                    TransferStatus = LatestTransferStatus.SendingServerTooManyAttempts,
                    Recipient = default,
                    VersionTag = default,
                    GlobalTransitId = default
                };
            }

            logger.LogDebug("Start: Sending updated file: {file} to {recipient}", FileItem.File, FileItem.Recipient);

            Guid versionTag = default;
            Guid globalTransitId = default;

            await PerformanceCounter.MeasureExecutionTime("Outbox UpdateRemoteFileOutboxWorker",
                async () => { (versionTag, globalTransitId) = await SendUpdatedFileItemAsync(FileItem, odinContext, cn, cancellationToken); });

            logger.LogDebug("Success Sending updated file: {file} to {recipient} with gtid: {gtid}", FileItem.File, FileItem.Recipient, globalTransitId);

            return (true, UnixTimeUtc.ZeroTime);
        }
        catch (OdinOutboxProcessingException e)
        {
            try
            {
                return await HandleOutboxProcessingException(odinContext, cn, e);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Error while handling the outbox processing exception " +
                                           "for file: {file} and recipient: {recipient} with version: " +
                                           "{version} and status: {status}",
                    e.File,
                    e.Recipient,
                    e.TransferStatus,
                    e.VersionTag);

                throw new OdinSystemException("Failed while handling Outbox Processing Exception", e);
            }
        }
    }

    private async Task<(Guid versionTag, Guid globalTransitId)> SendUpdatedFileItemAsync(OutboxFileItem outboxFileItem, IOdinContext odinContext,
        DatabaseConnection cn,
        CancellationToken cancellationToken)
    {
        OdinId recipient = outboxFileItem.Recipient;
        var file = outboxFileItem.File;
        var options = outboxFileItem.State.OriginalTransitOptions;

        var request = outboxFileItem.State.DeserializeData<UpdateRemoteFileRequest>();
        
        var instructionSet = FileItem.State.TransferInstructionSet;
        var fileSystem = fileSystemResolver.ResolveFileSystem(instructionSet.FileSystemType);
        var header = await fileSystem.Storage.GetServerFileHeader(outboxFileItem.File, odinContext, cn);
        var versionTag = header.FileMetadata.VersionTag.GetValueOrDefault();
        var globalTransitId = header.FileMetadata.GlobalTransitId;
        
        if (header.ServerMetadata.AllowDistribution == false)
        {
            throw new OdinOutboxProcessingException("File does not allow distribution")
            {
                TransferStatus = LatestTransferStatus.SourceFileDoesNotAllowDistribution,
                VersionTag = versionTag,
                Recipient = recipient,
                File = file
            };
        }

        var decryptedClientAuthTokenBytes = outboxFileItem.State.EncryptedClientAuthToken;
        var clientAuthToken = ClientAuthenticationToken.FromPortableBytes(decryptedClientAuthTokenBytes);
        decryptedClientAuthTokenBytes.WriteZeros(); //never send the client auth token; even if encrypted

        if (options.UseAppNotification)
        {
            instructionSet.AppNotificationOptions = options.AppNotificationOptions;
        }

        var redactedAcl = header.ServerMetadata.AccessControlList;
        redactedAcl?.OdinIdList?.Clear();
        instructionSet.OriginalAcl = redactedAcl;

        var transferInstructionSetBytes = OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray();
        var transferKeyHeaderStream = new StreamPart(
            new MemoryStream(transferInstructionSetBytes),
            "transferInstructionSet.encrypted", "application/json",
            Enum.GetName(MultipartHostTransferParts.TransferKeyHeader));

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
            GlobalTransitId = sourceMetadata.GlobalTransitId.GetValueOrDefault(),
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

        foreach (var descriptor in redactedMetadata.Payloads ?? new List<PayloadDescriptor>())
        {
            var payloadKey = descriptor.Key;

            string contentType = "application/unknown";

            //TODO: consider what happens if the payload has been delete from disk
            var p = await fileSystem.Storage.GetPayloadStream(file, payloadKey, null, odinContext, cn);
            var payloadStream = p.Stream;

            var payload = new StreamPart(payloadStream, payloadKey, contentType, Enum.GetName(MultipartHostTransferParts.Payload));
            additionalStreamParts.Add(payload);

            foreach (var thumb in descriptor.Thumbnails ?? new List<ThumbnailDescriptor>())
            {
                var (thumbStream, thumbHeader) =
                    await fileSystem.Storage.GetThumbnailPayloadStream(file, thumb.PixelWidth, thumb.PixelHeight, descriptor.Key, descriptor.Uid,
                        odinContext, cn);

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

        async Task<ApiResponse<PeerTransferResponse>> TrySendFile()
        {
            var client = odinHttpClientFactory.CreateClientUsingAccessToken<IPeerTransferHttpClient>(recipient, clientAuthToken);
            var response = await client.UpdatePeerFile(transferKeyHeaderStream, metaDataStream, additionalStreamParts.ToArray());
            return response;
        }

        try
        {
            ApiResponse<PeerTransferResponse> response = null;

            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                cancellationToken,
                async () => { response = await TrySendFile(); });

            if (response.IsSuccessStatusCode)
            {
                return (versionTag, globalTransitId.GetValueOrDefault());
            }

            throw new OdinOutboxProcessingException("Failed while sending the request")
            {
                TransferStatus = MapPeerErrorResponseHttpStatus(response),
                VersionTag = versionTag,
                GlobalTransitId = globalTransitId,
                Recipient = recipient,
                File = file
            };
        }
        catch (TryRetryException ex)
        {
            var e = ex.InnerException;
            var status = (e is TaskCanceledException or HttpRequestException or OperationCanceledException)
                ? LatestTransferStatus.RecipientServerNotResponding
                : LatestTransferStatus.UnknownServerError;

            throw new OdinOutboxProcessingException("Failed sending to recipient")
            {
                TransferStatus = status,
                VersionTag = versionTag,
                Recipient = recipient,
                GlobalTransitId = globalTransitId,
                File = file
            };
        }
    }

    protected override async Task<UnixTimeUtc> HandleRecoverableTransferStatus(IOdinContext odinContext, DatabaseConnection cn,
        OdinOutboxProcessingException e)
    {
        logger.LogDebug(e, "Recoverable: Updating TransferHistory file {file} to status {status}.", e.File, e.TransferStatus);

        var nextRunTime = CalculateNextRunTime(e.TransferStatus);
        await Task.CompletedTask;
        return nextRunTime;
    }

    protected override async Task HandleUnrecoverableTransferStatus(OdinOutboxProcessingException e,
        IOdinContext odinContext,
        DatabaseConnection cn)
    {
        logger.LogDebug(e, "Unrecoverable: Updating TransferHistory file {file} to status {status}.", e.File, e.TransferStatus);
        await Task.CompletedTask;
    }
}