using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Services.Background;
using Odin.Services.Mediator;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Services.AppNotifications.Push;

public class PushNotificationOutboxAdapter(
    ILogger<PushNotificationOutboxAdapter> logger,
    IBackgroundServiceTrigger backgroundServiceTrigger)
    : INotificationHandler<PushNotificationEnqueuedNotification>
{
    public Task Handle(PushNotificationEnqueuedNotification notificationEnqueuedNotification, CancellationToken cancellationToken)
    {
        logger.LogDebug("PushNotificationOutboxAdapter starting outbox processing");
        backgroundServiceTrigger.PulseBackgroundProcessor(nameof(PeerOutboxProcessorBackgroundService));
        return Task.CompletedTask;
    }
}