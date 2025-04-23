using System;
using System.Net.Http;
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
using Odin.Services.Drives.DriveCore.Storage;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Reactions;

public class DeleteRemoteReactionOutboxWorker(
    OutboxFileItem fileItem,
    ILogger<DeleteRemoteReactionOutboxWorker> logger,
    IOdinHttpClientFactory odinHttpClientFactory,
    OdinConfiguration odinConfiguration
) : OutboxWorkerBase(fileItem, logger, null, odinConfiguration)
{
    public async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> Send(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        try
        {
            AssertHasRemainingAttempts();

            logger.LogDebug("AddRemoteReaction -> Sending request for file: {file} to {recipient}", FileItem.File, FileItem.Recipient);

            var globalTransitId = await HandleRequest(FileItem, cancellationToken);

            logger.LogDebug("AddRemoteReaction -> Success for gtid {gtid} (version:{version}) to {recipient} - Action: " +
                            "Marking Complete (popStamp:{marker})",
                globalTransitId,
                "no version info",
                FileItem.Recipient,
                FileItem.Marker);

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
                throw;
            }
        }
    }

    private async Task<Guid> HandleRequest(OutboxFileItem outboxItem, CancellationToken cancellationToken)
    {
        OdinId recipient = outboxItem.Recipient;
        var file = outboxItem.File;

        var item = OdinSystemSerializer.Deserialize<RemoteReactionRequestRedux>(outboxItem.State.Data.ToStringFromUtf8Bytes());

        var decryptedClientAuthTokenBytes = outboxItem.State.EncryptedClientAuthToken;
        var clientAuthToken = ClientAuthenticationToken.FromPortableBytes(decryptedClientAuthTokenBytes);
        decryptedClientAuthTokenBytes.Wipe(); //never send the client auth token; even if encrypted

        async Task<ApiResponse<PeerResponseCode>> TrySendRequest()
        {
            var client = odinHttpClientFactory.CreateClientUsingAccessToken<IPeerGroupReactionHttpClient>(
                recipient,
                clientAuthToken,
                item.FileSystemType);

            return await client.DeleteReaction(item);
        }

        try
        {
            ApiResponse<PeerResponseCode> response = null;

            await TryRetry.Create()
                .WithAttempts(Configuration.Host.PeerOperationMaxAttempts)
                .WithDelay(Configuration.Host.PeerOperationDelayMs)
                .WithCancellation(cancellationToken)
                .ExecuteAsync(async () => { response = await TrySendRequest(); });

            if (response.IsSuccessStatusCode)
            {
                return item.File.ToGlobalTransitIdFileIdentifier().GlobalTransitId;
            }

            throw new OdinOutboxProcessingException("Failed while sending the request")
            {
                TransferStatus = MapPeerErrorResponseHttpStatus(response),
                VersionTag = default,
                GlobalTransitId = item.File.ToGlobalTransitIdFileIdentifier().GlobalTransitId,
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
                VersionTag = default,
                GlobalTransitId = item.File.ToGlobalTransitIdFileIdentifier().GlobalTransitId,
                Recipient = recipient,
                File = file
            };
        }
    }

    protected override Task<UnixTimeUtc> HandleRecoverableTransferStatus(
        IOdinContext odinContext, 
        
        OdinOutboxProcessingException e)
    {
        var nextRunTime = CalculateNextRunTime(e.TransferStatus);
        return Task.FromResult(nextRunTime);
    }

    protected override Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> HandleUnrecoverableTransferStatus(
        OdinOutboxProcessingException e,
        IOdinContext odinContext)
    {
        return Task.FromResult((false, UnixTimeUtc.ZeroTime));
    }
}