using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files;

public class SendDeleteFileRequestOutboxWorkerAsync(
    OutboxFileItem fileItem,
    ILogger<SendDeleteFileRequestOutboxWorkerAsync> logger,
    OdinConfiguration odinConfiguration,
    IOdinHttpClientFactory odinHttpClientFactory
) : OutboxWorkerBase(fileItem, logger, null, odinConfiguration)
{
    private readonly OutboxFileItem _fileItem = fileItem;

    public async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> Send(IOdinContext odinContext, IdentityDatabase db, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogDebug("SendDeleteFileRequest -> Sending request for file: {file} to {recipient}", _fileItem.File, _fileItem.Recipient);

            var globalTransitId = await SendRequest(_fileItem, cancellationToken);

            logger.LogDebug("SendDeleteFileRequest -> Success for gtid {gtid} (version:{version}) to {recipient} - Action: " +
                            "Marking Complete (popStamp:{marker})",
                globalTransitId,
                "no version info",
                _fileItem.Recipient,
                _fileItem.Marker);

            return (true, UnixTimeUtc.ZeroTime);
        }
        catch (OdinOutboxProcessingException e)
        {
            try
            {
                return await HandleOutboxProcessingException(odinContext, db, e);
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
                throw;
            }
        }
    }

    private async Task<Guid> SendRequest(OutboxFileItem outboxItem, CancellationToken cancellationToken)
    {
        OdinId recipient = outboxItem.Recipient;
        var file = outboxItem.File;

        var request = OdinSystemSerializer.Deserialize<DeleteRemoteFileRequest>(outboxItem.State.Data.ToStringFromUtf8Bytes());

        var decryptedClientAuthTokenBytes = outboxItem.State.EncryptedClientAuthToken;
        var clientAuthToken = ClientAuthenticationToken.FromPortableBytes(decryptedClientAuthTokenBytes);
        decryptedClientAuthTokenBytes.Wipe(); //never send the client auth token; even if encrypted

        async Task<ApiResponse<PeerTransferResponse>> TrySendFile()
        {
            var client = odinHttpClientFactory.CreateClientUsingAccessToken<IPeerTransferHttpClient>(
                recipient,
                clientAuthToken,
                request.FileSystemType);

            return await client.DeleteLinkedFile(request);
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
                return request.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId;
            }

            throw new OdinOutboxProcessingException("Failed while sending the request")
            {
                TransferStatus = MapPeerErrorResponseHttpStatus(response),
                VersionTag = default,
                GlobalTransitId = request.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId,
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
                VersionTag = default,
                GlobalTransitId = request.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId,
                Recipient = recipient,
                File = file
            };
        }
    }

    protected override Task<UnixTimeUtc> HandleRecoverableTransferStatus(IOdinContext odinContext, IdentityDatabase db,
        OdinOutboxProcessingException e)
    {
        var nextRunTime = CalculateNextRunTime(e.TransferStatus);
        return Task.FromResult(nextRunTime);
    }

    protected override Task HandleUnrecoverableTransferStatus(OdinOutboxProcessingException e,
        IOdinContext odinContext,
        IdentityDatabase db)
    {
        return Task.CompletedTask;
    }
}