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

public class SendPayloadOutboxWorker(
    OutboxFileItem fileItem,
    FileSystemResolver fileSystemResolver,
    ILogger<SendPayloadOutboxWorker> logger,
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

            logger.LogDebug("Start: Sending file: {file} to {recipient}", FileItem.File, FileItem.Recipient);

            Guid versionTag = default;
            Guid globalTransitId = default;

            await PerformanceCounter.MeasureExecutionTime("Outbox SendPayload",
                async () => { (versionTag, globalTransitId) = await SendPayload(FileItem, odinContext, cn, cancellationToken); });

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

    private async Task<(Guid versionTag, Guid globalTransitId)> SendPayload(OutboxFileItem outboxFileItem,
        IOdinContext odinContext,
        DatabaseConnection cn,
        CancellationToken cancellationToken)
    {
        OdinId recipient = outboxFileItem.Recipient;
        var file = outboxFileItem.File;

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

        var redactedAcl = header.ServerMetadata.AccessControlList;
        redactedAcl?.OdinIdList?.Clear();
        instructionSet.OriginalAcl = redactedAcl;

        var transferInstructionSetBytes = OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray();
        var transferKeyHeaderStream = new StreamPart(
            new MemoryStream(transferInstructionSetBytes),
            "transferInstructionSet.encrypted", "application/json",
            Enum.GetName(MultipartHostTransferParts.TransferKeyHeader));

        var additionalStreamParts = new List<StreamPart>();

        foreach (var descriptor in header.FileMetadata.Payloads ?? new List<PayloadDescriptor>())
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
            var response = await client.UpdatePayloads(transferKeyHeaderStream, additionalStreamParts.ToArray());
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

    protected override Task<UnixTimeUtc> HandleRecoverableTransferStatus(IOdinContext odinContext, DatabaseConnection cn,
        OdinOutboxProcessingException e)
    {
        return Task.FromResult(CalculateNextRunTime(e.TransferStatus));
    }

    protected override Task HandleUnrecoverableTransferStatus(OdinOutboxProcessingException e, IOdinContext odinContext, DatabaseConnection cn)
    {
        return Task.CompletedTask;
    }
}