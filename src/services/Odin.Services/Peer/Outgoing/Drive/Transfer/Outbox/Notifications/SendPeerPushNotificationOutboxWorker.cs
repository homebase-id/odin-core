using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.AppNotification;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Notifications;

public class SendPeerPushNotificationOutboxWorker(
    OutboxFileItem fileItem,
    ILogger<SendPeerPushNotificationOutboxWorker> logger,
    OdinConfiguration odinConfiguration,
    IOdinHttpClientFactory odinHttpClientFactory) : OutboxWorkerBase(fileItem, logger, null, odinConfiguration)
{
    public async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> Send(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        try
        {
            var newContext = OdinContextUpgrades.UpgradeToPeerTransferContext(odinContext);
            await NotifyPeerOfPushNotification(newContext, cancellationToken);

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

    private async Task NotifyPeerOfPushNotification(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        var data = FileItem.State.Data.ToStringFromUtf8Bytes();

        var record = OdinSystemSerializer.Deserialize<PushNotificationOutboxRecord>(data);
        var file = FileItem.File;
        var recipient = FileItem.Recipient;


        async Task<ApiResponse<PeerTransferResponse>> TryEnqueueNotification()
        {
            var client = await odinHttpClientFactory.CreateClientAsync<IPeerAppNotificationHttpClient>(FileItem.Recipient);
            var response = await client.EnqueuePushNotification(record);
            return response;
        }

        try
        {
            ApiResponse<PeerTransferResponse> response = null;

            await TryRetry.Create()
                .WithAttempts(Configuration.Host.PeerOperationMaxAttempts)
                .WithDelay(Configuration.Host.PeerOperationDelayMs)
                .WithCancellation(cancellationToken)
                .ExecuteAsync(async () => { response = await TryEnqueueNotification(); });

            if (response.IsSuccessStatusCode)
            {
                return;
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