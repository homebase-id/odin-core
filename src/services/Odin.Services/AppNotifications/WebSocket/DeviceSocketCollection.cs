using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Odin.Services.AppNotifications.WebSocket;

/// <summary>
/// All devices connected for app notifications
/// </summary>
public class DeviceSocketCollection
{
    private readonly ConcurrentDictionary<Guid, DeviceSocket> _sockets = new();

    public Dictionary<Guid, DeviceSocket> GetAll()
    {
        return _sockets.ToDictionary();
    }

    public void AddSocket(DeviceSocket socket)
    {
        _sockets.TryAdd(socket.Key, socket);
    }

    public async Task RemoveSocket(Guid key, WebSocketCloseStatus status = WebSocketCloseStatus.NormalClosure, string message = "")
    {
        if (_sockets.TryRemove(key, out var entry))
        {
            if (entry.Socket != null)
            {
                if (entry.Socket.State != WebSocketState.Closed && entry.Socket.State != WebSocketState.Aborted)
                {
                    try
                    {
                        await entry.Socket.CloseAsync(status, message, CancellationToken.None);
                    }
                    catch (Exception)
                    {
                        // End of the line - nothing we can do here
                    }
                }
            }
        }
    }
}

public class SharedDeviceSocketCollection<TRegisteredService> : DeviceSocketCollection
    where TRegisteredService : notnull
{
}