using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Services.Base;

namespace Odin.Core.Services.Transit.SendingHost
{
    public class TransferKeyEncryptionQueueService
    {
        private readonly GuidId _queueKey = GuidId.FromString("tkequeue__");
        private readonly TenantSystemStorage _tenantSystemStorage;

        public TransferKeyEncryptionQueueService(TenantSystemStorage tenantSystemStorage)
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