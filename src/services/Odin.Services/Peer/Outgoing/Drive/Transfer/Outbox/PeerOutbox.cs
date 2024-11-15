using System;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
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
        public async Task AddItemAsync(OutboxFileItem fileItem, bool useUpsert = false)
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
                await tenantSystemStorage.Outbox.UpsertAsync(record);
            }
            else
            {
                await tenantSystemStorage.Outbox.InsertAsync(record);
            }

            await mediator.Publish(new OutboxItemAddedNotification());
            PerformanceCounter.IncrementCounter($"Outbox Item Added {fileItem.Type}");
            
        }

        public async Task MarkCompleteAsync(Guid marker, IdentityDatabase db)
        {
            await tenantSystemStorage.Outbox.CompleteAndRemoveAsync(marker);
            
            PerformanceCounter.IncrementCounter("Outbox Mark Complete");
        }

        /// <summary>
        /// Add and item back the queue due to a failure
        /// </summary>
        public async Task MarkFailureAsync(Guid marker, UnixTimeUtc nextRun, IdentityDatabase db)
        {
            await tenantSystemStorage.Outbox.CheckInAsCancelledAsync(marker, nextRun);
            
            PerformanceCounter.IncrementCounter("Outbox Mark Failure");
        }

        public async Task<int> RecoverDeadAsync(UnixTimeUtc time, IdentityDatabase db)
        {
            var recovered = await tenantSystemStorage.Outbox.RecoverCheckedOutDeadItemsAsync(time);
            
            PerformanceCounter.IncrementCounter("Outbox Recover Dead");

            return recovered;
        }

        public async Task<OutboxFileItem> GetNextItemAsync(IdentityDatabase db)
        {
            var record = await tenantSystemStorage.Outbox.CheckOutItemAsync();
            
            if (null == record)
            {
                return null;
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

            return item;
        }

        public async Task<bool> HasOutboxFileItemAsync(OutboxFileItem fileItem, IdentityDatabase db)
        {
            var records = await tenantSystemStorage.Outbox.GetAsync(fileItem.File.DriveId, fileItem.File.FileId);
            var hasRecord = records?.Any(r => r.type == (int)OutboxItemType.File) ?? false;
            return hasRecord;
        }

        /// <summary>
        /// Gets the status of the specified Drive
        /// </summary>
        public async Task<OutboxDriveStatus> GetOutboxStatusAsync(Guid driveId, IdentityDatabase db)
        {
            var (totalCount, poppedCount, utc) = await tenantSystemStorage.Outbox.OutboxStatusDriveAsync(driveId);
            return new OutboxDriveStatus
            {
                CheckedOutCount = poppedCount,
                TotalItems = totalCount,
                NextItemRun = utc
            };
        }

        
        /// <summary>
        /// Get time until the next scheduled item should run (if any).
        /// </summary>
        public async Task<TimeSpan?> NextRunAsync(IdentityDatabase db)
        {
            var nextRun = await tenantSystemStorage.Outbox.NextScheduledItemAsync();
            if (nextRun == null)
            {
                return null;
            }
            
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(nextRun.Value.milliseconds);
            var now = DateTimeOffset.Now;
            if (dt < now)
            {
                return TimeSpan.Zero;
            }
            
            return dt - now;
        }
    }
}