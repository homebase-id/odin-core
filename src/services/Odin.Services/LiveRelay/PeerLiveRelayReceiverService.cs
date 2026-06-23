using System.Threading.Tasks;
using MediatR;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Base;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer;
using Odin.Services.Util;

namespace Odin.Services.LiveRelay;

/// <summary>
/// Recipient side (hop 2 ingress): accepts an opaque live data point from a connected peer, retains
/// the sender's last point in the (ephemeral) store, and publishes it to the matching app's
/// connected sockets. Nothing durable is written.
/// </summary>
public class PeerLiveRelayReceiverService
{
    private readonly CircleNetworkService _circleNetworkService;
    private readonly LiveRelayRetainedStore _store;
    private readonly IMediator _mediator;

    public PeerLiveRelayReceiverService(
        CircleNetworkService circleNetworkService,
        LiveRelayRetainedStore store,
        IMediator mediator)
    {
        _circleNetworkService = circleNetworkService;
        _store = store;
        _mediator = mediator;
    }

    public async Task<PeerTransferResponse> ReceiveAsync(LiveRelayPeerEnvelope envelope, IOdinContext odinContext)
    {
        odinContext.Caller.AssertCallerIsAuthenticated();

        OdinValidationUtils.AssertNotNull(envelope, nameof(envelope));
        OdinValidationUtils.AssertNotEmptyGuid(envelope.ChannelKey, nameof(envelope.ChannelKey));
        OdinValidationUtils.AssertNotEmptyGuid(envelope.AppId, nameof(envelope.AppId));
        OdinValidationUtils.AssertNotNullOrEmpty(envelope.Blob, nameof(envelope.Blob));

        var caller = odinContext.GetCallerOdinIdOrFail();

        // Accept only from a connected identity. (overrideHack: the peer-transfer context lacks the
        // ReadConnections permission; mirrors PeerAppNotificationService.)
        var isConnected = (await _circleNetworkService.GetIcrAsync(caller, odinContext, overrideHack: true)).IsConnected();
        if (!isConnected)
        {
            throw new OdinSecurityException("Caller not connected");
        }

        var receivedAt = UnixTimeUtc.Now();

        await _store.PutAsync(envelope.AppId, envelope.ChannelKey, caller, envelope.Blob, receivedAt);

        await _mediator.Publish(new LiveRelayNotification
        {
            SenderOdinId = caller,
            ChannelKey = envelope.ChannelKey,
            Blob = envelope.Blob,
            ReceivedAt = receivedAt,
            TargetAppId = envelope.AppId,
            OdinContext = odinContext
        });

        return new PeerTransferResponse
        {
            Code = PeerResponseCode.AcceptedIntoInbox
        };
    }
}
