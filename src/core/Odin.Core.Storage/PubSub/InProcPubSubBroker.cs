using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Json;

namespace Odin.Core.Storage.PubSub;

#nullable enable

public class InProcPubSubBroker(ILogger<InProcPubSubBroker> logger, int maxQueuedMessages = 100000)
{
    private readonly ConcurrentDictionary<string, NamedChannel> _namedChannels = new();

    //

    public void Publish(string channel, JsonEnvelope envelope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel, nameof(channel));

        if (_namedChannels.TryGetValue(channel, out var namedChannel))
        {
            namedChannel.Publish(envelope);
        }
    }

    //

    public IPubSubSubscription Subscribe(IPubSub subscriber, string channel, Func<JsonEnvelope, Task> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel, nameof(channel));
        ArgumentNullException.ThrowIfNull(handler, nameof(handler));

        var namedChannel = _namedChannels.GetOrAdd(channel, _ => new NamedChannel(logger, channel, maxQueuedMessages));
        var handlerRegistration = namedChannel.AddHandler(subscriber, handler);
        return new InProcPubSubSubscription(this, channel, handlerRegistration);
    }

    //

    public void Unsubscribe(string channel, HandlerRegistration subscription)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel, nameof(channel));

        if (_namedChannels.TryGetValue(channel, out var namedChannel))
        {
            namedChannel.RemoveHandler(subscription);
        }
    }

    //

    public void UnsubscribeAll(IPubSub owner)
    {
        foreach (var namedChannel in _namedChannels.Values)
        {
            namedChannel.RemoveAllHandlers(owner);
        }
    }

    //

    private class NamedChannel(ILogger logger, string channelName, int maxQueuedMessages)
    {
        private readonly Lock _mutex = new();
        private readonly List<HandlerRegistration> _handlers = [];

        private Channel<object>? _channel;
        private CancellationTokenSource? _cts;

        //

        public void Publish(JsonEnvelope envelope)
        {
            lock (_mutex)
            {
                if (_channel == null || _handlers.Count == 0)
                {
                    return;
                }

                _channel.Writer.TryWrite(envelope);
            }
        }

        //

        public HandlerRegistration AddHandler(IPubSub owner, Func<JsonEnvelope, Task> handler)
        {
            var handlerRegistration = new HandlerRegistration
            {
                Owner = owner,
                Handler = async envelope =>
                {
                    try
                    {
                        await handler(envelope);
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Handler failed: {message}", e.Message);
                    }
                }
            };

            lock (_mutex)
            {
                _handlers.Add(handlerRegistration);

                if (_handlers.Count == 1)
                {
                    _channel = Channel.CreateBounded<object>(new BoundedChannelOptions(maxQueuedMessages)
                    {
                        FullMode = BoundedChannelFullMode.DropOldest
                    });
                    _cts = new CancellationTokenSource();

                    Task
                        .Run(StartProcessingMessages)
                        .ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                            {
                                logger.LogError(t.Exception, "Processing task failed");
                            }
                        });
                }
            }

            return handlerRegistration;
        }

        //

        public void RemoveHandler(HandlerRegistration handlerRegistration)
        {
            lock (_mutex)
            {
                if (_handlers.Remove(handlerRegistration) && _handlers.Count == 0)
                {
                    _cts?.Cancel();
                    _cts?.Dispose();
                    _cts = null;
                    _channel = null;
                }
            }
        }

        //

        public void RemoveAllHandlers(IPubSub owner)
        {
            lock (_mutex)
            {
                _handlers.RemoveAll(h => ReferenceEquals(h.Owner, owner));
                if (_handlers.Count == 0)
                {
                    _cts?.Cancel();
                    _cts?.Dispose();
                    _cts = null;
                    _channel = null;
                }
            }
        }

        //

        private async Task StartProcessingMessages()
        {
            var channel = _channel ?? throw new InvalidOperationException("Channel not set");
            var cancellationToken = _cts?.Token ?? throw new InvalidOperationException("Token not set");

            logger.LogDebug("Started processing messages for channel {ChannelName}", channelName);
            try
            {
                await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken))
                {
                    if (message is not JsonEnvelope envelope)
                    {
                        continue;
                    }

                    List<HandlerRegistration> handlerRegistrations;
                    lock (_mutex)
                    {
                        handlerRegistrations = new List<HandlerRegistration>(_handlers);
                    }

                    foreach (var handlerRegistration in handlerRegistrations)
                    {
                        // Fire-and-forget
                        _ = handlerRegistration.Handler(envelope);
                    }
                }
            }
            catch (OperationCanceledException e) when (e.CancellationToken == cancellationToken)
            {
                logger.LogDebug("Stopped processing messages for channel {ChannelName}", channelName);
            }
            catch (Exception e)
            {
                logger.LogError(e, "StartProcessingMessages: {Message}", e.Message);
            }
        }
    }

    //

    public class HandlerRegistration
    {
        public required IPubSub Owner { get; init; }
        public required Func<JsonEnvelope, Task> Handler { get; init; }
    }

}

