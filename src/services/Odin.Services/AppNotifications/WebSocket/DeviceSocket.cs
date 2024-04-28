using System;
using System.Collections.Generic;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Services.AppNotifications.WebSocket;

public class DeviceSocket
{
    public Guid Key { get; set; }
    public System.Net.WebSockets.WebSocket Socket { get; set; }
    public IOdinContext DeviceOdinContext { get; set; }

    /// <summary>
    /// List of drives to which this device socket is subscribed
    /// </summary>
    public List<Guid> Drives { get; set; } = [];

}