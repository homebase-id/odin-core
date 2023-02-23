using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core.Serialization;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Transit.Incoming
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

        public Task Add(TransferBoxItem item)
        {
            item.AddedTimestamp = UnixTimeUtc.Now();

            var state = DotYouSystemSerializer.Serialize(item).ToUtf8ByteArray();
            _tenantSystemStorage.Inbox.InsertRow(item.DriveId.ToByteArray(), item.FileId.ToByteArray(), 1, state);
            
            return Task.CompletedTask;
        }

        public async Task<List<TransferBoxItem>> GetPendingItems(Guid driveId)
        {
            //CRITICAL NOTE: we can only get back one item since we want to make sure the marker is for that one item in-case the operation fails
            var records = this._tenantSystemStorage.Inbox.Pop(driveId.ToByteArray(), 1, out var marker);

            var record = records.SingleOrDefault();
            if (null == record)
            {
                return new List<TransferBoxItem>();
            }

            var items = records.Select(r =>
            {
                var item = DotYouSystemSerializer.Deserialize<TransferBoxItem>(r.value.ToStringFromUtf8Bytes());

                item.Priority = (int)r.priority;
                item.AddedTimestamp = r.timeStamp;
                item.DriveId = new Guid(r.boxId);
                item.FileId = new Guid(r.fileId);

                return item;
            }).ToList();

            return items;
        }

        public Task MarkComplete(Guid driveId, byte[] marker)
        {
            _tenantSystemStorage.Inbox.PopCommit(marker);
            return Task.CompletedTask;
        }

        public Task MarkFailure(Guid driveId, byte[] marker)
        {
            _tenantSystemStorage.Inbox.PopCancel(marker);
            return Task.CompletedTask;
        }
    }
}