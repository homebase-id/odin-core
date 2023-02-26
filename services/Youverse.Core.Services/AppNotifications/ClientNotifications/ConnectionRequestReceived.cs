using Youverse.Core.Identity;
using Youverse.Core.Serialization;

namespace Youverse.Core.Services.AppNotifications.ClientNotifications
{
    public class ConnectionRequestReceived : IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.ConnectionRequestReceived;

        public OdinId Sender { get; set; }

        public string GetClientData()
        {
            return DotYouSystemSerializer.Serialize(new
            {
                Sender = this.Sender.Id
            });
        }
    }
}