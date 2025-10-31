using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Json;
using Odin.Core.Serialization;
using StackExchange.Redis;

namespace Odin.Core.Storage.PubSub;

#nullable enable

public class RedisPubSub(ILogger logger, IConnectionMultiplexer connectionMultiplexer, string channelPrefix)
    : IPubSub, IDisposable
{
    private readonly ISubscriber _publisher = connectionMultiplexer.GetSubscriber();
    private readonly ISubscriber _subscriber = connectionMultiplexer.GetSubscriber();

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _subscriber.UnsubscribeAll();
    }

    //

    public async Task PublishAsync(string channel, JsonEnvelope envelope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel, nameof(channel));
        ArgumentNullException.ThrowIfNull(envelope, nameof(envelope));

        var json = OdinSystemSerializer.Serialize(envelope);
        await _publisher.PublishAsync(RedisChannel.Literal(channelPrefix + ":" + channel), json);
    }

    //

    public async Task<IPubSubSubscription> SubscribeAsync(string channel, Func<JsonEnvelope, Task> handler)
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

            JsonEnvelope? envelope;
            try
            {
                envelope = OdinSystemSerializer.DeserializeOrThrow<JsonEnvelope>(json);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to deserialize JsonEnvelope for {Channel}", channel);
                return;
            }

            _ = SafeInvokeAsync(handler, envelope, channel);
        };

        await _subscriber.SubscribeAsync(RedisChannel.Literal(channelPrefix + ":" + channel), action);

        return new RedisPubSubSubscription(this, channel, action);
    }

    //

    private async Task SafeInvokeAsync(Func<JsonEnvelope, Task> handler, JsonEnvelope envelope, string channel)
    {
        try
        {
            await handler(envelope);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Handler failed for {Channel}", channel);
        }
    }

    //

    public async Task UnsubscribeAsync(string channel, Action<RedisChannel, RedisValue> subscription)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel, nameof(channel));
        await _subscriber.UnsubscribeAsync(RedisChannel.Literal(channelPrefix + ":" + channel), subscription);
    }

    //

    public async Task UnsubscribeAllAsync()
    {
        await _subscriber.UnsubscribeAllAsync();
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
