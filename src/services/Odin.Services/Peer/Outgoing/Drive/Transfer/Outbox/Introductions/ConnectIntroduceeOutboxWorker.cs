using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Introductions;

public class ConnectIntroduceeOutboxWorker(
    OutboxFileItem fileItem,
    ILogger<ConnectIntroduceeOutboxWorker> logger,
    OdinConfiguration odinConfiguration,
    CircleNetworkIntroductionService introductionService) : OutboxWorkerBase(fileItem, logger, null, odinConfiguration)
{
    public Task<OutboxProcessingResult> Send(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        return SendHandledAsync(SendInternalAsync, odinContext, cancellationToken);
    }

    private async Task<OutboxProcessingResult> SendInternalAsync(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        var iid = OdinSystemSerializer.Deserialize<IdentityIntroduction>(FileItem.State.Data.ToStringFromUtf8Bytes());

        // Out of attempts: SendHandledAsync settles it as unrecoverable.
        AssertHasRemainingAttempts();

        try
        {
            await introductionService.SendAutoConnectIntroduceeRequest(iid, cancellationToken, odinContext);
            return OutboxProcessingResult.Complete();
        }
        catch (Exception e) when (e is OdinSecurityException
                                      or OdinClientException { ErrorCode: OdinClientErrorCode.RemoteServerReturnedForbidden })
        {
            // Recipient blocked us, or refused at the network edge: retrying won't change the answer.
            return OutboxProcessingResult.Complete();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down: let the processor reschedule it as it does any cancelled item.
            throw;
        }
        catch (Exception e)
        {
            // Anything else -- a client error, the network, or a local fault such as a locked database -- is
            // retried on the backoff. Not HandleOutboxProcessingException: it drops UnknownServerError on the
            // first attempt, which lost an introduction to a briefly unreachable recipient for good (#1778).
            logger.LogWarning(e, "ConnectIntroducee to {recipient} failed with {code} (attempt {attempt}); retrying",
                FileItem.Recipient, (e as OdinClientException)?.ErrorCode, FileItem.AttemptCount);
            return OutboxProcessingResult.Retry(CalculateBackoffNextRunTime());
        }
    }

    protected override Task<UnixTimeUtc> HandleRecoverableTransferStatus(IOdinContext odinContext, OdinOutboxProcessingException e)
    {
        return Task.FromResult(CalculateBackoffNextRunTime());
    }

    protected override Task HandleUnrecoverableTransferStatus(OdinOutboxProcessingException e, IOdinContext odinContext)
    {
        logger.LogWarning("ConnectIntroducee to {recipient} gave up after {attempts} attempts ({status})",
            FileItem.Recipient, FileItem.AttemptCount, e.TransferStatus);
        return Task.CompletedTask;
    }
}