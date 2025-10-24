using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Odin.Core.Storage.PubSub;

#nullable enable

public class InProcPubSubBroker(ILogger<InProcPubSubBroker> logger, int maxQueuedMessages = 100000)
{
    private readonly ConcurrentDictionary<string, NamedChannel> _namedChannels = new();

    //

    public void Publish<T>(string channel, T? message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel, nameof(channel));

        if (_namedChannels.TryGetValue(channel, out var namedChannel))
        {
            namedChannel.Publish(message);
        }
    }

    //

    public object Subscribe<T>(IPubSub subscriber, string channel, Func<T?, Task> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel, nameof(channel));
        ArgumentNullException.ThrowIfNull(handler, nameof(handler));

        var namedChannel = _namedChannels.GetOrAdd(channel, _ => new NamedChannel(logger, channel, maxQueuedMessages));

        return namedChannel.AddHandler(subscriber, handler);
    }

    //

    public void Unsubscribe(string channel, object unsubscribeToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel, nameof(channel));

        if (_namedChannels.TryGetValue(channel, out var namedChannel))
        {
            namedChannel.RemoveHandler(unsubscribeToken);
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
        private readonly Dictionary<object, HandlerRegistration> _handlers = new();

        private Channel<object>? _channel;
        private CancellationTokenSource? _cts;

        //

        public void Publish<T>(T? message)
        {
            lock (_mutex)
            {
                if (_channel == null || _handlers.Count == 0)
                {
                    return;
                }

                var envelope = new Envelope<T>
                {
                    Payload = message
                };

                _channel.Writer.TryWrite(envelope);
            }
        }

        //

        public object AddHandler<T>(IPubSub owner, Func<T?, Task> handler)
        {
            var unsubscribeToken = Guid.NewGuid();

            lock (_mutex)
            {
                _handlers[unsubscribeToken] = new HandlerRegistration
                {
                    Owner = owner,
                    Handler = async envelope =>
                    {
                        if (envelope is Envelope<T> typed)
                        {
                            try
                            {
                                await handler(typed.Payload);
                            }
                            catch (Exception e)
                            {
                                logger.LogError(e, "Handler failed: {message}", e.Message);
                            }

                        }
                    }
                };

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

            return unsubscribeToken;
        }

        //

        public void RemoveHandler(object unsubscribeToken)
        {
            lock (_mutex)
            {
                if (_handlers.Remove(unsubscribeToken) && _handlers.Count == 0)
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
                var keysToRemove = _handlers
                    .Where(x => ReferenceEquals(x.Value.Owner, owner))
                    .Select(kvp => kvp.Key).ToList();
                foreach (var key in keysToRemove)
                {
                    _handlers.Remove(key);
                }
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
                    List<HandlerRegistration> handlerRegistrations;
                    lock (_mutex)
                    {
                        handlerRegistrations = new List<HandlerRegistration>(_handlers.Values);
                    }

                    foreach (var handlerRegistration in handlerRegistrations)
                    {
                        // Fire-and-forget
                        _ = handlerRegistration.Handler(message);
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

        //

        private class HandlerRegistration
        {
            public required IPubSub Owner { get; set; }
            public required Func<object, Task> Handler { get; set; }
        }

    }

    private class Envelope<T>
    {
        public T? Payload { get; set; }
    }
}

