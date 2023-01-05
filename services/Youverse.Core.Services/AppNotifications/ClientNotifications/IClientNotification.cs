using MediatR;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.AppNotifications.ClientNotifications
{
    public interface IClientNotification : INotification
    {
        ClientNotificationType NotificationType { get; }

        /// <summary>
        /// Data needed by the client to usefully process the notification.
        /// </summary>
        /// <returns></returns>
        string GetClientData();
    }
    
    public interface IDriveClientNotification : INotification
    {
        ClientNotificationType NotificationType { get; }
        
        public InternalDriveFileId File { get; set; }
        
    }
}