using System;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Mediator;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox
{
    /// <summary>
    /// Services that manages items in a given Tenant's outbox
    /// </summary>
    public class PeerOutbox(TenantSystemStorage tenantSystemStorage, IMediator mediator)
    {
        /// <summary>
        /// Adds an item to be encrypted and moved to the outbox
        /// </summary>
        public async Task AddItem(OutboxFileItem fileItem, bool useUpsert = false)
        {
            var record = new OutboxRecord()
            {
                driveId = fileItem.File.DriveId,
                recipient = fileItem.Recipient,
                fileId = fileItem.File.FileId,
                dependencyFileId = fileItem.DependencyFileId,
                type = (int)fileItem.Type,
                priority = fileItem.Priority,
                value = OdinSystemSerializer.Serialize(fileItem.State).ToUtf8ByteArray()
            };

            if (useUpsert)
            {
                tenantSystemStorage.Outbox.Upsert(record);
            }
            else
            {
                tenantSystemStorage.Outbox.Insert(record);
            }

            await mediator.Publish(new OutboxItemAddedNotification());
            PerformanceCounter.IncrementCounter($"Outbox Item Added {fileItem.Type}");
            
        }

        public Task MarkComplete(Guid marker, IdentityDatabase db)
        {
            tenantSystemStorage.Outbox.CompleteAndRemove(marker);
            
            PerformanceCounter.IncrementCounter("Outbox Mark Complete");

            return Task.CompletedTask;
        }

        /// <summary>
        /// Add and item back the queue due to a failure
        /// </summary>
        public Task MarkFailure(Guid marker, UnixTimeUtc nextRun, IdentityDatabase db)
        {
            tenantSystemStorage.Outbox.CheckInAsCancelled(marker, nextRun);
            
            PerformanceCounter.IncrementCounter("Outbox Mark Failure");

            return Task.CompletedTask;
        }

        public Task<int> RecoverDead(UnixTimeUtc time, IdentityDatabase db)
        {
            var recovered = tenantSystemStorage.Outbox.RecoverCheckedOutDeadItems(time);
            
            PerformanceCounter.IncrementCounter("Outbox Recover Dead");

            return Task.FromResult(recovered);
        }

        public async Task<OutboxFileItem> GetNextItem(IdentityDatabase db)
        {
            var record = tenantSystemStorage.Outbox.CheckOutItem();
            
            if (null == record)
            {
                return await Task.FromResult<OutboxFileItem>(null);
            }

            PerformanceCounter.IncrementCounter("Outbox Item Checkout");

            OutboxItemState state;
            state = OdinSystemSerializer.Deserialize<OutboxItemState>(record.value.ToStringFromUtf8Bytes());

            var item = new OutboxFileItem()
            {
                File = new InternalDriveFileId()
                {
                    DriveId = record.driveId,
                    FileId = record.fileId
                },

                Recipient = (OdinId)record.recipient,
                Priority = record.priority,
                AddedTimestamp = record.created.ToUnixTimeUtc().seconds,
                Type = (OutboxItemType)record.type,

                AttemptCount = record.checkOutCount,
                Marker = record.checkOutStamp.GetValueOrDefault(),
                State = state
            };

            return await Task.FromResult(item);
        }

        public Task<bool> HasOutboxFileItem(OutboxFileItem fileItem, IdentityDatabase db)
        {
            var records = tenantSystemStorage.Outbox.Get(fileItem.File.DriveId, fileItem.File.FileId);
            var hasRecord = records?.Any(r => r.type == (int)OutboxItemType.File) ?? false;
            return Task.FromResult(hasRecord);
        }

        /// <summary>
        /// Gets the status of the specified Drive
        /// </summary>
        public async Task<OutboxDriveStatus> GetOutboxStatus(Guid driveId, IdentityDatabase db)
        {
            var (totalCount, poppedCount, utc) = tenantSystemStorage.Outbox.OutboxStatusDrive(driveId);
            return await Task.FromResult(new OutboxDriveStatus()
            {
                CheckedOutCount = poppedCount,
                TotalItems = totalCount,
                NextItemRun = utc
            });
        }

        
        /// <summary>
        /// Get time until the next scheduled item should run (if any).
        /// </summary>
        public async Task<TimeSpan?> NextRun(IdentityDatabase db)
        {
            var nextRun = tenantSystemStorage.Outbox.NextScheduledItem();
            if (nextRun == null)
            {
                return null;
            }
            
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(nextRun.Value.milliseconds);
            var now = DateTimeOffset.Now;
            if (dt < now)
            {
                return await Task.FromResult(TimeSpan.Zero);
            }
            
            return await Task.FromResult(dt - now);
        }
    }
}