using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Reactions;

public class AddRemoteReactionOutboxWorker(
    OutboxFileItem fileItem,
    ILogger<AddRemoteReactionOutboxWorker> logger,
    IOdinHttpClientFactory odinHttpClientFactory,
    OdinConfiguration odinConfiguration
) : OutboxWorkerBase(fileItem, logger, null)
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
        decryptedClientAuthTokenBytes.WriteZeros(); //never send the client auth token; even if encrypted

        async Task<ApiResponse<PeerResponseCode>> TrySendRequest()
        {
            var client = odinHttpClientFactory.CreateClientUsingAccessToken<IPeerGroupReactionHttpClient>(
                recipient,
                clientAuthToken,
                item.FileSystemType);

            return await client.AddReaction(item);
        }

        try
        {
            ApiResponse<PeerResponseCode> response = null;

            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                cancellationToken,
                async () => { response = await TrySendRequest(); });

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

    protected override Task<UnixTimeUtc> HandleRecoverableTransferStatus(IOdinContext odinContext, DatabaseConnection cn,
        OdinOutboxProcessingException e)
    {
        var nextRunTime = CalculateNextRunTime(e.TransferStatus);
        return Task.FromResult(nextRunTime);
    }

    protected override Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> HandleUnrecoverableTransferStatus(OdinOutboxProcessingException e,
        IOdinContext odinContext,
        DatabaseConnection cn)
    {
        return Task.FromResult((false, UnixTimeUtc.ZeroTime));
    }
}