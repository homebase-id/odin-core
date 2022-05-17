using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Notifications;

namespace Youverse.Core.Services.Transit.Outbox
{
    /// <summary>
    /// Services that manages items in a given Tenant's outbox
    /// </summary>
    public class OutboxService : IOutboxService
    {
        private readonly IPendingTransfersService _pendingTransfers;
        private readonly ISystemStorage _systemStorage;
        private readonly DotYouContextAccessor _contextAccessorAccessor;
        private readonly TenantContext _tenantContext;
        private const string OutboxItemsCollection = "obxitems";

        public OutboxService(DotYouContextAccessor contextAccessor, ILogger<IOutboxService> logger, IPendingTransfersService pendingTransfers, ISystemStorage systemStorage, TenantContext tenantContext)
        {
            _contextAccessorAccessor = contextAccessor;
            _pendingTransfers = pendingTransfers;
            _systemStorage = systemStorage;
            _tenantContext = tenantContext;
        }
        
        /// <summary>
        /// Adds an item to be encrypted and moved to the outbox
        /// </summary>
        /// <param name="item"></param>
        public Task Add(OutboxItem item)
        {
            item.IsCheckedOut = false;
            _systemStorage.WithTenantSystemStorage<OutboxItem>(OutboxItemsCollection, s => s.Save(item));
            _pendingTransfers.EnsureSenderIsPending(_tenantContext.HostDotYouId);
            return Task.CompletedTask;
        }

        public Task Add(IEnumerable<OutboxItem> items)
        {
            foreach (var item in items)
            {
                this.Add(item);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Add and item back the queue due to a failure
        /// </summary>
        public async Task MarkFailure(Guid itemId, TransferFailureReason reason)
        {
            var item = await _systemStorage.WithTenantSystemStorageReturnSingle<OutboxItem>(OutboxItemsCollection, s => s.Get(itemId));

            if (null == item)
            {
                return;
            }
            
            
            //TODO: check all other fields on the item;
            item.IsCheckedOut = false;
            item.Attempts.Add(new TransferAttempt()
            {
                TransferFailureReason = reason,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            //TODO:this puts it at the end of the queue however we need to decide if we want to push it forward for various reasons (i.e. it's a chat message, etc.)
            _systemStorage.WithTenantSystemStorage<OutboxItem>(OutboxItemsCollection,s=>s.Save(item));
        }

        public async Task<PagedResult<OutboxItem>> GetNextBatch()
        {
            //TODO: update logic to handle things like priority and other bits
            var pageOptions = new PageOptions(1, 10);
            var pagedResult = await _systemStorage.WithTenantSystemStorageReturnList<OutboxItem>(OutboxItemsCollection, s => s.Find(item => !item.IsCheckedOut, ListSortDirection.Ascending, key => key.AddedTimestamp, pageOptions));

            //check out the items
            foreach (var item in pagedResult.Results)
            {
                item.IsCheckedOut = true;
                _systemStorage.WithTenantSystemStorage<OutboxItem>(OutboxItemsCollection, s => s.Save(item));
            }

            return pagedResult;
        }

        /// <summary>
        /// Gets all the items currently in the queue w/o making changes to it 
        /// </summary>
        /// <returns></returns>
        public async Task<PagedResult<OutboxItem>> GetPendingItems(PageOptions pageOptions)
        {
            return await _systemStorage.WithTenantSystemStorageReturnList<OutboxItem>(OutboxItemsCollection, s => s.GetList(pageOptions, ListSortDirection.Ascending, key => key.AddedTimestamp));
        }

        public async Task Remove(DotYouIdentity recipient, InternalDriveFileId file)
        {
            //TODO: need to make a better queue here
            Expression<Func<OutboxItem, bool>> predicate = outboxItem => outboxItem.Recipient == recipient && outboxItem.File == file;
            var item = await _systemStorage.WithTenantSystemStorageReturnSingle<OutboxItem>(OutboxItemsCollection, s => s.FindOne(predicate));
            _systemStorage.WithTenantSystemStorage<OutboxItem>(OutboxItemsCollection, s => s.Delete(item.Id));
        }
        
        public Task Remove(Guid id)
        {
            _systemStorage.WithTenantSystemStorage<OutboxItem>(OutboxItemsCollection, s => s.Delete(id));
            return Task.CompletedTask;
        }

        public async Task<OutboxItem> GetItem(Guid id)
        {
            var item = await _systemStorage.WithTenantSystemStorageReturnSingle<OutboxItem>(OutboxItemsCollection, s => s.Get(id));
            return item;
        }

        public Task RemoveItem(Guid id)
        {
            _systemStorage.WithTenantSystemStorage<OutboxItem>(OutboxItemsCollection, s => s.Delete(id));
            return Task.CompletedTask;
        }

        public async Task UpdatePriority(Guid id, int priority)
        {
            var item = await this.GetItem(id);
            item.Priority = priority;
            _systemStorage.WithTenantSystemStorage<OutboxItem>(OutboxItemsCollection, s => s.Save(item));
        }
    }
}