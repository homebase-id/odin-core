using System;
using Odin.Core.Identity;
using Odin.Core.Services.AppNotifications.WebSocket;
using Odin.Core.Services.Drives;

namespace Odin.Core.Services.AppNotifications.ClientNotifications
{
    public class FileAddedNotification : IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.FileAdded;

        public Guid NotificationTypeId { get; }

        public OdinId Sender { get; set; }

        public ExternalFileIdentifier TempFile { get; set; }

        public string GetClientData()
        {
            return "{TODO:'info on file'}";
        }
    }
}