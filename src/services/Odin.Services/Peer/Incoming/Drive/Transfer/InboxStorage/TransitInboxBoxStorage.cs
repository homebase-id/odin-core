using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Drives;

namespace Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage
{
    /// <summary>
    /// Manages items incoming to a DI that have not yet been processed (pre-inbox)
    /// </summary>
    public class TransitInboxBoxStorage(TableInbox tableInbox)
    {
        public async Task AddAsync(TransferInboxItem item)
        {
            item.AddedTimestamp = UnixTimeUtc.Now();

            var state = OdinSystemSerializer.Serialize(item).ToUtf8ByteArray();
            await tableInbox.InsertAsync(new InboxRecord() { boxId = item.DriveId, fileId = item.FileId, priority = 1, value = state });

            PerformanceCounter.IncrementCounter("Inbox Item Added");
        }

        public async Task<InboxStatus> GetStatusAsync(Guid driveId)
        {
            var p = await tableInbox.PopStatusSpecificBoxAsync(driveId);

            return new InboxStatus
            {
                TotalItems = p.totalCount,
                PoppedCount = p.poppedCount,
                OldestItemTimestamp = p.oldestItemTime,
            };
        }


        public async Task<InboxStatus> GetPendingCountAsync(Guid driveId)
        {
            var p = await tableInbox.PopStatusSpecificBoxAsync(driveId);
            return new InboxStatus()
            {
                TotalItems = p.totalCount,
                PoppedCount = p.poppedCount,
                OldestItemTimestamp = p.oldestItemTime,
            };
        }

        public async Task<List<TransferInboxItem>> GetPendingItemsAsync(Guid driveId, int batchSize)
        {
            //CRITICAL NOTE: we can only get back one item since we want to make sure the marker is for that one item in-case the operation fails
            var records = await tableInbox.PopSpecificBoxAsync(driveId, batchSize == 0 ? 1 : batchSize);

            if (null == records)
            {
                return new List<TransferInboxItem>();
            }

            PerformanceCounter.IncrementCounter("Inbox Item Checkout");

            var items = records.Select(r =>
            {
                var item = OdinSystemSerializer.Deserialize<TransferInboxItem>(r.value.ToStringFromUtf8Bytes());

                item.Priority = r.priority;
                item.AddedTimestamp = r.timeStamp;
                item.DriveId = r.boxId;
                item.FileId = r.fileId;
                item.Marker = r.popStamp.GetValueOrDefault();
                item.CorrelationId = r.correlationId;

                return item;
            }).ToList();

            return items;
        }

        public async Task<int> MarkCompleteAsync(InternalDriveFileId file, Guid marker)
        {
            int r = await tableInbox.PopCommitListAsync(marker, file.DriveId, [file.FileId]);

            // TODO TODD, you need to throw an exception here is r != 1.
            
            PerformanceCounter.IncrementCounter("Inbox Mark Complete");

            return r;
        }

        public async Task<int> MarkFailureAsync(InternalDriveFileId file, Guid marker)
        {
            int n = await tableInbox.PopCancelListAsync(marker, file.DriveId, [file.FileId]);
            PerformanceCounter.IncrementCounter("Inbox Mark Failure");
            return n;
        }

        public async Task<int> RecoverDeadAsync(UnixTimeUtc time)
        {
            var recovered = await tableInbox.PopRecoverDeadAsync(time);
            PerformanceCounter.IncrementCounter("Inbox Recover Dead");
            return recovered;
        }
    }
}