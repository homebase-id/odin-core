using System;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Odin.Core.Storage.PubSub;

public sealed class RedisPubSubSubscription(
    RedisPubSub pubSub,
    string channel,
    Action<RedisChannel, RedisValue> subscription) : IPubSubSubscription
{
    private volatile bool _disposed;

    //

    public async Task UnsubscribeAsync()
    {
        await pubSub.UnsubscribeAsync(channel, subscription);
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
