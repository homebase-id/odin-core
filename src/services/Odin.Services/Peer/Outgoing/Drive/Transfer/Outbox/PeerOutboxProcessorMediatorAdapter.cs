using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Odin.Services.Background;
using Odin.Services.Mediator;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

public class PeerOutboxProcessorMediatorAdapter(IBackgroundServiceNotifier<PeerOutboxProcessorBackgroundService> backgroundServiceNotifier)
    : INotificationHandler<OutboxItemAddedNotification>
{
    public async Task Handle(OutboxItemAddedNotification notification, CancellationToken cancellationToken)
    {
        await backgroundServiceNotifier.NotifyWorkAvailableAsync();
    }
}