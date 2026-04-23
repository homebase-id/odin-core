using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.WebRtcSignaling;
using Odin.Services.AppNotifications.WebRtcSignaling.Notifications;
using Odin.Services.AppNotifications.WebRtcSignaling.PeerRelay;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Peer;

namespace Odin.Hosting.Controllers.PeerIncoming.WebRtc;

// Receives a single WebRTC signaling envelope forwarded from the sender's identity server,
// confirms the sender is a connected identity from the recipient's perspective, and pushes
// the typed *Received notification down all of the recipient's open WebSocket sessions via
// the standard MediatR/PubSub fan-out used by other client notifications.
[ApiController]
[Route(PeerApiPathConstants.WebRtcV1)]
[Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCapiAuthScheme)]
[ApiExplorerSettings(GroupName = "peer-v1")]
public class PeerIncomingWebRtcRelayController(
    IMediator mediator,
    AppNotificationHandler notificationHandler,
    ILogger<PeerIncomingWebRtcRelayController> logger)
    : OdinControllerBase
{
    [HttpPost("relay")]
    public async Task<WebRtcRelayResponse> Relay([FromBody] WebRtcRelayRequest request, CancellationToken cancellationToken)
    {
        if (!WebOdinContext.Caller.IsConnected)
        {
            return Unavailable(CallUnavailableReason.NotConnected);
        }

        var sender = WebOdinContext.Caller.OdinId;
        if (!sender.HasValue)
        {
            return Unavailable(CallUnavailableReason.RejectedByServer);
        }

        if (!notificationHandler.HasOpenSockets())
        {
            return Unavailable(CallUnavailableReason.Offline);
        }

        var notification = BuildNotification(request, sender.Value);
        if (notification == null)
        {
            logger.LogDebug("Unknown WebRTC signal type {type} from {sender}", request.SignalType, sender);
            return Unavailable(CallUnavailableReason.RejectedByServer);
        }

        await mediator.Publish(notification, cancellationToken);
        return new WebRtcRelayResponse { Delivered = true };
    }

    private IClientNotification BuildNotification(WebRtcRelayRequest request, OdinId from)
    {
        return request.SignalType switch
        {
            WebRtcSignalType.Invite => new CallInviteReceivedNotification
            {
                CallId = request.CallId,
                From = from,
                OdinContext = WebOdinContext,
            },
            WebRtcSignalType.Offer => new CallOfferReceivedNotification
            {
                CallId = request.CallId,
                From = from,
                Sdp = request.Sdp,
                OdinContext = WebOdinContext,
            },
            WebRtcSignalType.Answer => new CallAnswerReceivedNotification
            {
                CallId = request.CallId,
                From = from,
                Sdp = request.Sdp,
                OdinContext = WebOdinContext,
            },
            WebRtcSignalType.Ice => new CallIceReceivedNotification
            {
                CallId = request.CallId,
                From = from,
                Candidate = request.Candidate,
                SdpMid = request.SdpMid,
                SdpMLineIndex = request.SdpMLineIndex,
                OdinContext = WebOdinContext,
            },
            WebRtcSignalType.Hangup => new CallHangupReceivedNotification
            {
                CallId = request.CallId,
                From = from,
                OdinContext = WebOdinContext,
            },
            WebRtcSignalType.Reject => new CallRejectReceivedNotification
            {
                CallId = request.CallId,
                From = from,
                OdinContext = WebOdinContext,
            },
            _ => null,
        };
    }

    private static WebRtcRelayResponse Unavailable(string reason) =>
        new() { Delivered = false, Reason = reason };
}
