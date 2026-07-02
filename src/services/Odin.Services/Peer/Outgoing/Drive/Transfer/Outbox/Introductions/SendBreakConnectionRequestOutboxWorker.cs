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
using Odin.Services.Membership.Connections;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Introductions;

/// <summary>
/// Notifies a recipient that we have severed our connection with them, so they disconnect from us in return.
/// </summary>
public class SendBreakConnectionRequestOutboxWorker(
    OutboxFileItem fileItem,
    ILogger<SendBreakConnectionRequestOutboxWorker> logger,
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
            // Captured before the local connection record was deleted, so we can still authenticate.
            var clientAuthToken = FileItem.State.GetClientAccessToken();

            ApiResponse<HttpContent> response = null;
            await TryRetry.Create()
                .WithAttempts(Configuration.Host.PeerOperationMaxAttempts)
                .WithDelay(Configuration.Host.PeerOperationDelayMs)
                .WithCancellation(cancellationToken)
                .ExecuteAsync(async () =>
                {
                    var client = await odinHttpClientFactory.CreateClientUsingAccessTokenAsync<ICircleNetworkPeerConnectionsClient>(
                        recipient, clientAuthToken.ToAuthenticationToken());

                    response = await client.BreakConnection(cancellationToken);
                });

            if (response.IsSuccessStatusCode)
            {
                return (true, UnixTimeUtc.ZeroTime);
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                // The recipient no longer treats us as a connection (e.g. they already disconnected
                // from their side, or revoked our access). The connection is gone on both ends either
                // way, so there's nothing left to deliver — drop the item instead of retrying.
                logger.LogInformation("BreakConnection to {recipient} returned {status}; dropping outbox item.",
                    recipient, response.StatusCode);
                return (true, UnixTimeUtc.ZeroTime);
            }

            throw new OdinOutboxProcessingException("Failed while sending break-connection notification")
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
