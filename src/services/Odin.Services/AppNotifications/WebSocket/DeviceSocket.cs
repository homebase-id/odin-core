using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Drives;

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
}

public class DeviceSocket
{
    private CancellationTokenSource _cancelTimeoutToken = null!;

    // private readonly Queue<Tuple<Guid, string>> _messageQueue = new();
    private readonly OrderedDictionary _messageQueue = new();
    private DateTime _lastSentTime = DateTime.MinValue;

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

    private bool LongTimeNoSee()
    {
        return (DateTime.UtcNow - _lastSentTime).TotalMilliseconds >= this.ForcePushInterval.TotalMilliseconds;
    }


    public async Task EnqueueMessage(string json, Guid? groupId = null, CancellationToken? cancellationToken = null)
    {
        if (null == Socket)
        {
            throw new OdinSystemException("Socket is null during EnqueueMessage");
        }

        _messageQueue[groupId.GetValueOrDefault(Guid.NewGuid())] = json;

        if (_messageQueue.Count >= BatchSize || this.LongTimeNoSee())
        {
            await ProcessBatch(cancellationToken.GetValueOrDefault(CancellationToken.None));
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
        
        foreach (var value in _messageQueue.Values)
        {
            var message = (string)value;
            var jsonBytes = message.ToUtf8ByteArray();
            await Socket.SendAsync(
                buffer: new ArraySegment<byte>(jsonBytes, 0, message.Length),
                messageType: WebSocketMessageType.Text,
                messageFlags: GetMessageFlags(endOfMessage: true, compressMessage: true),
                cancellationToken: cancellationToken);
        }
        
        _messageQueue.Clear();

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