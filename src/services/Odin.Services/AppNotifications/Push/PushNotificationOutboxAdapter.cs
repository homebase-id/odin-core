using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Services.Mediator;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Services.AppNotifications.Push;

public class PushNotificationOutboxAdapter(
    ILogger<PushNotificationOutboxAdapter> logger,
    PeerOutboxProcessorAsync outboxProcessorAsync)
    : INotificationHandler<PushNotificationEnqueuedNotification>
{
    public async Task Handle(PushNotificationEnqueuedNotification notificationEnqueuedNotification, CancellationToken cancellationToken)
    {
        logger.LogDebug("PushNotificationOutboxAdapter starting outbox processing");
        await outboxProcessorAsync.StartOutboxProcessingAsync(notificationEnqueuedNotification.OdinContext,
            notificationEnqueuedNotification.DatabaseConnection);
    }
}