using Youverse.Core.Identity;
using Youverse.Core.Serialization;

namespace Youverse.Core.Services.AppNotifications.ClientNotifications;

public class NewFollowerNotification : IClientNotification
{
    public ClientNotificationType NotificationType { get; }=ClientNotificationType.NewFollower;
        
    public OdinId OdinId { get; set; }
        
    public string GetClientData()
    {
        return DotYouSystemSerializer.Serialize(new
        {
            Sender = this.OdinId.DomainName
        });
    }
}