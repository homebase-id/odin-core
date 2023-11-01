using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.AppNotifications.WebSocket;

namespace Odin.Core.Services.AppNotifications.ClientNotifications;

public class NewFollowerNotification : IClientNotification
{
    public ClientNotificationType NotificationType { get; }=ClientNotificationType.NewFollower;
        
    public OdinId OdinId { get; set; }
        
    public string GetClientData()
    {
        return OdinSystemSerializer.Serialize(new
        {
            Sender = this.OdinId.DomainName
        });
    }
}