using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Services.AppNotifications.WebRtcSignaling.PeerRelay;
using Odin.Services.Base;
using Odin.Services.Membership.Connections;

namespace Odin.Services.AppNotifications.WebRtcSignaling;

// Sender-side relay. The flow is uniform — same-server and cross-server both go through
// the recipient's peer HTTP route. The recipient's server runs the local-delivery branch
// and reports back whether any of the recipient's sockets accepted the message.
public class WebRtcSignalingService(
    IOdinHttpClientFactory odinHttpClientFactory,
    CircleNetworkService circleNetworkService,
    ILogger<WebRtcSignalingService> logger)
{
    public async Task<RelayOutcome> RelayAsync(
        OdinId recipient,
        WebRtcRelayRequest request,
        IOdinContext odinContext,
        CancellationToken cancellationToken = default)
    {
        // Connection check from sender's side. The recipient's server will run the
        // mirror-image check before delivery, so a malicious peer can't push signals
        // toward someone they aren't connected to either.
        var icr = await circleNetworkService.GetIcrAsync(recipient, odinContext, overrideHack: true);
        if (!icr.IsConnected())
        {
            return RelayOutcome.NotConnected;
        }

        var token = icr.CreateClientAccessToken(odinContext.PermissionsContext.GetIcrKey());
        var client = await odinHttpClientFactory.CreateClientUsingAccessTokenAsync<IPeerWebRtcSignalingHttpClient>(
            recipient, token.ToAuthenticationToken());

        try
        {
            var response = await client.Relay(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("WebRTC relay to {recipient} returned {status}", recipient, response.StatusCode);
                return response.StatusCode == HttpStatusCode.Forbidden
                    ? RelayOutcome.NotConnected
                    : RelayOutcome.Offline;
            }

            var body = response.Content;
            if (body == null)
            {
                return RelayOutcome.Offline;
            }

            if (body.Delivered)
            {
                return RelayOutcome.Success;
            }

            return body.Reason switch
            {
                CallUnavailableReason.NotConnected => RelayOutcome.NotConnected,
                CallUnavailableReason.RejectedByServer => RelayOutcome.RejectedByServer,
                _ => RelayOutcome.Offline,
            };
        }
        catch (HttpRequestException e)
        {
            logger.LogDebug(e, "WebRTC relay to {recipient} failed: {message}", recipient, e.Message);
            return RelayOutcome.Offline;
        }
        catch (TaskCanceledException)
        {
            return RelayOutcome.Offline;
        }
    }
}

public sealed record RelayOutcome(bool Delivered, string FailureReason)
{
    public static readonly RelayOutcome Success = new(true, null);
    public static readonly RelayOutcome Offline = new(false, CallUnavailableReason.Offline);
    public static readonly RelayOutcome NotConnected = new(false, CallUnavailableReason.NotConnected);
    public static readonly RelayOutcome RejectedByServer = new(false, CallUnavailableReason.RejectedByServer);
}
