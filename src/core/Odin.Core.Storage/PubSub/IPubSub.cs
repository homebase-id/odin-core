using System;
using System.Threading.Tasks;

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
    /// <param name="message">Message to publish.</param>
    Task PublishAsync<T>(string channel, T? message);

    /// <summary>
    /// Publish a string message, at-most-once delivery semantics.
    /// This is a compile time JSON friendly wrapper method, i.e. you get an error if you try to publish anything
    /// that is not a string.
    /// </summary>
    /// <param name="channel">Name of channel to publish message to.</param>
    /// <param name="message">Message to publish.</param>
    Task PublishStringAsync(string channel, string message);

    /// <summary>
    /// Subscribe to messages on named channel.
    /// </summary>
    /// <param name="channel">Name of channel to get message from.</param>
    /// <param name="handler">Handler being called with the message. Mismatching types are dropped.</param>
    /// <returns>UnsubscribeToken. Use this to unsubscribe to the named channel.</returns>
    Task<object> SubscribeAsync<T>(string channel, Func<T?, Task> handler);

    /// <summary>
    /// Subscribe to string messages on named channel.
    /// This is a compile time JSON friendly wrapper method, i.e. you get an error if you try to subscribe
    /// with a handler not explicitly accepting string param.
    /// </summary>
    /// <param name="channel">Name of channel to get message from.</param>
    /// <param name="handler">Handler being called with the string message</param>
    /// <returns>UnsubscribeToken. Use this to unsubscribe to the named channel.</returns>
    Task<object> SubscribeStringAsync(string channel, Func<string?, Task> handler);

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

