using System;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Mediator;

namespace Odin.Services.AppNotifications.WebRtcSignaling.Notifications;

public class CallAnswerReceivedNotification : MediatorNotificationBase, IClientNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.CallAnswerReceived;
    public Guid NotificationTypeId { get; } = Guid.Parse("c3c5f7b1-3f2c-4a4e-9b1a-3d2e3f4a5b6c");

    public Guid CallId { get; init; }
    public OdinId From { get; init; }
    public string Sdp { get; init; }

    public string GetClientData()
    {
        return OdinSystemSerializer.Serialize(new
        {
            CallId,
            From = From.DomainName,
            Sdp,
        });
    }
}
