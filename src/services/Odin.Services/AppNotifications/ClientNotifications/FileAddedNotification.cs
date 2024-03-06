using System;
using Odin.Core.Identity;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Drives;

namespace Odin.Services.AppNotifications.ClientNotifications
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