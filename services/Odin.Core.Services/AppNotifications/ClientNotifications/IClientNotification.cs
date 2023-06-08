using MediatR;

namespace Odin.Core.Services.AppNotifications.ClientNotifications
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