using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Outgoing;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;

namespace Odin.Core.Services.AppNotifications.Data;

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

public class NotificationsListResult
{
    public UnixTimeUtcUnique? Cursor { get; set; }
    public List<AppNotification> Results { get; set; }
    
}

public class GetNotificationListRequest
{
    public int Count { get; set; }
    public UnixTimeUtcUnique? Cursor { get; set; }
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