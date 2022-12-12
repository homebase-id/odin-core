using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Transit
{
    public class TransferKeyEncryptionQueueService : ITransferKeyEncryptionQueueService
    {
        private readonly GuidId _queueKey = GuidId.FromString("tkequeue__");
        private readonly ITenantSystemStorage _tenantSystemStorage;

        public TransferKeyEncryptionQueueService(ITenantSystemStorage tenantSystemStorage)
        {
            _tenantSystemStorage = tenantSystemStorage;
        }

        public Task Enqueue(TransitKeyEncryptionQueueItem item)
        {
            var items = _tenantSystemStorage.SingleKeyValueStorage.Get<List<TransitKeyEncryptionQueueItem>>(_queueKey);

            items.Add(item);
            this._tenantSystemStorage.SingleKeyValueStorage.Upsert(_queueKey, items);

            return Task.CompletedTask;
        }

        public Task<IEnumerable<TransitKeyEncryptionQueueItem>> GetNext()
        {
            var items = _tenantSystemStorage.SingleKeyValueStorage.Get<List<TransitKeyEncryptionQueueItem>>(_queueKey);
            return Task.FromResult<IEnumerable<TransitKeyEncryptionQueueItem>>(items);
        }
    }
}