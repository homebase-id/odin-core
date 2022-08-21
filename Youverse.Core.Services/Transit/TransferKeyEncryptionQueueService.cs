using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.SystemStorage;

namespace Youverse.Core.Services.Transit
{
    public class TransferKeyEncryptionQueueService : ITransferKeyEncryptionQueueService
    {
        private readonly ByteArrayId _queueKey = ByteArrayId.FromString("tkequeue__");
        private readonly ISystemStorage _systemStorage;

        public TransferKeyEncryptionQueueService(ISystemStorage systemStorage)
        {
            _systemStorage = systemStorage;
        }

        public Task Enqueue(TransitKeyEncryptionQueueItem item)
        {
            var items = _systemStorage.SingleKeyValueStorage.Get<List<TransitKeyEncryptionQueueItem>>(_queueKey);

            items.Add(item);
            this._systemStorage.SingleKeyValueStorage.Upsert(_queueKey, items);

            return Task.CompletedTask;
        }

        public Task<IEnumerable<TransitKeyEncryptionQueueItem>> GetNext()
        {
            var items = _systemStorage.SingleKeyValueStorage.Get<List<TransitKeyEncryptionQueueItem>>(_queueKey);
            return Task.FromResult<IEnumerable<TransitKeyEncryptionQueueItem>>(items);
        }
    }
}