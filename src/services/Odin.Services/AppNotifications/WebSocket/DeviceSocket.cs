using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Nito.AsyncEx;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
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
    /// When true, the socket gets no drive events for writes made by a request with its own token: a client that
    /// applies its writes locally need not hear them back. That includes what the same request changed on the side
    /// (e.g. the transfer history an upload starts). Writes by any other token, files and receipts arriving from
    /// peers, and the server's later work (e.g. the outbox's delivery updates, reaction previews) still arrive.
    /// Sockets sharing one token (e.g. two tabs of an app) also miss each other's writes.
    /// </summary>
    public bool ExcludeOwnWrites { get; set; }

    /// <summary>
    /// What both socket handshakes take from these options: the drives, each of which the caller must be able to read
    /// (OdinSecurityException otherwise), the shape (OdinClientException when unknown) and whose writes to leave
    /// out. <paramref name="ownTokenId"/> is the connection's own token, null where it has none to exclude (a peer);
    /// asking to exclude own writes there is an OdinClientException. Nothing is set on the socket unless all of it
    /// is valid.
    /// </summary>
    public void ApplyTo(DeviceSocket socket, PermissionContext permissions, Guid? ownTokenId)
    {
        var drives = new List<Guid>();
        foreach (var td in Drives ?? [])
        {
            permissions.AssertCanReadDrive(td.Alias);
            drives.Add(td.Alias);
        }

        if (ExcludeOwnWrites && ownTokenId == null)
        {
            throw new OdinClientException("excludeOwnWrites is not available on this connection", OdinClientErrorCode.InvalidQuery);
        }

        // All but the drives first: drives are what route events to the socket, so the rest is in place by then
        socket.ResultShape = FileResultShapes.Resolve(ResultShape, whenAbsent: FileResultShape.Full);
        socket.ExcludedWriterTokenId = ExcludeOwnWrites ? ownTokenId : null;
        socket.Drives = drives;
    }

    /// <summary>
    /// The handshake's options; malformed JSON (e.g. an unknown resultShape name) is the client's error
    /// (InvalidQuery), not the server's
    /// </summary>
    public static EstablishConnectionOptions Parse(string data)
    {
        try
        {
            return OdinSystemSerializer.Deserialize<EstablishConnectionOptions>(data) ?? new EstablishConnectionOptions();
        }
        catch (JsonException e)
        {
            throw new OdinClientException($"Invalid connection options: {e.Message}", OdinClientErrorCode.InvalidQuery);
        }
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
    /// True once the socket's handshake gave it a context with a shared secret, which encrypted messages need
    /// </summary>
    public bool HasSharedSecret => DeviceOdinContext?.PermissionsContext?.SharedSecretKey != null;

    /// <summary>
    /// What drive events sent to this socket describe their file with, as the socket asked at its handshake
    /// </summary>
    public FileResultShape ResultShape { get; set; } = FileResultShape.Full;

    /// <summary>
    /// The token whose drive writes this socket does not get, as it asked at its handshake; null for all writes
    /// </summary>
    public Guid? ExcludedWriterTokenId { get; set; }

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