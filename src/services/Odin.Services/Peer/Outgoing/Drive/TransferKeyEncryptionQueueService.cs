using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Services.Base;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Services.Peer.Outgoing.Drive
{
    public class TransferKeyEncryptionQueueService
    {
        private readonly TenantSystemStorage _tenantSystemStorage;
        private readonly GuidId _queueStorageId = Guid.Parse("0bc60d9a-1f43-4724-84fa-3ba9508c84fc");
        private readonly byte[] _queueDataType = Guid.Parse("3ba60266-663b-47ac-8931-1ffc15d62720").ToByteArray();

        private readonly TwoKeyValueStorage _queueStorage;

        public TransferKeyEncryptionQueueService(TenantSystemStorage tenantSystemStorage)
        {
            _tenantSystemStorage = tenantSystemStorage;
            const string queueContextKey = "21a24e2d-f2c6-4d7a-9cfa-44d0d956d0d7";
            _queueStorage = tenantSystemStorage.CreateTwoKeyValueStorage(Guid.Parse(queueContextKey));
        }

        public Task Enqueue(PeerKeyEncryptionQueueItem item, DatabaseConnection cn)
        {
            _queueStorage.Upsert(cn, item.Id, _queueDataType, item);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<PeerKeyEncryptionQueueItem>> GetNext(DatabaseConnection cn)
        {
            var items = _queueStorage.Get<List<PeerKeyEncryptionQueueItem>>(cn, _queueStorageId);
            return Task.FromResult<IEnumerable<PeerKeyEncryptionQueueItem>>(items);
        }
    }
}