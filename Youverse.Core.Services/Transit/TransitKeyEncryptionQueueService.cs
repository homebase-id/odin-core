using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Transit
{
    public class TransitKeyEncryptionQueueService : DotYouServiceBase,  ITransitKeyEncryptionQueueService
    {
        private const string CollectionName = "tkeqs";
        public TransitKeyEncryptionQueueService(DotYouContext context, ILogger logger) : base(context, logger, null, null)
        {
            
        }
        
        public Task Enqueue(TransitKeyEncryptionQueueItem item)
        {
            this.WithTenantSystemStorage<TransitKeyEncryptionQueueItem>(CollectionName, s => s.Save(item));
            return Task.CompletedTask;
        }

        public async Task<IEnumerable<TransitKeyEncryptionQueueItem>> GetNext(PageOptions pageOptions)
        {
            var list = await this.WithTenantSystemStorageReturnList<TransitKeyEncryptionQueueItem>(CollectionName, 
                s => s.GetList(pageOptions, ListSortDirection.Descending, key=>key.FirstAddedTimestampMs));
            return list.Results;
        }
    }
}