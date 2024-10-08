using System;
using MediatR;
using Odin.Services.AppNotifications.WebSocket;

namespace Odin.Services.AppNotifications.ClientNotifications
{
    public interface IClientNotification : INotification
    {
        ClientNotificationType NotificationType { get; }

        public Guid NotificationTypeId { get; }
        
        /// <summary>
        /// Data needed by the client to usefully process the notification.
        /// </summary>
        /// <returns></returns>
        string GetClientData();
    }
}