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

    /// <summary>
    /// What each drive event describes its file with. When null, <see cref="FileResultShape.Full"/>: the full
    /// header, as before this option existed.
    /// </summary>
    public FileResultShape? ResultShape { get; set; }

    /// <summary>
    /// What both socket handshakes take from these options: the drives, each of which the caller must be able to read
    /// (OdinSecurityException otherwise), and the shape (OdinClientException when unknown). Nothing is set on the
    /// socket unless all of it is valid.
    /// </summary>
    public void ApplyTo(DeviceSocket socket, PermissionContext permissions)
    {
        var drives = new List<Guid>();
        foreach (var td in Drives)
        {
            permissions.AssertCanReadDrive(td.Alias);
            drives.Add(td.Alias);
        }

        var shape = FileResultShapes.Resolve(ResultShape, whenAbsent: FileResultShape.Full);
        socket.Drives = drives;
        socket.ResultShape = shape;
    }
}

public class DeviceSocket
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);
    private readonly AsyncLock _lock = new();

    public Guid Key { get; init; }
    public System.Net.WebSockets.WebSocket? Socket { get; init; }
    public IOdinContext? DeviceOdinContext { get; set; }

    /// <summary>
    /// The app this socket is authenticated as, if any. Used to deliver app-scoped notifications
    /// (e.g. live relay data) only to sockets belonging to the matching app.
    /// </summary>
    public Guid? AppId { get; set; }

    public List<Guid> Drives { get; set; } = [];

    /// <summary>
    /// What drive events sent to this socket describe their file with, as the socket asked at its handshake
    /// </summary>
    public FileResultShape ResultShape { get; set; } = FileResultShape.Full;
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