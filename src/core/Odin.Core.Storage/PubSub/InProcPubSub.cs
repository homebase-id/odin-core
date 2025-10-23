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

//

public class InProcPubSub(ILogger<InProcPubSub> logger, string channelPrefix) : IPubSub, IDisposable
{
    private static readonly ConcurrentDictionary<string, NamedChannel> NamedChannels = new();
    private readonly string _senderId = Guid.NewGuid().ToString();
    private readonly CancellationTokenSource _cts = new();

    //

    public Task PublishAsync<T>(string channel, T message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel, nameof(channel));

        var channelName = channelPrefix + ':' + channel;
        if (message != null && NamedChannels.TryGetValue(channelName, out var namedChannel))
        {
            namedChannel.Publish(_senderId, message);
        }

        return Task.CompletedTask;
    }

    //

    public Task<object> SubscribeAsync<T>(string channel, MessageFromSelf messageFromSelf, Func<T, Task> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel, nameof(channel));
        ArgumentNullException.ThrowIfNull(handler, nameof(handler));

        var channelName = channelPrefix + ':' + channel;
        var namedChannel = NamedChannels.GetOrAdd(channelName, _ => new NamedChannel(logger, channelName));

        namedChannel.AddHandler(_senderId, messageFromSelf, handler);

        return Task.FromResult<object>(handler);
    }

    //

    // Note: unsubscribeToken must be the same instance as returned by SubscribeAsync
    public Task UnsubscribeAsync(string channel, object unsubscribeToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel, nameof(channel));

        var channelName = channelPrefix + ':' + channel;
        if (NamedChannels.TryGetValue(channelName, out var namedChannel))
        {
            namedChannel.RemoveHandler(unsubscribeToken);
        }

        return Task.CompletedTask;
    }

    //

    public Task UnsubscribeAllAsync()
    {
        foreach (var namedChannel in NamedChannels.Values)
        {
            namedChannel.RemoveAllHandlers(_senderId);
        }
        return Task.CompletedTask;
    }

    //

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _cts.Dispose();
    }

    //

    private class Envelope<T>
    {
        public string SenderId { get; set; } = "";
        public T? Payload { get; set; }
    }

    //

    private class NamedChannel(ILogger logger, string channelName)
    {
        private readonly Channel<object> _channel = Channel.CreateBounded<object>(new BoundedChannelOptions(100000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        private readonly Lock _mutex = new();
        private readonly Dictionary<object, HandlerRegistration> _handlers = new();
        private CancellationTokenSource? _cts;

        //

        public void Publish<T>(string senderId, T message)
        {
            lock (_mutex)
            {
                if (_handlers.Count == 0)
                {
                    return;
                }
            }

            var envelope = new Envelope<T>
            {
                SenderId = senderId,
                Payload = message
            };

            _channel.Writer.TryWrite(envelope);
        }

        //

        public void AddHandler<T>(string senderId, MessageFromSelf messageFromSelf, Func<T, Task> handler)
        {
            lock (_mutex)
            {
                if (_handlers.ContainsKey(handler))
                {
                    return;
                }

                _handlers[handler] = new HandlerRegistration
                {
                    SenderId = senderId,
                    MessageFromSelf = messageFromSelf,
                    Handler = async envelope =>
                    {
                        if (envelope is Envelope<T> typed && typed.Payload != null)
                        {
                            if (typed.SenderId != senderId || messageFromSelf == MessageFromSelf.Process)
                            {
                                try
                                {
                                    await handler(typed.Payload);
                                }
                                catch (Exception e)
                                {
                                    logger.LogError(e, "Handler failed");
                                }
                            }
                        }
                    }
                };

                if (_handlers.Count == 1)
                {
                    _cts = new CancellationTokenSource();
                    _ = StartProcessingMessages();
                }
            }
        }

        //

        public void RemoveHandler(object handler)
        {
            lock (_mutex)
            {
                if (_handlers.Remove(handler) && _handlers.Count == 0)
                {
                    _cts?.Cancel();
                    _cts?.Dispose();
                    _cts = null;
                }
            }
        }

        //

        public void RemoveAllHandlers(string senderId)
        {
            lock (_mutex)
            {
                var keysToRemove = _handlers.Where(x => x.Value.SenderId == senderId).Select(kvp => kvp.Key).ToList();
                foreach (var key in keysToRemove)
                {
                    _handlers.Remove(key);
                }
                if (_handlers.Count == 0)
                {
                    _cts?.Cancel();
                    _cts?.Dispose();
                    _cts = null;
                }
            }
        }

        //

        private async Task StartProcessingMessages()
        {
            var cancellationToken = _cts?.Token ?? throw new InvalidOperationException("Token not set");

            logger.LogDebug("Started processing messages for channel {ChannelName}", channelName);
            try
            {
                await foreach (var message in _channel.Reader.ReadAllAsync(cancellationToken))
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
                logger.LogError(e, "ProcessMessagesAsync: {Message}", e.Message);
                throw;
            }
        }

        //

        private class HandlerRegistration
        {
            public required string SenderId { get; set; }
            public required MessageFromSelf MessageFromSelf { get; set; }
            public required Func<object, Task> Handler { get; set; }
        }

    }
}

//

public class SystemInProcPubSub(
    ILogger<SystemInProcPubSub> logger)
    : InProcPubSub(logger, "system"), ISystemPubSub;

//

public class TenantInProcPubSub(
    ILogger<TenantInProcPubSub> logger,
    ChannelPrefix channelPrefix)
    : InProcPubSub(logger, channelPrefix), ITenantPubSub;

//
