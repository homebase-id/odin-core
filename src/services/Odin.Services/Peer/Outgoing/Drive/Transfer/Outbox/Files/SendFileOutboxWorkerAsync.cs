using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files;

public class SendFileOutboxWorkerAsync(
    OutboxFileItem fileItem,
    FileSystemResolver fileSystemResolver,
    ILogger<SendFileOutboxWorkerAsync> logger,
    OdinConfiguration odinConfiguration,
    IOdinHttpClientFactory odinHttpClientFactory
) : OutboxWorkerBase(fileItem, logger, fileSystemResolver, odinConfiguration)
{
    public async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> Send(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        try
        {
            AssertHasRemainingAttempts();

            logger.LogDebug("Start: Sending file: {file} to {recipient}", FileItem.File, FileItem.Recipient);

            Guid versionTag = default;
            Guid globalTransitId = default;

            await PerformanceCounter.MeasureExecutionTime("Outbox SendOutboxFileItemAsync",
                async () => { (versionTag, globalTransitId) = await SendOutboxFileItemAsync(FileItem, odinContext, cancellationToken); });

            await UpdateFileTransferHistory(globalTransitId, versionTag, odinContext);
            logger.LogDebug("Successful transfer of {gtid} to {recipient} - ", globalTransitId, FileItem.Recipient);

            return (true, UnixTimeUtc.ZeroTime);
        }
        catch (OdinOutboxProcessingException e)
        {
            try
            {
                return await HandleOutboxProcessingException(odinContext, e);
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

    private async Task<(Guid versionTag, Guid globalTransitId)> SendOutboxFileItemAsync(OutboxFileItem outboxFileItem, IOdinContext odinContext,
        
        CancellationToken cancellationToken)
    {
        OdinId recipient = outboxFileItem.Recipient;
        var file = outboxFileItem.File;
        var options = outboxFileItem.State.OriginalTransitOptions;

        var instructionSet = FileItem.State.TransferInstructionSet;
        var fileSystem = FileSystemResolver.ResolveFileSystem(instructionSet.FileSystemType);
        var header = await fileSystem.Storage.GetServerFileHeader(outboxFileItem.File, odinContext);
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

        if (options.UseAppNotification)
        {
            instructionSet.AppNotificationOptions = options.AppNotificationOptions;
        }

        var redactedAcl = header.ServerMetadata.AccessControlList;
        redactedAcl?.OdinIdList?.Clear();
        instructionSet.OriginalAcl = redactedAcl;

        var transferKeyHeaderStream = new StreamPart(
            new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray()),
            "transferInstructionSet.encrypted", "application/json",
            Enum.GetName(MultipartHostTransferParts.TransferKeyHeader));

        var shouldSendPayload = options.SendContents.HasFlag(SendContents.Payload);
        var (metaDataStream, payloadStreams) = await PackageFileStreamsAsync(header, shouldSendPayload, odinContext, options.OverrideRemoteGlobalTransitId);

        var decryptedClientAuthTokenBytes = outboxFileItem.State.EncryptedClientAuthToken;
        var clientAuthToken = ClientAuthenticationToken.FromPortableBytes(decryptedClientAuthTokenBytes);
        decryptedClientAuthTokenBytes.Wipe(); //never send the client auth token; even if encrypted
        
        async Task<ApiResponse<PeerTransferResponse>> TrySendFile()
        {
            var client = odinHttpClientFactory.CreateClientUsingAccessToken<IPeerTransferHttpClient>(recipient, clientAuthToken);
            var response = await client.SendHostToHost(transferKeyHeaderStream, metaDataStream, payloadStreams.ToArray());
            return response;
        }

        try
        {
            ApiResponse<PeerTransferResponse> response = null;

            await TryRetry.WithDelayAsync(
                Configuration.Host.PeerOperationMaxAttempts,
                Configuration.Host.PeerOperationDelayMs,
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

    protected override async Task<UnixTimeUtc> HandleRecoverableTransferStatus(IOdinContext odinContext,
        OdinOutboxProcessingException e)
    {
        logger.LogDebug(e, "Recoverable: Updating TransferHistory file {file} to status {status}.", e.File, e.TransferStatus);

        var update = new UpdateTransferHistoryData()
        {
            IsInOutbox = true,
            LatestTransferStatus = e.TransferStatus,
            VersionTag = null
        };
        
        var nextRunTime = CalculateNextRunTime(e.TransferStatus);
        var fs = FileSystemResolver.ResolveFileSystem(FileItem.State.TransferInstructionSet.FileSystemType);
        await fs.Storage.UpdateTransferHistory(FileItem.File, FileItem.Recipient, update, odinContext);

        return nextRunTime;
    }

    protected override async Task HandleUnrecoverableTransferStatus(OdinOutboxProcessingException e,
        IOdinContext odinContext)
    {
        logger.LogDebug(e, "Unrecoverable: Updating TransferHistory file {file} to status {status}.", e.File, e.TransferStatus);

        var update = new UpdateTransferHistoryData()
        {
            IsInOutbox = false,
            LatestTransferStatus = e.TransferStatus,
            VersionTag = null
        };

        var fs = FileSystemResolver.ResolveFileSystem(FileItem.State.TransferInstructionSet.FileSystemType);
        await fs.Storage.UpdateTransferHistory(FileItem.File, FileItem.Recipient, update, odinContext);
    }
}