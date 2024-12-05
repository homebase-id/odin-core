using System;
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
        var newContext = OdinContextUpgrades.UpgradeToPeerTransferContext(odinContext);
        await MakeIntroductionAsync(newContext, cancellationToken);
        return (true, UnixTimeUtc.ZeroTime);
    }

    private async Task MakeIntroductionAsync(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        var data = FileItem.State.Data.ToStringFromUtf8Bytes();

        var introduction = OdinSystemSerializer.Deserialize<Introduction>(data);
        var file = FileItem.File;
        var recipient = FileItem.Recipient;

        bool success = false;
        try
        {
            var clientAuthToken = FileItem.State.GetClientAccessToken();

            ApiResponse<HttpContent> response = null;
            await TryRetry.WithDelayAsync(
                Configuration.Host.PeerOperationMaxAttempts,
                Configuration.Host.PeerOperationDelayMs,
                cancellationToken,
                async () =>
                {
                    var json = OdinSystemSerializer.Serialize(introduction);
                    var encryptedPayload = SharedSecretEncryptedPayload.Encrypt(json.ToUtf8ByteArray(), clientAuthToken.SharedSecret);
                    var client = odinHttpClientFactory.CreateClientUsingAccessToken<ICircleNetworkPeerConnectionsClient>(recipient,
                        clientAuthToken.ToAuthenticationToken());

                    response = await client.MakeIntroduction(encryptedPayload);
                    success = response.IsSuccessStatusCode;
                });

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