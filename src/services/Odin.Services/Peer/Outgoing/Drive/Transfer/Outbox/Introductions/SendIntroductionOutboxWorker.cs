using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Introductions;

public class SendIntroductionOutboxWorker(
    OutboxFileItem fileItem,
    ILogger<SendIntroductionOutboxWorker> logger,
    OdinConfiguration odinConfiguration,
    IOdinHttpClientFactory odinHttpClientFactory) : OutboxWorkerBase(fileItem, logger, null, odinConfiguration)
{
    public async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> Send(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        try
        {
            AssertHasRemainingAttempts();
            return await SendIntroduction(cancellationToken);
        }
        catch (OdinOutboxProcessingException e)
        {
            // Settle it as the other peer workers do. Escaping to the processor would retry it at once,
            // logged at Error (#1778).
            return await HandleOutboxProcessingException(odinContext, e);
        }
    }

    private async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> SendIntroduction(CancellationToken cancellationToken)
    {
        var data = FileItem.State.Data.ToStringFromUtf8Bytes();

        var introduction = OdinSystemSerializer.Deserialize<Introduction>(data);
        var file = FileItem.File;
        var recipient = FileItem.Recipient;

        try
        {
            var clientAuthToken = FileItem.State.GetClientAccessToken();

            ApiResponse<HttpContent> response = null;
            await TryRetry.Create()
                .WithAttempts(Configuration.Host.PeerOperationMaxAttempts)
                .WithDelay(Configuration.Host.PeerOperationDelayMs)
                .WithCancellation(cancellationToken)
                .ExecuteAsync(async () =>
                {
                    var json = OdinSystemSerializer.Serialize(introduction);
                    var encryptedPayload = SharedSecretEncryptedPayload.Encrypt(json.ToUtf8ByteArray(), clientAuthToken.SharedSecret);
                    var client = await odinHttpClientFactory.CreateClientUsingAccessTokenAsync<ICircleNetworkPeerConnectionsClient>(recipient,
                        clientAuthToken.ToAuthenticationToken());

                    response = await client.MakeIntroduction(encryptedPayload, cancellationToken);
                });

            if (response.IsSuccessStatusCode)
            {
                return (true, UnixTimeUtc.ZeroTime);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                // Recipient denied the introduction (e.g. introducer lacks AllowIntroductions
                // permission, or recipient has the introduced identity blocked). Retrying won't
                // change the answer — drop the item rather than retry it.
                var body = response.Error?.Content;
                logger.LogInformation(
                    "SendIntroduction to {recipient} returned 403; dropping outbox item. body={body}",
                    recipient, body);
                return (true, UnixTimeUtc.ZeroTime);
            }

            throw new OdinOutboxProcessingException("Failed while enqueuing notification")
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
        return Task.FromResult(CalculateBackoffNextRunTime());
    }

    protected override Task HandleUnrecoverableTransferStatus(OdinOutboxProcessingException e, IOdinContext odinContext)
    {
        logger.LogWarning("SendIntroduction to {recipient} gave up after {attempts} attempts ({status})",
            FileItem.Recipient, FileItem.AttemptCount, e.TransferStatus);
        return Task.CompletedTask;
    }
}