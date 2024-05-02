using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Services.Base;

namespace Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage
{
    /// <summary>
    /// Manages items incoming to a DI that have not yet been processed (pre-inbox)
    /// </summary>
    public class TransitInboxBoxStorage(TenantSystemStorage tenantSystemStorage)
    {
        public Task Add(TransferInboxItem item, DatabaseConnection cn)
        {
            item.AddedTimestamp = UnixTimeUtc.Now();

            var state = OdinSystemSerializer.Serialize(item).ToUtf8ByteArray();
            tenantSystemStorage.Inbox.Insert(cn, new InboxRecord() { boxId = item.DriveId, fileId = item.FileId, priority = 1, value = state });

            return Task.CompletedTask;
        }

        public async Task<InboxStatus> GetStatus(Guid driveId, DatabaseConnection cn)
        {
            var p = tenantSystemStorage.Inbox.PopStatusSpecificBox(cn, driveId);

            return await Task.FromResult(new InboxStatus()
            {
                TotalItems = p.totalCount,
                PoppedCount = p.poppedCount,
                OldestItemTimestamp = p.oldestItemTime,
            });
        }

        
        public InboxStatus GetPendingCount(Guid driveId, DatabaseConnection cn)
        {

            var p = tenantSystemStorage.Inbox.PopStatusSpecificBox(cn, driveId);

            return new InboxStatus()
            {
                TotalItems = p.totalCount,
                PoppedCount = p.poppedCount,
                OldestItemTimestamp = p.oldestItemTime,
            };
        }

        public async Task<List<TransferInboxItem>> GetPendingItems(Guid driveId, int batchSize, DatabaseConnection cn)
        {
            //CRITICAL NOTE: we can only get back one item since we want to make sure the marker is for that one item in-case the operation fails
            var records = tenantSystemStorage.Inbox.PopSpecificBox(cn, driveId, batchSize == 0 ? 1 : batchSize);

            if (null == records)
            {
                return new List<TransferInboxItem>();
            }

            var items = records.Select(r =>
            {
                var item = OdinSystemSerializer.Deserialize<TransferInboxItem>(r.value.ToStringFromUtf8Bytes());

                item.Priority = (int)r.priority;
                item.AddedTimestamp = r.timeStamp;
                item.DriveId = r.boxId;
                item.FileId = r.fileId;
                item.Marker = r.popStamp.GetValueOrDefault();

                return item;
            }).ToList();

            return await Task.FromResult(items);
        }

        public Task MarkComplete(Guid driveId, Guid marker, DatabaseConnection cn)
        {
            tenantSystemStorage.Inbox.PopCommitAll(cn, marker);
            return Task.CompletedTask;
        }

        public Task MarkFailure(Guid driveId, Guid marker, DatabaseConnection cn)
        {
            tenantSystemStorage.Inbox.PopCancelAll(cn, marker);
            return Task.CompletedTask;
        }

        public Task RecoverDead(UnixTimeUtc time, DatabaseConnection cn)
        {
            tenantSystemStorage.Inbox.PopRecoverDead(cn, time);
            return Task.CompletedTask;
        }
    }
}