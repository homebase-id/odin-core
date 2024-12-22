using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
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

        try
        {
            var result = await introductionService.SendAutoConnectIntroduceeRequest(iid, cancellationToken, odinContext);
            //
            // switch (result.IssueType)
            // {
            //     case PeerRequestIssueType.SocketError:
            //     case PeerRequestIssueType.ServiceUnavailable:
            //     case PeerRequestIssueType.InternalServerError:
            //     case PeerRequestIssueType.OperationCancelled:
            //     case PeerRequestIssueType.HttpRequestFailed:
            //         break;
            //
            //     case PeerRequestIssueType.None:
            //     case PeerRequestIssueType.BadRequest:
            //     case PeerRequestIssueType.ForbiddenWithInvalidRemoteIcr:
            //     case PeerRequestIssueType.Forbidden:
            //     case PeerRequestIssueType.Unhandled:
            //         break;
            //     default:
            //         throw new ArgumentOutOfRangeException();
            // }
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
        //TODO: change to calculated 
        return Task.FromResult(UnixTimeUtc.Now().AddMinutes(10));
    }

    protected override Task HandleUnrecoverableTransferStatus(OdinOutboxProcessingException e, IOdinContext odinContext)
    {
        return Task.CompletedTask;
    }
}