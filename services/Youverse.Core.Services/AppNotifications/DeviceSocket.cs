using System;
using System.Net.WebSockets;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.AppNotifications;

public class DeviceSocket
{
    public Guid Key { get; set; }
    public WebSocket Socket { get; set; }
    public ClientAuthenticationToken DeviceAuthToken { get; set; }
}