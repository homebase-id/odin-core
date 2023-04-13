using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Schema;
using Dawn;
using Youverse.Core.Serialization;
using Youverse.Core.Storage;
using Youverse.Core.Storage.Sqlite.IdentityDatabase;

namespace Youverse.Core.Services.Transit.ReceivingHost.Incoming
{
    /// <summary>
    /// Manages items incoming to a DI that have not yet been processed (pre-inbox)
    /// </summary>
    public class TransitInboxBoxStorage
    {
        private readonly ITenantSystemStorage _tenantSystemStorage;

        public TransitInboxBoxStorage(ITenantSystemStorage tenantSystemStorage)
        {
            _tenantSystemStorage = tenantSystemStorage;
        }

        public Task Add(TransferInboxItem item)
        {
            item.AddedTimestamp = UnixTimeUtc.Now();

            var state = DotYouSystemSerializer.Serialize(item).ToUtf8ByteArray();
            _tenantSystemStorage.Inbox.Insert(new InboxRecord() { boxId = item.DriveId, fileId = item.FileId, priority = 1, value = state });

            return Task.CompletedTask;
        }

        public InboxStatus GetPendingCount(Guid driveId)
        {
            var p = _tenantSystemStorage.Inbox.PopStatusSpecificBox(driveId);
            return new InboxStatus()
            {
                TotalItems = p.totalCount,
                PoppedCount = p.poppedCount,
                OldestItemTimestamp = p.oldestItemTime,
            };
        }

        public async Task<List<TransferInboxItem>> GetPendingItems(Guid driveId, int batchSize)
        {
            //CRITICAL NOTE: we can only get back one item since we want to make sure the marker is for that one item in-case the operation fails
            var records = this._tenantSystemStorage.Inbox.PopSpecificBox(driveId, batchSize == 0 ? 1 : batchSize);
            
            if (null == records)
            {
                return new List<TransferInboxItem>();
            }

            var items = records.Select(r =>
            {
                var item = DotYouSystemSerializer.Deserialize<TransferInboxItem>(r.value.ToStringFromUtf8Bytes());

                item.Priority = (int)r.priority;
                item.AddedTimestamp = r.timeStamp;
                item.DriveId = r.boxId;
                item.FileId = r.fileId;
                item.Marker = r.popStamp.GetValueOrDefault();

                return item;
            }).ToList();

            return await Task.FromResult(items);
        }

        public Task MarkComplete(Guid driveId, Guid marker)
        {
            _tenantSystemStorage.Inbox.PopCommitAll(marker);
            return Task.CompletedTask;
        }

        public Task MarkFailure(Guid driveId, Guid marker)
        {
            _tenantSystemStorage.Inbox.PopCancelAll(marker);
            return Task.CompletedTask;
        }
    }
}