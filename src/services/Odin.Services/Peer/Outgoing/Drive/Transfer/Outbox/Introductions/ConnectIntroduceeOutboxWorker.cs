using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Introductions;

public class ConnectIntroduceeOutboxWorker(
    OutboxFileItem fileItem,
    ILogger<ConnectIntroduceeOutboxWorker> logger,
    OdinConfiguration odinConfiguration,
    CircleNetworkIntroductionService introductionService) : OutboxWorkerBase(fileItem, logger, null, odinConfiguration)
{
    public async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> Send(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        var data = FileItem.State.Data.ToStringFromUtf8Bytes();

        var iid = OdinSystemSerializer.Deserialize<IdentityIntroduction>(data);
        var file = FileItem.File;
        var recipient = FileItem.Recipient;

        AssertHasRemainingAttempts();

        try
        {
            await introductionService.SendAutoConnectIntroduceeRequest(iid, cancellationToken, odinContext);
        }
        catch (OdinClientException e) when (e.ErrorCode == OdinClientErrorCode.RemoteServerReturnedForbidden)
        {
            // Recipient blocked us (or otherwise refused at the network edge). Equivalent to the
            // OdinSecurityException case below — retrying won't change the answer, mark complete.
            return (true, UnixTimeUtc.ZeroTime);
        }
        catch (OdinClientException e)
        {
            // Warning, with the code: this is the retry path, and it used to wait out a flat 10 minutes
            // without saying why, so a stalled introduction could not be diagnosed from the logs (#1778).
            logger.LogWarning(e, "ConnectIntroducee to {recipient} failed with {code} (attempt {attempt}); retrying",
                recipient, e.ErrorCode, FileItem.AttemptCount);
            return (false, CalculateBackoffNextRunTime());
        }
        catch (OdinSecurityException)
        {
            return (true, UnixTimeUtc.ZeroTime);
        }
        catch (Exception ex)
        {
            var status = (ex is TaskCanceledException or HttpRequestException or OperationCanceledException)
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

        return (true, UnixTimeUtc.ZeroTime);
    }

    protected override Task<UnixTimeUtc> HandleRecoverableTransferStatus(IOdinContext odinContext, OdinOutboxProcessingException e)
    {
        return Task.FromResult(CalculateBackoffNextRunTime());
    }

    protected override Task HandleUnrecoverableTransferStatus(OdinOutboxProcessingException e, IOdinContext odinContext)
    {
        return Task.CompletedTask;
    }
}