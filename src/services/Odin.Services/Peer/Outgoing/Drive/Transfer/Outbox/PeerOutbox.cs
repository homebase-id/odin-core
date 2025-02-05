using System;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Table;
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
    public class PeerOutbox(IMediator mediator, TableOutbox tblOutbox)
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
                await tblOutbox.UpsertAsync(record);
            }
            else
            {
                await tblOutbox.InsertAsync(record);
            }

            await mediator.Publish(new OutboxItemAddedNotification());
            PerformanceCounter.IncrementCounter($"Outbox Item Added {fileItem.Type}");
        }

        public async Task MarkCompleteAsync(Guid marker)
        {
            await tblOutbox.CompleteAndRemoveAsync(marker);

            PerformanceCounter.IncrementCounter("Outbox Mark Complete");
        }

        /// <summary>
        /// Add and item back the queue due to a failure
        /// </summary>
        public async Task MarkFailureAsync(Guid marker, UnixTimeUtc nextRun)
        {
            await tblOutbox.CheckInAsCancelledAsync(marker, nextRun);

            PerformanceCounter.IncrementCounter("Outbox Mark Failure");
        }

        public async Task<int> RecoverDeadAsync(UnixTimeUtc time)
        {
            var recovered = await tblOutbox.RecoverCheckedOutDeadItemsAsync(time);

            PerformanceCounter.IncrementCounter("Outbox Recover Dead");

            return recovered;
        }

        public async Task<RedactedOutboxFileItem> GetItemAsync(Guid driveId, Guid fileId, OdinId recipient)
        {
            var record = await tblOutbox.GetAsync(driveId, fileId, recipient);
            return OutboxRecordToFileItem(record)?.Redacted();
        }

        public async Task<OutboxFileItem> GetNextItemAsync()
        {
            var record = await tblOutbox.CheckOutItemAsync();
            var item = OutboxRecordToFileItem(record);
            if (null != item)
            {
                PerformanceCounter.IncrementCounter("Outbox Item Checkout");
            }

            return item;
        }

        public async Task<bool> HasOutboxFileItemAsync(OutboxFileItem fileItem)
        {
            var records = await tblOutbox.GetAsync(fileItem.File.DriveId, fileItem.File.FileId);
            var hasRecord = records?.Any(r => r.type == (int)OutboxItemType.File) ?? false;
            return hasRecord;
        }

        /// <summary>
        /// Gets the status of the specified Drive
        /// </summary>
        public async Task<OutboxDriveStatus> GetOutboxStatusAsync(Guid driveId)
        {
            var (totalCount, poppedCount, utc) = await tblOutbox.OutboxStatusDriveAsync(driveId);
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
        public async Task<TimeSpan?> NextRunAsync()
        {
            var nextRun = await tblOutbox.NextScheduledItemAsync();
            if (nextRun == null)
            {
                return null;
            }

            UnixTimeUtc dt = nextRun.Value;
            var now = UnixTimeUtc.Now();
            if (dt < now)
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromMilliseconds(dt.milliseconds - now.milliseconds);
        }

        private static OutboxFileItem OutboxRecordToFileItem(OutboxRecord record)
        {
            if (null == record)
            {
                return null;
            }

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
    }
}