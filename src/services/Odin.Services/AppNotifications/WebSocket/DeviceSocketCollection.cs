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

    public void RemoveSocket(Guid key)
    {
        _sockets.TryRemove(key, out _);
    }
}

public class SharedDeviceSocketCollection<TRegisteredService> : DeviceSocketCollection
    where TRegisteredService : notnull
{
}