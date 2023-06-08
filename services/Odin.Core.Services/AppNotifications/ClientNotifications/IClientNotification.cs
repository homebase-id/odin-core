using MediatR;
using Youverse.Core.Services.Apps;

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
}