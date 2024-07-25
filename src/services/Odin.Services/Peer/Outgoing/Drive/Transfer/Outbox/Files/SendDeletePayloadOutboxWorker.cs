using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
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
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files;

public class SendDeletePayloadOutboxWorker(
    OutboxFileItem fileItem,
    ILogger<SendDeletePayloadOutboxWorker> logger,
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

            var request = OdinSystemSerializer.Deserialize<DeleteRemotePayloadRequest>(FileItem.State.Data.ToStringFromUtf8Bytes());
            request.TargetFile.AssertIsValid(FileIdentifierType.GlobalTransitId);
            
            await PerformanceCounter.MeasureExecutionTime("Outbox DeleteRemotePayloadRequest",
                async () => { await SendRequest(request, odinContext, cn, cancellationToken); });

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

    private async Task SendRequest(
        DeleteRemotePayloadRequest request,
        IOdinContext odinContext,
        DatabaseConnection cn,
        CancellationToken cancellationToken)
    {
        OdinId recipient = FileItem.Recipient;

        var file = FileItem.File;

        var decryptedClientAuthTokenBytes = FileItem.State.EncryptedClientAuthToken;
        var clientAuthToken = ClientAuthenticationToken.FromPortableBytes(decryptedClientAuthTokenBytes);
        decryptedClientAuthTokenBytes.WriteZeros(); //never send the client auth token; even if encrypted
        
        async Task<ApiResponse<PeerTransferResponse>> TrySendFile()
        {
            var client = odinHttpClientFactory.CreateClientUsingAccessToken<IPeerTransferHttpClient>(recipient,clientAuthToken);
            var response = await client.DeletePayload(request);
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
                return;
            }

            throw new OdinOutboxProcessingException(
                "Failed while sending updated payloads (note: versionTag and GlobalTransitId are for the recipient identity)")
            {
                TransferStatus = MapPeerErrorResponseHttpStatus(response),
                VersionTag = request.VersionTag,
                GlobalTransitId = request.TargetFile.FileId,
                Recipient = recipient,
                File = file
            };
        }
        catch (TryRetryException ex)
        {
            var e = ex.InnerException;
            var status = (e is TaskCanceledException or HttpRequestException or OperationCanceledException)
                ? LatestTransferStatus.RecipientServerNotResponding
                : LatestTransferStatus.InternalServerError;

            throw new OdinOutboxProcessingException(
                "Failed sending updated payloads to recipient (note: versionTag and GlobalTransitId are for the recipient identity)")
            {
                TransferStatus = status,
                VersionTag = request.VersionTag,
                GlobalTransitId = request.TargetFile.FileId,
                Recipient = recipient,
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