using System;
using System.Threading.Tasks;

namespace Odin.Core.Storage.PubSub;

#nullable enable

//

public class InProcPubSub(InProcPubSubBroker broker, string channelPrefix) : IPubSub, IDisposable
{

    //

    public Task PublishAsync<T>(string channel, T? message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel, nameof(channel));

        var channelName = channelPrefix + ':' + channel;
        broker.Publish(channelName, message);

        return Task.CompletedTask;
    }

    //

    public Task PublishStringAsync(string channel, string message)
    {
        return PublishAsync(channel, message);
    }

    //

    public Task<object> SubscribeAsync<T>(string channel, Func<T?, Task> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel, nameof(channel));
        ArgumentNullException.ThrowIfNull(handler, nameof(handler));

        var channelName = channelPrefix + ':' + channel;
        var unsubscribeToken = broker.Subscribe(this, channelName, handler);

        return  Task.FromResult(unsubscribeToken);
    }

    //

    public Task<object> SubscribeStringAsync(string channel, Func<string?, Task> handler)
    {
        return SubscribeAsync(channel, handler);
    }

    //

    // Note: unsubscribeToken must be the same instance as returned by SubscribeAsync
    public Task UnsubscribeAsync(string channel, object unsubscribeToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel, nameof(channel));

        var channelName = channelPrefix + ':' + channel;
        broker.Unsubscribe(channelName, unsubscribeToken);

        return Task.CompletedTask;
    }

    //

    public Task UnsubscribeAllAsync()
    {
        broker.UnsubscribeAll(this);
        return Task.CompletedTask;
    }

    //

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        UnsubscribeAllAsync();
    }

    //

}

//

public class SystemInProcPubSub(InProcPubSubBroker broker)
    : InProcPubSub(broker, "system"), ISystemPubSub;

//

public class TenantInProcPubSub(InProcPubSubBroker broker, ChannelPrefix channelPrefix)
    : InProcPubSub(broker, channelPrefix), ITenantPubSub;

//
