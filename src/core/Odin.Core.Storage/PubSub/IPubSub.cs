using System;
using System.Threading.Tasks;

namespace Odin.Core.Storage.PubSub;

#nullable enable

public enum MessageFromSelf
{
    Ignore,
    Process
}

public interface IPubSub
{
    Task PublishAsync<T>(string channel, T message);
    Task SubscribeAsync<T>(string channel, MessageFromSelf messageFromSelf, Func<T, Task> handler);
}
public interface ISystemPubSub : IPubSub;
public interface ITenantPubSub : IPubSub;

