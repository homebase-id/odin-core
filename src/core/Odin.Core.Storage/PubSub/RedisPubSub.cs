using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using StackExchange.Redis;

namespace Odin.Core.Storage.PubSub;

#nullable enable

public class RedisPubSub(ILogger logger, IConnectionMultiplexer connectionMultiplexer, string channelPrefix)
    : IPubSub, IDisposable
{
    private readonly ISubscriber _publisher = connectionMultiplexer.GetSubscriber();
    private readonly ISubscriber _subscriber = connectionMultiplexer.GetSubscriber();

    //

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _subscriber.UnsubscribeAll();
    }

    //

    public async Task PublishAsync<T>(string channel, T? message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel, nameof(channel));

        var envelope = new Envelope<T>
        {
            Payload = message
        };
        var json = OdinSystemSerializer.Serialize(envelope);
        await _publisher.PublishAsync(RedisChannel.Literal(channelPrefix + ":" + channel), json);
    }

    //

    public Task PublishStringAsync(string channel, string message)
    {
        return PublishAsync(channel, message);
    }

    //

    public async Task<object> SubscribeAsync<T>(string channel, Func<T?, Task> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel, nameof(channel));
        ArgumentNullException.ThrowIfNull(handler, nameof(handler));

        Action<RedisChannel, RedisValue> action = (ch, message) =>
        {
            var json = message.ToString();
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            var envelope = OdinSystemSerializer.DeserializeOrThrow<Envelope<T>>(json);

            // Fire-and-forget
            _ = SafeInvokeAsync(handler, envelope.Payload, channel);
        };

        await _subscriber.SubscribeAsync(RedisChannel.Literal(channelPrefix + ":" + channel), action);

        return action;
    }

    //

    public Task<object> SubscribeStringAsync(string channel, Func<string?, Task> handler)
    {
        return SubscribeAsync(channel, handler);
    }

    //

    private async Task SafeInvokeAsync<T>(Func<T, Task> handler, T instance, string channel)
    {
        try
        {
            await handler(instance);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Handler failed for {Channel}", channel);
        }
    }

    //

    // Note: unsubscribeToken must be the same instance as returned by SubscribeAsync
    public async Task UnsubscribeAsync(string channel, object unsubscribeToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel, nameof(channel));

        if (unsubscribeToken is not Action<RedisChannel, RedisValue> action)
        {
            throw new ArgumentException("Handler is of incorrect type", nameof(unsubscribeToken));
        }

        await _subscriber.UnsubscribeAsync(RedisChannel.Literal(channelPrefix + ":" + channel), action);
    }

    //

    public async Task UnsubscribeAllAsync()
    {
        await _subscriber.UnsubscribeAllAsync();
    }

    //

    private class Envelope<T>
    {
        public T? Payload { get; set; }
    }

}

//

public class SystemRedisPubSub(
    ILogger<SystemRedisPubSub> logger,
    IConnectionMultiplexer connectionMultiplexer)
    : RedisPubSub(logger, connectionMultiplexer, "system"), ISystemPubSub;

//

public class TenantRedisPubSub(
    ILogger<TenantRedisPubSub> logger,
    IConnectionMultiplexer connectionMultiplexer,
    ChannelPrefix channelPrefix)
    : RedisPubSub(logger, connectionMultiplexer, channelPrefix), ITenantPubSub;

//
