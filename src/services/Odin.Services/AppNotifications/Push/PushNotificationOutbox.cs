using System;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.Base;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Services.AppNotifications.Push;

/// <summary>
/// The outbox of notifications that need to be pushed to all subscribed devices
/// </summary>
public class PushNotificationOutbox(TenantSystemStorage tenantSystemStorage)
{
    const string NotificationBoxId = "21a409e0-7cc2-4e97-b28d-93ef04c94a9c";
    private readonly Guid _notificationBoxId = Guid.Parse(NotificationBoxId);

    public Task Add(PushNotificationOutboxRecord record, IOdinContext odinContext)
    {
        var recipient = odinContext.Tenant;
        var fileId = record.Options.TagId;
        var state = OdinSystemSerializer.Serialize(record).ToUtf8ByteArray();
        
        tenantSystemStorage.Outbox.Insert(new OutboxRecord()
        {
            // driveId = _notificationBoxId,
            driveId = Guid.NewGuid(),
            recipient = recipient,
            fileId = fileId,
            priority = 0, //super high priority to ensure these are sent quickly
            type = (int)OutboxItemType.PushNotification,
            value = state
        });

        return Task.CompletedTask;
    }
}

public class PushNotificationOutboxRecord
{
    public OdinId SenderId { get; set; }
    public AppNotificationOptions Options { get; set; }
    public long Timestamp { get; set; }
}