using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Odin.Services.Mediator;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

public class PeerOutboxProcessorMediatorAdapter(PeerOutboxProcessorBackgroundService outboxProcessorBackgroundService)
    : INotificationHandler<OutboxItemAddedNotification>
{
    public Task Handle(OutboxItemAddedNotification notification, CancellationToken cancellationToken)
    {
        outboxProcessorBackgroundService.PulseBackgroundProcessor();
        return Task.CompletedTask;
    }
}