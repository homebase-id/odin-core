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
        var data = FileItem.State.Data.ToStringFromUtf8Bytes();

        var introduction = OdinSystemSerializer.Deserialize<Introduction>(data);
        var file = FileItem.File;
        var recipient = FileItem.Recipient;

        AssertHasRemainingAttempts();

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

                    response = await client.MakeIntroduction(encryptedPayload);
                });

            if (response.IsSuccessStatusCode)
            {
                return (true, UnixTimeUtc.ZeroTime);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return (false, UnixTimeUtc.Now().AddMinutes(10));
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
        //TODO: change to calculated 
        return Task.FromResult(UnixTimeUtc.Now().AddMinutes(10));
    }

    protected override Task HandleUnrecoverableTransferStatus(OdinOutboxProcessingException e, IOdinContext odinContext)
    {
        return Task.CompletedTask;
    }
}