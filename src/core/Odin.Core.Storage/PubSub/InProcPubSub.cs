using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        var namedChannel = NamedChannels.GetOrAdd(channelName, _ =>
        {
            var nc = new NamedChannel(logger, channelName);
            Task.Run(async () =>
            {
                await nc.ProcessMessagesAsync(_cts.Token);
            });
            return nc;
        });

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

    public Task ShutdownAsync()
    {
        // SEB:TODO
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
        private readonly Channel<object> _channel = Channel.CreateUnbounded<object>();
        private readonly ConcurrentDictionary<object, HandlerRegistration> _handlers = new();

        //

        public void Publish<T>(string senderId, T message)
        {
            if (_handlers.IsEmpty)
            {
                return;
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
            _handlers.GetOrAdd(handler, _ => new HandlerRegistration
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
            });
        }

        //

        public void RemoveHandler(object handler)
        {
            _handlers.TryRemove(handler, out _);
        }

        //

        public async Task ProcessMessagesAsync(CancellationToken cancellationToken)
        {
            logger.LogDebug("Started processing messages for channel {ChannelName}", channelName);
            var tasks = new List<Task>();
            try
            {
                await foreach (var message in _channel.Reader.ReadAllAsync(cancellationToken))
                {
                    foreach (var registration in _handlers.Values)
                    {
                        tasks.Add(registration.Handler(message));
                    }
                    await Task.WhenAll(tasks);
                    tasks.Clear();
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
