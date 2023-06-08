using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drives;

namespace Youverse.Core.Services.AppNotifications.ClientNotifications
{
    public class FileAddedNotification : IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.FileAdded;

        public OdinId Sender { get; set; }

        public ExternalFileIdentifier TempFile { get; set; }

        public string GetClientData()
        {
            return "{TODO:'info on file'}";
        }
    }
}