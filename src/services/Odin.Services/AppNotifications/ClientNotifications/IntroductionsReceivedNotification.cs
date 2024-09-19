using System;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Mediator;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Services.AppNotifications.ClientNotifications;

public class IntroductionsReceivedNotification : MediatorNotificationBase, IClientNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.IntroductionsReceived;
    public Guid NotificationTypeId { get; } = Guid.Parse("f100bfa0-ac4e-468a-9322-bdaf6059ec8a");

    public DatabaseConnection DatabaseConnection { get; init; }
    public OdinId IntroducerOdinId { get; init; }
    public Introduction Introduction { get; init; }

    public string GetClientData()
    {
        return OdinSystemSerializer.Serialize(new
        {
            IntroducerOdinId = this.IntroducerOdinId,
            Introduction = this.Introduction
        });
    }
}

public class IntroductionsAcceptedNotification : MediatorNotificationBase, IClientNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.IntroductionAccepted;
    public Guid NotificationTypeId { get; } = Guid.Parse("f56ee792-56dd-45fd-8f9e-f96bb5d0e3de");

    public DatabaseConnection DatabaseConnection { get; init; }
    public OdinId IntroducerOdinId { get; init; }
    public OdinId Recipient { get; init; }

    public string GetClientData()
    {
        return OdinSystemSerializer.Serialize(new
        {
            IntroducerOdinId = this.IntroducerOdinId,
            Recipient = Recipient
        });
    }
}
