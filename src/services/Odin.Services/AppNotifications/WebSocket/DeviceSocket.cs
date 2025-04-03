using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;
using Odin.Core;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Services.AppNotifications.WebSocket;

#nullable enable

public class EstablishConnectionOptions
{
    public List<TargetDrive> Drives { get; set; } = new();
}

public class DeviceSocket
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);
    private readonly AsyncLock _lock = new();

    public Guid Key { get; init; }
    public System.Net.WebSockets.WebSocket? Socket { get; init; }
    public IOdinContext? DeviceOdinContext { get; set; }
    public List<Guid> Drives { get; set; } = [];
    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    //

    public async Task SendMessageAsync(string json, CancellationToken cancellationToken = default)
    {
        await InternalSendAsync(json, cancellationToken);
    }

    //

    public Task FireAndForgetAsync(string json, CancellationToken cancellationToken = default)
    {
        // Fire and forget, so NO await here:
        InternalSendAsync(json, cancellationToken).ContinueWith(_ =>
        {
            // Not much we can do here, nom-nom
        }, TaskContinuationOptions.OnlyOnFaulted);

        return Task.CompletedTask;
    }

    //

    private async Task InternalSendAsync(string message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(Socket);
        ArgumentNullException.ThrowIfNull(message);

        using var timeoutCts = new CancellationTokenSource(Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var linkedToken = linkedCts.Token;

        using (await _lock.LockAsync(linkedToken))
        {
            var jsonBytes = message.ToUtf8ByteArray();
            await Socket.SendAsync(
                buffer: new ArraySegment<byte>(jsonBytes, 0, jsonBytes.Length),
                messageType: WebSocketMessageType.Text,
                messageFlags: GetMessageFlags(endOfMessage: true, compressMessage: true),
                cancellationToken: linkedToken);
        }
    }

    //

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

    //
}