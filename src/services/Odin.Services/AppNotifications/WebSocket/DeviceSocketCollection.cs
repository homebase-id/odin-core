using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Odin.Services.AppNotifications.WebSocket;

/// <summary>
/// All devices connected for app notifications
/// </summary>
/// <param name="closeTimeout">
/// How long closing a socket waits for the client's close frame before aborting it (default <see cref="DefaultCloseTimeout"/>)
/// </param>
public class DeviceSocketCollection(ILogger<DeviceSocketCollection> logger, TimeSpan? closeTimeout = null)
{
    /// <summary>
    /// A client that stopped reading never answers a close, and an unbounded wait held the identity's status gate:
    /// pausing it hung, and every status change after it (#1854)
    /// </summary>
    public static readonly TimeSpan DefaultCloseTimeout = TimeSpan.FromSeconds(5);

    private readonly TimeSpan _closeTimeout = closeTimeout ?? DefaultCloseTimeout;
    private readonly ConcurrentDictionary<Guid, DeviceSocket> _sockets = new();

    public Dictionary<Guid, DeviceSocket> GetAll()
    {
        return _sockets.ToDictionary();
    }

    public void AddSocket(DeviceSocket socket)
    {
        _sockets.TryAdd(socket.Key, socket);
    }

    /// <summary>
    /// Closes and removes every socket; returns how many there were
    /// </summary>
    public async Task<int> RemoveAllSocketsAsync(WebSocketCloseStatus status, string message)
    {
        var keys = _sockets.Keys.ToList();
        await Task.WhenAll(keys.Select(key => RemoveSocket(key, status, message)));
        return keys.Count;
    }

    public async Task RemoveSocket(Guid key, WebSocketCloseStatus status = WebSocketCloseStatus.NormalClosure, string message = "")
    {
        if (_sockets.TryRemove(key, out var entry) && entry.Socket is { } socket &&
            socket.State is not (WebSocketState.Closed or WebSocketState.Aborted))
        {
            await CloseAsync(socket, status, message);
        }
    }

    // The close handshake, bounded: past the timeout the socket is aborted, which also ends its handler's pending receive.
    // WaitAsync as well as the token, in case a receive already pending on the socket keeps the close from noticing.
    private async Task CloseAsync(System.Net.WebSockets.WebSocket socket, WebSocketCloseStatus status, string message)
    {
        using var timeout = new CancellationTokenSource(_closeTimeout);
        try
        {
            await socket.CloseAsync(status, message, timeout.Token).WaitAsync(_closeTimeout);
        }
        catch (Exception e) when (e is OperationCanceledException or TimeoutException)
        {
            logger.LogInformation("WebSocket client did not answer the close within {timeout} s; aborted it", _closeTimeout.TotalSeconds);
        }
        catch (Exception)
        {
            // End of the line - nothing we can do here
        }
        finally
        {
            if (socket.State != WebSocketState.Closed)
            {
                socket.Abort();
            }
        }
    }
}

public class SharedDeviceSocketCollection<TRegisteredService>(ILogger<DeviceSocketCollection> logger)
    : DeviceSocketCollection(logger)
    where TRegisteredService : notnull
{
}