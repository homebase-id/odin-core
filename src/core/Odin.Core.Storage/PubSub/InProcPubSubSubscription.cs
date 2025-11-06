using System.Threading.Tasks;

namespace Odin.Core.Storage.PubSub;

public sealed class InProcPubSubSubscription(
    InProcPubSubBroker pubSub,
    string channel,
    InProcPubSubBroker.HandlerRegistration subscription) : IPubSubSubscription
{
    private volatile bool _disposed;

    //

    public Task UnsubscribeAsync()
    {
        pubSub.Unsubscribe(channel, subscription);
        return Task.CompletedTask;
    }

    //

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            UnsubscribeAsync().GetAwaiter().GetResult();
        }
    }

    //

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await UnsubscribeAsync();
        }
    }
}
