using System;
using System.Threading.Tasks;
using Odin.Core.Json;

namespace Odin.Core.Storage.PubSub;

#nullable enable

/// <summary>
/// Fire-and-forget publish/subscribe with at-most-once delivery semantics.
/// </summary>
public interface IPubSub
{
    /// <summary>
    /// Publish message, at-most-once delivery semantics.
    /// </summary>
    /// <param name="channel">Name of channel to publish message to.</param>
    /// <param name="envelope">Envelope to publish.</param>
    Task PublishAsync(string channel, JsonEnvelope envelope);

    /// <summary>
    /// Subscribe to messages on named channel.
    /// </summary>
    /// <param name="channel">Name of channel to get message from.</param>
    /// <param name="handler">Handler being called with the envelope. Mismatching types are dropped.</param>
    /// <returns>UnsubscribeToken. Use this to unsubscribe to the named channel.</returns>
    Task<object> SubscribeAsync(string channel, Func<JsonEnvelope, Task> handler);

    /// <summary>
    /// Unsubscribe from a named channel.
    /// </summary>
    /// <param name="channel">Name of the channel to unsubscribe from.</param>
    /// <param name="unsubscribeToken">The token returned by <see cref="SubscribeAsync"/></param>
    /// <returns></returns>
    Task UnsubscribeAsync(string channel, object unsubscribeToken);

    /// <summary>
    /// Unsubscribe all channels
    /// </summary>
    /// <returns></returns>
    Task UnsubscribeAllAsync();
}

public interface ISystemPubSub : IPubSub;
public interface ITenantPubSub : IPubSub;

