using System;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Mediator;

namespace Odin.Services.AppNotifications.WebRtcSignaling.Notifications;

public class CallInviteReceivedNotification : MediatorNotificationBase, IClientNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.CallInviteReceived;
    public Guid NotificationTypeId { get; } = Guid.Parse("a3c5f7b1-1f2c-4a4e-9b1a-1d2e3f4a5b6c");

    public Guid CallId { get; init; }
    public OdinId From { get; init; }

    public string GetClientData()
    {
        return OdinSystemSerializer.Serialize(new
        {
            CallId,
            From = From.DomainName,
        });
    }
}
