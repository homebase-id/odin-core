using System;
using System.Threading.Tasks;

namespace Odin.Core.Storage.PubSub;

#nullable enable

public interface IPubSub
{
    Task PublishAsync<T>(string channel, T message);
    Task SubscribeAsync<T>(string channel, Func<T, Task> handler);
}
public interface ISystemPubSub : IPubSub;
public interface ITenantPubSub : IPubSub;

