using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage
{
    /// <summary>
    /// Manages items incoming to a DI that have not yet been processed (pre-inbox)
    /// </summary>
    public class TransitInboxBoxStorage(TenantSystemStorage tenantSystemStorage)
    {
        public async Task AddAsync(TransferInboxItem item)
        {
            item.AddedTimestamp = UnixTimeUtc.Now();

            var state = OdinSystemSerializer.Serialize(item).ToUtf8ByteArray();
            await tenantSystemStorage.Inbox.InsertAsync(new InboxRecord() { boxId = item.DriveId, fileId = item.FileId, priority = 1, value = state });

            PerformanceCounter.IncrementCounter("Inbox Item Added");
        }

        public async Task<InboxStatus> GetStatusAsync(Guid driveId)
        {
            var p = await tenantSystemStorage.Inbox.PopStatusSpecificBoxAsync(driveId);

            return new InboxStatus
            {
                TotalItems = p.totalCount,
                PoppedCount = p.poppedCount,
                OldestItemTimestamp = p.oldestItemTime,
            };
        }


        public async Task<InboxStatus> GetPendingCountAsync(Guid driveId)
        {
            var p = await tenantSystemStorage.Inbox.PopStatusSpecificBoxAsync(driveId);
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
            var records = await tenantSystemStorage.Inbox.PopSpecificBoxAsync(driveId, batchSize == 0 ? 1 : batchSize);

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

                return item;
            }).ToList();

            return items;
        }

        public async Task MarkCompleteAsync(InternalDriveFileId file, Guid marker)
        {
            await tenantSystemStorage.Inbox.PopCommitListAsync(marker, file.DriveId, [file.FileId]);
            
            PerformanceCounter.IncrementCounter("Inbox Mark Complete");
        }

        public async Task MarkFailureAsync(InternalDriveFileId file, Guid marker)
        {
            await tenantSystemStorage.Inbox.PopCancelListAsync(marker, file.DriveId, [file.FileId]);
            PerformanceCounter.IncrementCounter("Inbox Mark Failure");
        }

        public async Task<int> RecoverDeadAsync(UnixTimeUtc time)
        {
            var recovered = await tenantSystemStorage.Inbox.PopRecoverDeadAsync(time);
            PerformanceCounter.IncrementCounter("Inbox Recover Dead");
            return recovered;
        }
    }
}