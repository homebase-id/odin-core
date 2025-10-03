using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Security.PasswordRecovery.Shamir;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.PasswordRecovery;

public class SendShardToAutomatedIdentityOutboxWorker(
    OutboxFileItem fileItem,
    FileSystemResolver fileSystemResolver,
    ILogger<SendShardToAutomatedIdentityOutboxWorker> logger,
    OdinConfiguration odinConfiguration,
    IOdinHttpClientFactory odinHttpClientFactory
) : OutboxWorkerBase(fileItem, logger, fileSystemResolver, odinConfiguration)
{
    public async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> Send(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        try
        {
            AssertHasRemainingAttempts();

            logger.LogDebug("Start: Shard to automated identity: {file} to {recipient}", FileItem.File, FileItem.Recipient);

            await PerformanceCounter.MeasureExecutionTime($"Outbox {nameof(SendShardToAutomatedIdentityOutboxWorker)}",
                async () => { await SendShard(cancellationToken); });

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

                throw new OdinSystemException("Failed while handling Outbox Processing Exception", e);
            }
        }
    }

    private async Task SendShard(CancellationToken cancellationToken)
    {
        OdinId recipient = FileItem.Recipient;
        var shard = FileItem.State.DeserializeData<PlayerEncryptedShard>();

        async Task<ApiResponse<PeerTransferResponse>> TrySendFile()
        {
            try
            {
                logger.LogDebug("SendShard BEGIN recipient:{recipient}", recipient.ToString());

                var fauxToken = new ClientAuthenticationToken
                {
                    Id = Configuration.Registry.AutomatedIdentityKey,
                    AccessTokenHalfKey = Configuration.Registry.AutomatedIdentityKey.ToByteArray().ToSensitiveByteArray(),
                    ClientTokenType = ClientTokenType.AutomatedPasswordRecovery
                };
                
                var client = odinHttpClientFactory.CreateClientUsingAccessToken<IPeerPasswordRecoveryHttpClient>(recipient, fauxToken);

                var response = await client.SendShardToAutomatedIdentity(shard);

                logger.LogDebug("SendShard END recipient:{recipient} status:{status}", recipient, response.StatusCode);

                return response;
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "SendOutboxFileItemAsync:TrySendFile recipient:{recipient} (TryRetry) {message}",
                    recipient.ToString(), e.Message);
                throw;
            }
        }

        try
        {
            ApiResponse<PeerTransferResponse> response = null;

            await TryRetry.Create()
                .WithAttempts(Configuration.Host.PeerOperationMaxAttempts)
                .WithDelay(Configuration.Host.PeerOperationDelayMs)
                .WithCancellation(cancellationToken)
                .ExecuteAsync(async () => { response = await TrySendFile(); });

            if (response.IsSuccessStatusCode)
            {
                return;
            }

            throw new OdinOutboxProcessingException("Failed while sending the request")
            {
                TransferStatus = MapPeerErrorResponseHttpStatus(response),
                VersionTag = Guid.Empty,
                GlobalTransitId = Guid.Empty,
                Recipient = recipient,
                File = InternalDriveFileId.Redacted()
            };
        }
        catch (TryRetryException ex)
        {
            var e = ex.InnerException;

            logger.LogDebug(e, "Failed processing outbox item (type={t}) from outbox. Message {e}", FileItem.Type, e?.Message ?? "Unknown");

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
                VersionTag = Guid.Empty,
                GlobalTransitId = Guid.Empty,
                Recipient = recipient,
                File = InternalDriveFileId.Redacted()
            };
        }
    }

    protected override async Task<UnixTimeUtc> HandleRecoverableTransferStatus(IOdinContext odinContext,
        OdinOutboxProcessingException e)
    {
        var update = new UpdateTransferHistoryData()
        {
            IsInOutbox = true,
            LatestTransferStatus = e.TransferStatus,
            VersionTag = null
        };

        var nextRunTime = CalculateNextRunTime(e.TransferStatus);

        logger.LogDebug(e, "Recoverable: Updating TransferHistory file {file} to status {status}.  Next Run Time {nrt} sec", e.File,
            e.TransferStatus,
            nextRunTime.AddMilliseconds(UnixTimeUtc.Now().milliseconds * -1).seconds);

        var fs = FileSystemResolver.ResolveFileSystem(FileItem.State.TransferInstructionSet.FileSystemType);
        await fs.Storage.UpdateTransferHistory(FileItem.File, FileItem.Recipient, update, odinContext, null);

        return nextRunTime;
    }

    protected override async Task HandleUnrecoverableTransferStatus(OdinOutboxProcessingException e,
        IOdinContext odinContext)
    {
        logger.LogDebug(e, "Unrecoverable: Updating TransferHistory file {file} to status {status}.", e.File, e.TransferStatus);

        var update = new UpdateTransferHistoryData()
        {
            IsInOutbox = false,
            LatestTransferStatus = e.TransferStatus,
            VersionTag = null
        };

        var fs = FileSystemResolver.ResolveFileSystem(FileItem.State.TransferInstructionSet.FileSystemType);
        await fs.Storage.UpdateTransferHistory(FileItem.File, FileItem.Recipient, update, odinContext, null);
    }
}