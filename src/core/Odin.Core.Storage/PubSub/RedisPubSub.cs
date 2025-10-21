using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using StackExchange.Redis;

namespace Odin.Core.Storage.PubSub;

//

public class RedisPubSub(ILogger logger, IConnectionMultiplexer connectionMultiplexer, string channelPrefix)
    : IPubSub, IDisposable
{
    private readonly string _senderId = Guid.NewGuid().ToString();
    private readonly ISubscriber _publisher = connectionMultiplexer.GetSubscriber();
    private readonly ISubscriber _subscriber = connectionMultiplexer.GetSubscriber();

    //

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _subscriber.UnsubscribeAll();
    }

    //

    public async Task PublishAsync<T>(string channel, T message)
    {
        var envelope = new Envelope<T>
        {
            SenderId = _senderId,
            Payload = message
        };
        var json = OdinSystemSerializer.Serialize(envelope);
        await _publisher.PublishAsync(RedisChannel.Literal(channelPrefix + ":" + channel), json);
    }

    //

    public async Task SubscribeAsync<T>(string channel, MessageFromSelf messageFromSelf, Func<T, Task> handler)
    {
        await _subscriber.SubscribeAsync(RedisChannel.Literal(channelPrefix + ":" + channel), (ch, message) =>
        {
            var json = message.ToString();
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            var envelope = OdinSystemSerializer.DeserializeOrThrow<Envelope<T>>(json);

            if (envelope.SenderId == _senderId && messageFromSelf == MessageFromSelf.Ignore)
            {
                // Ignore own messages
                return;
            }

            // Fire-and-forget
            _ = SafeInvokeAsync(handler, envelope.Payload, channel);
        });
    }

    //

    public async Task UnsubscribeAsync(string channel)
    {
        await _subscriber.UnsubscribeAsync(RedisChannel.Literal(channelPrefix + ":" + channel));
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

    private class Envelope<T>
    {
        public string SenderId { get; set; }
        public T Payload { get; set; }
    }

}

//

public class SystemPubSub(
    ILogger<SystemPubSub> logger,
    IConnectionMultiplexer connectionMultiplexer)
    : RedisPubSub(logger, connectionMultiplexer, "system"), ISystemPubSub;

//

public class TenantPubSub(
    ILogger<SystemPubSub> logger,
    IConnectionMultiplexer connectionMultiplexer,
    ChannelPrefix channelPrefix)
    : RedisPubSub(logger, connectionMultiplexer, channelPrefix), ITenantPubSub;

//
