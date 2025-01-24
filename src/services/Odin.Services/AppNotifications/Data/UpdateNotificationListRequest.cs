using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Services.Drives;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing;
using Odin.Core.Time;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.AppNotifications.Data;

//TODO: background job that sends these packages based on timer/logic/weather conditions
public class PushNotificationPackage
{
    private List<AppNotification> Notifications { get; set; }
}

public class AppNotification
{
    public Guid Id { get; set; }
    public string SenderId { get; set; }
    public bool Unread { get; set; }

    public UnixTimeUtc Created { get; set; }

    public AppNotificationOptions Options { get; set; }
}

public class AddNotificationResult
{
    public Guid NotificationId { get; set; }
}

public class NotificationsCountResult
{
    public Dictionary<Guid, int> UnreadCounts { get; set; }
}

public class NotificationsListResult
{
    public string Cursor { get; set; }
    public List<AppNotification> Results { get; set; }
}

public class GetNotificationListRequest
{
    public Guid? AppId { get; set; }
    public Guid? TypeId { get; set; }

    public int Count { get; set; }

    public UnixTimeUtcUnique? Cursor { get; set; }
}

public class MarkNotificationsAsReadRequest
{
    public Guid AppId { get; set; }
    public Guid TypeId { get; set; }
}

public class AddNotificationRequest
{
    public AppNotificationOptions AppNotificationOptions { get; set; }
    public long Timestamp { get; set; }
}

public class DeleteNotificationsRequest
{
    public List<Guid> IdList { get; set; }
}

public class UpdateNotificationListRequest
{
    public List<UpdateNotificationRequest> Updates { get; set; }
}

public class UpdateNotificationRequest
{
    public Guid Id { get; set; }

    public bool Unread { get; set; }
}