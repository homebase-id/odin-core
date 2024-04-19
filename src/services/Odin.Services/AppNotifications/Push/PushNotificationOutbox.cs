using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
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
        //PRIMARY KEY (fileId,recipient)
        var recipient = odinContext.Tenant;

        //TODO: do i need to capture the sender as part of the outbox structure is the state alone ok?
        var fileId = record.Options.TagId;

        var state = OdinSystemSerializer.Serialize(record).ToUtf8ByteArray();

        tenantSystemStorage.Outbox.Insert(new OutboxRecord()
        {
            driveId = _notificationBoxId,
            recipient = recipient,
            fileId = fileId,
            priority = 0, //super high priority to ensure these are sent quickly
            type = (int)OutboxItemType.PushNotification,
            value = state
        });

        return Task.CompletedTask;
    }
    
    public Task MarkComplete(Guid marker)
    {
        tenantSystemStorage.Outbox.CompleteAndRemove(marker);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Add and item back the queue due to a failure
    /// </summary>
    public async Task MarkFailure(Guid marker)
    {
        tenantSystemStorage.Outbox.CheckInAsCancelled(marker, UnixTimeUtc.Now().AddSeconds(-3));
        await Task.CompletedTask;
    }

    public async Task<List<PushNotificationOutboxRecord>> GetBatchForProcessing(int batchSize)
    {
        // var records = _tenantSystemStorage.Outbox.PopSpecificBox(_notificationBoxId, batchSize);

        var record = tenantSystemStorage.Outbox.CheckOutItem();
        OutboxRecord[] records = [record];

        var items = records.Select(r =>
        {
            
            var record = OdinSystemSerializer.Deserialize<PushNotificationOutboxRecord>(r.value.ToStringFromUtf8Bytes());
            // record.Timestamp = r.timeStamp.milliseconds;
            record.Marker = r.checkOutStamp.GetValueOrDefault();

            return record;
        });

        return await Task.FromResult(items.ToList());
    }
}

public class PushNotificationOutboxRecord
{
    public OdinId SenderId { get; set; }
    public AppNotificationOptions Options { get; set; }
    public long Timestamp { get; set; }

    public Guid Marker { get; set; }
}