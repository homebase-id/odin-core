using Youverse.Core.Identity;
using Youverse.Core.Serialization;

namespace Youverse.Core.Services.AppNotifications.ClientNotifications;

public class NewFollowerNotification : IClientNotification
{
    public ClientNotificationType NotificationType { get; }=ClientNotificationType.NewFollower;
        
    public OdinId DotYouId { get; set; }
        
    public string GetClientData()
    {
        return DotYouSystemSerializer.Serialize(new
        {
            Sender = this.DotYouId.Id
        });
    }
}