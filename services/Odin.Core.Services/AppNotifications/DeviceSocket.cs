using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using Odin.Core.Services.Authorization.ExchangeGrants;

namespace Odin.Core.Services.AppNotifications;

public class DeviceSocket
{
    public Guid Key { get; set; }
    public WebSocket Socket { get; set; }
    public ClientAuthenticationToken DeviceAuthToken { get; set; }
    
    /// <summary>
    /// List of drives to which this device socket is subscribed
    /// </summary>
    public List<Guid> Drives { get; set; }

    public SensitiveByteArray SharedSecretKey { get; set; }
}