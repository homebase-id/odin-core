using System;
using System.Collections.Concurrent;
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

    public ConcurrentDictionary<Guid, DeviceSocket> GetAll()
    {
        return _sockets;
    }

    public void AddSocket(DeviceSocket socket)
    {
        _sockets.TryAdd(socket.Key, socket);
    }

    public void RemoveSocket(Guid key)
    {
        _sockets.TryRemove(key, out _);
    }
}

public class SharedDeviceSocketCollection<TRegisteredService> : DeviceSocketCollection
    where TRegisteredService : notnull
{
}