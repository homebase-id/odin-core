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
    private readonly AsyncLock _lock = new();

    public Guid Key { get; init; }
    public System.Net.WebSockets.WebSocket? Socket { get; init; }
    public IOdinContext? DeviceOdinContext { get; set; }
    public List<Guid> Drives { get; set; } = [];

    //

    public async Task SendMessageAsync(
        string json,
        bool fireAndForget,
        CancellationToken cancellationToken = default)
    {
        if (!fireAndForget)
        {
            await InternalSendAsync(json, cancellationToken);
        }
        else
        {
            _ = InternalSendAsync(json, cancellationToken)
                .ContinueWith(t =>
                {
                    // Not much we can do here, nom-nom
                }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    //

    private async Task InternalSendAsync(string message, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(Socket);
        ArgumentNullException.ThrowIfNull(message);

        using (await _lock.LockAsync(token))
        {
            var jsonBytes = message.ToUtf8ByteArray();
            await Socket.SendAsync(
                buffer: new ArraySegment<byte>(jsonBytes, 0, jsonBytes.Length),
                messageType: WebSocketMessageType.Text,
                messageFlags: GetMessageFlags(endOfMessage: true, compressMessage: true),
                cancellationToken: token);
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