using System;
using System.Collections.Generic;
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
    private readonly Queue<string> _messageQueue = new();

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
    
    public async Task EnqueueMessage(string json, CancellationToken cancellationToken)
    {
        if (null == Socket)
        {
            throw new OdinSystemException("Socket is null during EnqueueMessage");
        }

        _messageQueue.Enqueue(json);
        if (_messageQueue.Count >= BatchSize)
        {
            await ProcessBatch(cancellationToken);
            ResetTimeout();
        }
        else if (_messageQueue.Count == 1)
        {
            StartTimeout(cancellationToken);
        }
    }

    private async Task ProcessBatch(CancellationToken cancellationToken)
    {
        if (null == Socket)
        {
            return;
        }

        while (_messageQueue.Count > 0)
        {
            var message = _messageQueue.Dequeue();
            var jsonBytes = message.ToUtf8ByteArray();
            await Socket.SendAsync(
                buffer: new ArraySegment<byte>(jsonBytes, 0, message.Length),
                messageType: WebSocketMessageType.Text,
                messageFlags: GetMessageFlags(endOfMessage: true, compressMessage: true),
                cancellationToken: cancellationToken);
        }
    }

    private void StartTimeout(CancellationToken cancellationToken)
    {
        _cancelTimeoutToken = new CancellationTokenSource();
        _ = Task.Delay(ForcePushInterval, cancellationToken).ContinueWith(async t =>
        {
            if (!t.IsCanceled && _messageQueue.Count > 0)
            {
                await ProcessBatch(cancellationToken);
            }
        }, cancellationToken).Unwrap();
    }

    private void ResetTimeout()
    {
        _cancelTimeoutToken.Cancel();
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