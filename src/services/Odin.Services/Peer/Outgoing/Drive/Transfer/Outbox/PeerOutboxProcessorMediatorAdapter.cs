using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Odin.Services.Background;
using Odin.Services.Mediator;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

public class PeerOutboxProcessorMediatorAdapter(IBackgroundServiceTrigger backgroundServiceTrigger)
    : INotificationHandler<OutboxItemAddedNotification>
{
    public Task Handle(OutboxItemAddedNotification notification, CancellationToken cancellationToken)
    {
        backgroundServiceTrigger.PulseBackgroundProcessor(nameof(PeerOutboxProcessorBackgroundService));
        return Task.CompletedTask;
    }
}