using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Util;
using Odin.Services.Base;
using Odin.Services.Drives;
using SQLitePCL;

namespace Odin.Services.AppNotifications.WebSocket;

#nullable enable

public class EstablishConnectionOptions
{
    public List<TargetDrive> Drives { get; set; } = new();

    /// <summary>
    /// Number of events to send in a given push
    /// </summary>
    public int BatchSize { get; init; }

    /// <summary>
    /// Milliseconds to wait between pushes
    /// </summary>
    public int WaitTimeMs { get; init; }

    /// <summary>
    /// List of Ids specified by the client that should receive a socket notification
    /// when another client with the same key establishes a connection  
    /// </summary>
    public List<Guid> OtherOnlineIdentityKeys { get; init; } = new();
}

public class DeviceSocket
{
    private CancellationTokenSource _cancelTimeoutToken = null!;

    // private readonly Queue<Tuple<Guid, string>> _messageQueue = new();
    private readonly OrderedDictionary _messageQueue = new();
    private DateTime _lastSentTime = DateTime.MinValue;
    private readonly object _lock = new object();

    public Guid Key { get; set; }
    public System.Net.WebSockets.WebSocket? Socket { get; set; }
    public IOdinContext? DeviceOdinContext { get; set; }

    /// <summary>
    /// List of drives to which this device socket is subscribed
    /// </summary>
    public List<Guid> Drives { get; set; } = [];

    /// <summary>
    /// Number of events to send in a given push
    /// </summary>
    public int BatchSize { get; set; }

    /// <summary>
    /// Milliseconds interval to push the batch even if it's not reached the batchsize
    /// </summary>
    public TimeSpan ForcePushInterval { get; set; }

    /// <summary>
    /// List of Ids specified by the client that should receive a socket notification
    /// when another client with the same key establishes a connection  
    /// </summary>
    public List<Guid> OtherOnlineIdentityKeys { get; set; } = new();

    private bool LongTimeNoSee()
    {
        var v = (DateTime.UtcNow - _lastSentTime).TotalMilliseconds >= this.ForcePushInterval.TotalMilliseconds;
        return v;
    }


    public async Task EnqueueMessage(string json, Guid? groupId = null, CancellationToken? cancellationToken = null)
    {
        if (null == Socket)
        {
            throw new OdinSystemException("Socket is null during EnqueueMessage");
        }

        lock (_lock)
        {
            _messageQueue[groupId.GetValueOrDefault(Guid.NewGuid())] = json;
        }

        if (LongTimeNoSee())
        {
            await ProcessBatch(cancellationToken.GetValueOrDefault(CancellationToken.None));
            PerformanceCounter.IncrementCounter("App Notification Process Batch(reason:LongTimeNoSee)");
            ResetTimeout();
        }

        if (_messageQueue.Count >= BatchSize)
        {
            await ProcessBatch(cancellationToken.GetValueOrDefault(CancellationToken.None));
            PerformanceCounter.IncrementCounter("App Notification Process Batch(reason: batch full)");
            ResetTimeout();
        }
        else if (_messageQueue.Count == 1)
        {
            await StartTimeout(cancellationToken.GetValueOrDefault(CancellationToken.None));
        }
    }

    private async Task ProcessBatch(CancellationToken cancellationToken)
    {
        if (null == Socket)
        {
            return;
        }

        string[] valuesArray;
        lock (_lock)
        {
            valuesArray = new string[_messageQueue.Values.Count];
            if (_messageQueue?.Values?.Count > 0)
            {
                _messageQueue.Values.CopyTo(valuesArray, 0);
                _messageQueue.Clear();
            }
        }

        foreach (var message in valuesArray)
        {
            var jsonBytes = message.ToUtf8ByteArray();
            await Socket.SendAsync(
                buffer: new ArraySegment<byte>(jsonBytes, 0, message.Length),
                messageType: WebSocketMessageType.Text,
                messageFlags: GetMessageFlags(endOfMessage: true, compressMessage: true),
                cancellationToken: cancellationToken);
        }

        PerformanceCounter.IncrementCounter("AppNotification Batch Sent");

        _lastSentTime = DateTime.UtcNow;
    }

    private async Task StartTimeout(CancellationToken cancellationToken)
    {
        _cancelTimeoutToken = new CancellationTokenSource();
        try
        {
            await Task.Delay(ForcePushInterval, _cancelTimeoutToken.Token);
            if (_messageQueue.Count > 0 && LongTimeNoSee())
            {
                PerformanceCounter.IncrementCounter("App Notification Process Batch(reason: Timeout)");
                await ProcessBatch(cancellationToken);
            }
        }
        catch (TaskCanceledException)
        {
            //gulp
        }
    }

    private void ResetTimeout()
    {
        _cancelTimeoutToken?.Cancel();
    }

    private static WebSocketMessageFlags GetMessageFlags(bool endOfMessage, bool compressMessage)
    {
        var flags = WebSocketMessageFlags.None;

        if (endOfMessage)
        {
            flags |= WebSocketMessageFlags.EndOfMessage;
        }

        if (!compressMessage)
        {
            flags |= WebSocketMessageFlags.DisableCompression;
        }

        return flags;
    }
}