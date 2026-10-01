using System;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer;
using Odin.Services.Util;

namespace Odin.Services.LiveRelay;

/// <summary>
/// Recipient side (hop 2 ingress): accepts an opaque live data point from a connected peer, retains
/// the sender's last point in the (ephemeral) store, and publishes it to the matching app's
/// connected sockets. Nothing durable is written for the point itself; a push riding on it, if any,
/// goes through the normal push pipeline (notification list + outbox) with the caller as sender.
/// </summary>
public class PeerLiveRelayReceiverService
{
    private readonly CircleNetworkService _circleNetworkService;
    private readonly LiveRelayRetainedStore _store;
    private readonly IMediator _mediator;
    private readonly IAppRegistrationService _appRegistrationService;
    private readonly PushNotificationService _pushNotificationService;
    private readonly ILogger<PeerLiveRelayReceiverService> _logger;

    public PeerLiveRelayReceiverService(
        CircleNetworkService circleNetworkService,
        LiveRelayRetainedStore store,
        IMediator mediator,
        IAppRegistrationService appRegistrationService,
        PushNotificationService pushNotificationService,
        ILogger<PeerLiveRelayReceiverService> logger)
    {
        _circleNetworkService = circleNetworkService;
        _store = store;
        _mediator = mediator;
        _appRegistrationService = appRegistrationService;
        _pushNotificationService = pushNotificationService;
        _logger = logger;
    }

    public async Task<PeerTransferResponse> ReceiveAsync(LiveRelayPeerEnvelope envelope, IOdinContext odinContext)
    {
        odinContext.Caller.AssertCallerIsAuthenticated();

        OdinValidationUtils.AssertNotNull(envelope, nameof(envelope));
        OdinValidationUtils.AssertNotEmptyGuid(envelope.ChannelKey, nameof(envelope.ChannelKey));
        OdinValidationUtils.AssertNotEmptyGuid(envelope.AppId, nameof(envelope.AppId));
        OdinValidationUtils.AssertNotNullOrEmpty(envelope.Blob, nameof(envelope.Blob));
        if (envelope.Push != null)
        {
            PushDeliveryOptionsValidation.AssertValid(envelope.Push);
        }

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

        // Last, so a push problem can never cost the socket delivery above.
        if (envelope.Push != null)
        {
            await TryEnqueuePushAsync(envelope, caller, odinContext);
        }

        return new PeerTransferResponse
        {
            Code = PeerResponseCode.AcceptedIntoInbox
        };
    }

    /// <summary>
    /// The recipient's consent to be woken is having the app installed: no app registration, no
    /// push. The same gate the push worker applies later, applied early so an uninstalled app
    /// leaves no notification-list row either. Failures are logged, never surfaced: the relay
    /// itself already succeeded.
    /// </summary>
    private async Task TryEnqueuePushAsync(LiveRelayPeerEnvelope envelope, OdinId caller, IOdinContext odinContext)
    {
        try
        {
            var push = envelope.Push;
            push.AppId = envelope.AppId;
            push.Recipients = null;
            push.PeerSubscriptionId = Guid.Empty;

            // Grants SendPushNotifications, as every peer-originated push gets.
            var pushContext = OdinContextUpgrades.UpgradeToPeerTransferContext(odinContext);

            var appRegistration = await _appRegistrationService.GetAppRegistration(envelope.AppId, pushContext);
            if (appRegistration == null)
            {
                _logger.LogDebug("Live relay push from {caller} skipped: app {appId} is not registered on this identity",
                    caller, envelope.AppId);
                return;
            }

            await _pushNotificationService.EnqueueNotification(caller, push, pushContext);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Live relay push from {caller} for app {appId} could not be enqueued; the relay itself was delivered",
                caller, envelope.AppId);
        }
    }
}
