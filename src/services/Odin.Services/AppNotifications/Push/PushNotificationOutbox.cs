using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.Base;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Core.Time;

namespace Odin.Services.AppNotifications.Push;

/// <summary>
/// The outbox of notifications that need to be pushed to all subscribed devices
/// </summary>
public class PushNotificationOutbox
{
    const string NotificationBoxId = "21a409e0-7cc2-4e97-b28d-93ef04c94a9c";
    private readonly Guid _notificationBoxId = Guid.Parse(NotificationBoxId);

    private readonly TenantSystemStorage _tenantSystemStorage;
    private readonly OdinContextAccessor _contextAccessor;

    public PushNotificationOutbox(TenantSystemStorage tenantSystemStorage, OdinContextAccessor contextAccessor)
    {
        _tenantSystemStorage = tenantSystemStorage;
        _contextAccessor = contextAccessor;
    }

    public Task Add(PushNotificationOutboxRecord record)
    {
        //PRIMARY KEY (fileId,recipient)
        var recipient = _contextAccessor.GetCurrent().Tenant;

        //TODO: do i need to capture the sender as part of the outbox structure is the state alone ok?
        var fileId = record.Options.TagId;
        
        var state = OdinSystemSerializer.Serialize(record).ToUtf8ByteArray();

        _tenantSystemStorage.Outbox.Upsert(new OutboxRecord()
        {
            driveId = _notificationBoxId,
            recipient = recipient,
            fileId = fileId,
            priority = 10,
            value = state
        });

        return Task.CompletedTask;
    }

    public Task MarkComplete(Guid marker)
    {
        _tenantSystemStorage.Outbox.CompleteAndRemove(marker);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Add and item back the queue due to a failure
    /// </summary>
    public async Task MarkFailure(Guid marker)
    {
        _tenantSystemStorage.Outbox.CompleteAndRemoveList(marker, listFileId: new List<Guid>());

        //TODO: there is no way to keep information on why an item failed
        _tenantSystemStorage.Outbox.CheckInAsCancelled(marker, UnixTimeUtc.Now().AddMinutes(1));

        await Task.CompletedTask;
    }

    public async Task<List<PushNotificationOutboxRecord>> GetBatchForProcessing(int batchSize)
    {
        var records = new List<OutboxRecord> { _tenantSystemStorage.Outbox.CheckOutItem() };

        var items = records.Select(r =>
        {
            var record = OdinSystemSerializer.Deserialize<PushNotificationOutboxRecord>(r.value.ToStringFromUtf8Bytes());
            // OBSOLETE record.Timestamp = r.timeStamp.milliseconds;
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