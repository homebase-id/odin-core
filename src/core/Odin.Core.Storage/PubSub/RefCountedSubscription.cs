using System;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;
using Odin.Core.Json;

namespace Odin.Core.Storage.PubSub;

#nullable enable

public sealed class RefCountedSubscription(
    IPubSub pubSub,
    string channel,
    Func<JsonEnvelope, Task> handler) : IDisposable
{
    private readonly AsyncLock _mutex = new();
    private IPubSubSubscription? _subscription;
    private int _subscriptionCount;

    public int SubscriptionCount => _subscriptionCount;
    public bool IsSubscribed => _subscription != null;

    //

    public async Task SubscribeAsync(CancellationToken cancellationToken = default)
    {
        using (await _mutex.LockAsync(cancellationToken))
        {
            if (++_subscriptionCount > 1)
            {
                // Already subscribed
                return;
            }

            _subscription = await pubSub.SubscribeAsync(
                channel,
                async envelope => await handler(envelope));
        }
    }

    //

    public async Task UnsubscribeAsync(CancellationToken cancellationToken = default)
    {
        using (await _mutex.LockAsync(cancellationToken))
        {
            if (_subscriptionCount < 1)
            {
                return;
            }

            _subscriptionCount--;
            if (_subscriptionCount > 0)
            {
                return;
            }

            try
            {
                if (_subscription != null)
                {
                    await _subscription.UnsubscribeAsync();
                }
            }
            finally
            {
                _subscription = null;
                _subscriptionCount = 0;
            }
        }
    }

    //

    public void Dispose()
    {
        UnsubscribeAsync().GetAwaiter().GetResult();
    }

    //
}