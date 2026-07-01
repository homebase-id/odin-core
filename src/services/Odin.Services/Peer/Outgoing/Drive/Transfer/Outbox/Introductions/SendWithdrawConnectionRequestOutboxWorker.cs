using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Membership.Connections.Requests;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Introductions;

/// <summary>
/// Notifies a recipient that we have cancelled a connection request we sent them, so they withdraw the matching
/// pending request from their side.
/// </summary>
public class SendWithdrawConnectionRequestOutboxWorker(
    OutboxFileItem fileItem,
    ILogger<SendWithdrawConnectionRequestOutboxWorker> logger,
    OdinConfiguration odinConfiguration,
    IOdinHttpClientFactory odinHttpClientFactory) : OutboxWorkerBase(fileItem, logger, null, odinConfiguration)
{
    public async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> Send(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        var file = FileItem.File;
        var recipient = FileItem.Recipient;

        AssertHasRemainingAttempts();

        try
        {
            var withdrawal = FileItem.State.DeserializeData<ConnectionRequestWithdrawal>();

            ApiResponse<HttpContent> response = null;
            await TryRetry.Create()
                .WithAttempts(Configuration.Host.PeerOperationMaxAttempts)
                .WithDelay(Configuration.Host.PeerOperationDelayMs)
                .WithCancellation(cancellationToken)
                .ExecuteAsync(async () =>
                {
                    // Connection requests (and their withdrawal) travel over the certificate-authenticated peer
                    // perimeter -- no connection or CAT exists for a pending request -- so create a tokenless client.
                    var client = await odinHttpClientFactory.CreateClientAsync<ICircleNetworkRequestHttpClient>(recipient);

                    response = await client.WithdrawConnectionRequest(withdrawal, cancellationToken);
                });

            if (response.IsSuccessStatusCode)
            {
                return (true, UnixTimeUtc.ZeroTime);
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                // The recipient won't accept the notification (e.g. they blocked us). There's nothing left to
                // deliver, so drop the item instead of retrying.
                logger.LogInformation("WithdrawConnectionRequest to {recipient} returned {status}; dropping outbox item.",
                    recipient, response.StatusCode);
                return (true, UnixTimeUtc.ZeroTime);
            }

            throw new OdinOutboxProcessingException("Failed while sending withdraw-connection-request notification")
            {
                TransferStatus = MapPeerErrorResponseHttpStatus(response),
                VersionTag = default,
                GlobalTransitId = default,
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
                Recipient = recipient,
                GlobalTransitId = default,
                File = file
            };
        }
    }

    protected override Task<UnixTimeUtc> HandleRecoverableTransferStatus(IOdinContext odinContext, OdinOutboxProcessingException e)
    {
        return Task.FromResult(UnixTimeUtc.Now().AddMinutes(10));
    }

    protected override Task HandleUnrecoverableTransferStatus(OdinOutboxProcessingException e, IOdinContext odinContext)
    {
        return Task.CompletedTask;
    }
}
