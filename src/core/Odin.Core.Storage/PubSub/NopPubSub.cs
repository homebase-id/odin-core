using System;
using System.Threading.Tasks;

namespace Odin.Core.Storage.PubSub;

public class NopPubSub : ISystemPubSub, ITenantPubSub
{
    public Task PublishAsync<T>(string channel, T message)
    {
        return Task.CompletedTask;
    }

    //

    public Task SubscribeAsync<T>(string channel, MessageFromSelf messageFromSelf, Func<T, Task> handler)
    {
        return Task.CompletedTask;
    }

    //

    public Task UnsubscribeAsync(string channel)
    {
        return Task.CompletedTask;
    }
}
