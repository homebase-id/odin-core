using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Peer.AppNotification;

public class AppNotificationTokenResponse
{
    public ClientAccessToken Token { get; set; }
}