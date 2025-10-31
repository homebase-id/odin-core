using System;
using System.Threading.Tasks;
using Odin.Core.Json;

namespace Odin.Core.Storage.PubSub;

#nullable enable

//

public class InProcPubSub(InProcPubSubBroker broker, string channelPrefix) : IPubSub, IDisposable
{

    //

    public Task PublishAsync(string channel, JsonEnvelope envelope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel, nameof(channel));

        var channelName = channelPrefix + ':' + channel;
        broker.Publish(channelName, envelope);

        return Task.CompletedTask;
    }

    //

    public Task<IPubSubSubscription> SubscribeAsync(string channel, Func<JsonEnvelope, Task> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel, nameof(channel));
        ArgumentNullException.ThrowIfNull(handler, nameof(handler));

        var channelName = channelPrefix + ':' + channel;
        var subscription = broker.Subscribe(this, channelName, handler);

        return  Task.FromResult(subscription);
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
        UnsubscribeAllAsync().GetAwaiter().GetResult();;
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
