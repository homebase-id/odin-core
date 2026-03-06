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
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer.Incoming.Drive.Transfer.FileUpdate;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files;

public class UpdateRemoteFileOutboxWorker(
    OutboxFileItem fileItem,
    FileSystemResolver fileSystemResolver,
    ILogger<UpdateRemoteFileOutboxWorker> logger,
    OdinConfiguration odinConfiguration,
    IOdinHttpClientFactory odinHttpClientFactory) : OutboxWorkerBase(fileItem, logger, fileSystemResolver, odinConfiguration)
{
    public async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> Send(IOdinContext odinContext,
        CancellationToken cancellationToken)
    {
        try
        {
            AssertHasRemainingAttempts();
            logger.LogDebug("Start: Sending updated file: {file} to {recipient}", FileItem.File, FileItem.Recipient);

            Guid versionTag = default;
            Guid globalTransitId = default;

            await PerformanceCounter.MeasureExecutionTime("Outbox UpdateRemoteFileOutboxWorker",
                async () => { (versionTag, globalTransitId) = await SendUpdatedFileItemAsync(FileItem, odinContext, cancellationToken); });

            logger.LogDebug("Success Sending updated file: {file} to {recipient} with gtid: {gtid}", FileItem.File, FileItem.Recipient,
                globalTransitId);

            if (!FileItem.State.IsTransientFile)
            {
                await UpdateFileTransferHistory(globalTransitId, versionTag, odinContext);
            }

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

    private async Task<(Guid versionTag, Guid globalTransitId)> SendUpdatedFileItemAsync(OutboxFileItem outboxFileItem,
        IOdinContext odinContext,
        CancellationToken cancellationToken)
    {
        OdinId recipient = outboxFileItem.Recipient;
        var file = outboxFileItem.File;

        var instructionSet = outboxFileItem.State.DeserializeData<EncryptedRecipientFileUpdateInstructionSet>();
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

        // var theDrive = await driveManager.GetDrive(file.DriveId);
        var redactedAcl = header.ServerMetadata.AccessControlList;
        redactedAcl?.OdinIdList?.Clear();
        instructionSet.OriginalAcl = redactedAcl;

        var decryptedClientAuthTokenBytes = outboxFileItem.State.EncryptedClientAuthToken;
        var clientAuthToken = ClientAuthenticationToken.FromPortableBytes(decryptedClientAuthTokenBytes);
        decryptedClientAuthTokenBytes.Wipe(); //never send the client auth token; even if encrypted

        async Task<ApiResponse<PeerTransferResponse>> TrySendFile()
        {
            Stream transferKeyHeaderMemory = null;
            Stream metaDataStream = null;
            List<Stream> payloadStreams = [];

            try
            {
                logger.LogDebug("UpdatePeerFile BEGIN");

                transferKeyHeaderMemory = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

                var transferKeyHeaderStreamPart = new StreamPart(
                    transferKeyHeaderMemory,
                    "transferInstructionSet.encrypted", "application/json",
                    Enum.GetName(MultipartHostTransferParts.TransferKeyHeader));

                (metaDataStream, var metaDataStreamPart, payloadStreams, var payloadStreamParts) = await PackageFileStreamsAsync(
                    header,
                    odinContext,
                    datasourceOverride: FileItem.State.DataSourceOverride);

                var client = await odinHttpClientFactory.CreateClientUsingAccessTokenAsync<IPeerTransferHttpClient>(
                    recipient, clientAuthToken);

                var response = await client.UpdatePeerFile(
                    transferKeyHeaderStreamPart, metaDataStreamPart, payloadStreamParts.ToArray());

                logger.LogDebug("UpdatePeerFile END");

                return response;
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "SendUpdatedFileItemAsync:TrySendFile (TryRetry) {message}", e.Message);
                throw;
            }
            finally
            {
                transferKeyHeaderMemory?.Dispose();
                metaDataStream?.Dispose();
                foreach (var stream in payloadStreams)
                {
                    stream?.Dispose();
                }
            }
        }

        try
        {
            ApiResponse<PeerTransferResponse> response = null;

            await TryRetry.Create()
                .WithAttempts(Configuration.Host.PeerOperationMaxAttempts)
                .WithDelay(Configuration.Host.PeerOperationDelayMs)
                .WithCancellation(cancellationToken)
                .ExecuteAsync(async () => { response = await TrySendFile(); });

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
            logger.LogDebug(e, "Failed processing outbox item (type={t}) from outbox. Message {e}", FileItem.Type, e.Message);

            if (e is HttpRequestException httpRequestException)
            {
                logger.LogDebug("HttpRequestException Error {e} and status code: {status}", httpRequestException.HttpRequestError,
                    httpRequestException.StatusCode);
            }

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

    protected override Task<UnixTimeUtc> HandleRecoverableTransferStatus(IOdinContext odinContext,
        OdinOutboxProcessingException e)
    {
        logger.LogDebug(e, "Recoverable: Updating TransferHistory file {file} to status {status}.", e.File, e.TransferStatus);
        var nextRunTime = CalculateNextRunTime(e.TransferStatus);
        return Task.FromResult(nextRunTime);
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
        await fs.Storage.UpdateTransferHistory(FileItem.File, FileItem.Recipient, update, odinContext, null);
    }
}