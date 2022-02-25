using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Transit
{
    public class TransferKeyEncryptionQueueService : ITransferKeyEncryptionQueueService
    {
        private const string CollectionName = "tkeqs";
        private readonly ISystemStorage _systemStorage;
        public TransferKeyEncryptionQueueService(DotYouContextAccessor contextAccessor, ILogger<ITransferKeyEncryptionQueueService> logger, ISystemStorage systemStorage)
        {
            _systemStorage = systemStorage;
        }
        
        public Task Enqueue(TransitKeyEncryptionQueueItem item)
        {
            this._systemStorage.WithTenantSystemStorage<TransitKeyEncryptionQueueItem>(CollectionName, s => s.Save(item));
            return Task.CompletedTask;
        }

        public async Task<IEnumerable<TransitKeyEncryptionQueueItem>> GetNext(PageOptions pageOptions)
        {
            var list = await this._systemStorage.WithTenantSystemStorageReturnList<TransitKeyEncryptionQueueItem>(CollectionName, 
                s => s.GetList(pageOptions, ListSortDirection.Descending, key=>key.FirstAddedTimestampMs));
            return list.Results;
        }
    }
}