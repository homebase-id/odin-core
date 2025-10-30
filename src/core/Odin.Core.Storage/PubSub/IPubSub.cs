using System;
using System.Threading.Tasks;
using Odin.Core.Json;

namespace Odin.Core.Storage.PubSub;

#nullable enable

/// <summary>
/// Fire-and-forget publish/subscribe with at-most-once delivery semantics.
/// All messages are transported as serialized JSON (via JsonEnvelope) to maintain
/// implementation parity between in-process and distributed (Redis) backends.
/// This ensures no serialization surprises when switching implementations, but
/// trades in-process performance for consistency.
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
    /// <returns>IPubSubSubscription. Use this to unsubscribe from the named channel.</returns>
    Task<IPubSubSubscription> SubscribeAsync(string channel, Func<JsonEnvelope, Task> handler);

    /// <summary>
    /// Unsubscribe all channels
    /// </summary>
    /// <returns></returns>
    Task UnsubscribeAllAsync();
}

public interface ISystemPubSub : IPubSub;
public interface ITenantPubSub : IPubSub;

public interface IPubSubSubscription : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Unsubscribe from messages
    /// </summary>
    Task UnsubscribeAsync();
}

