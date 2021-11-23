using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Transit.Outbox
{
    /// <summary>
    /// Services that manages items in a given Tenant's outbox
    /// </summary>
    public class OutboxService : DotYouServiceBase, IOutboxService
    {
        private readonly IPendingTransfersService _pendingTransfers;

        private const string OutboxItemsCollection = "obxitems";

        public OutboxService(DotYouContext context, ILogger logger, IPendingTransfersService pendingTransfers, IHubContext<NotificationHub, INotificationHub> notificationHub, DotYouHttpClientFactory fac) : base(context, logger, notificationHub, fac)
        {
            _pendingTransfers = pendingTransfers;
        }

        /// <summary>
        /// Adds an item to be encrypted and moved to the outbox
        /// </summary>
        /// <param name="item"></param>
        public Task Add(OutboxItem item)
        {
            item.IsCheckedOut = false;
            WithTenantSystemStorage<OutboxItem>(OutboxItemsCollection, s => s.Save(item));
            _pendingTransfers.EnsureSenderIsPending(this.Context.HostDotYouId);
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
            var item = await WithTenantSystemStorageReturnSingle<OutboxItem>(OutboxItemsCollection, s => s.Get(itemId));

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
            WithTenantSystemStorage<OutboxItem>(OutboxItemsCollection,s=>s.Save(item));
        }

        public async Task<PagedResult<OutboxItem>> GetNextBatch()
        {
            //TODO: update logic to handle things like priority and other bits
            var pageOptions = new PageOptions(1, 10);
            var pagedResult = await WithTenantSystemStorageReturnList<OutboxItem>(OutboxItemsCollection, s => s.Find(item => !item.IsCheckedOut, ListSortDirection.Ascending, key => key.AddedTimestamp, pageOptions));

            //check out the items
            foreach (var item in pagedResult.Results)
            {
                item.IsCheckedOut = true;
                WithTenantSystemStorage<OutboxItem>(OutboxItemsCollection, s => s.Save(item));
            }

            return pagedResult;
        }

        /// <summary>
        /// Gets all the items currently in the queue w/o making changes to it 
        /// </summary>
        /// <returns></returns>
        public async Task<PagedResult<OutboxItem>> GetPendingItems(PageOptions pageOptions)
        {
            return await WithTenantSystemStorageReturnList<OutboxItem>(OutboxItemsCollection, s => s.GetList(pageOptions, ListSortDirection.Ascending, key => key.AddedTimestamp));
        }

        public async Task Remove(DotYouIdentity recipient, Guid fileId)
        {
            //TODO: need to make a better queue here
            Expression<Func<OutboxItem, bool>> predicate = outboxItem => outboxItem.Recipient == recipient && outboxItem.FileId == fileId;
            var item = await WithTenantSystemStorageReturnSingle<OutboxItem>(OutboxItemsCollection, s => s.FindOne(predicate));
            WithTenantSystemStorage<OutboxItem>(OutboxItemsCollection, s => s.Delete(item.Id));
        }

        public async Task<OutboxItem> GetItem(Guid id)
        {
            var item = await WithTenantSystemStorageReturnSingle<OutboxItem>(OutboxItemsCollection, s => s.Get(id));
            return item;
        }

        public Task RemoveItem(Guid id)
        {
            WithTenantSystemStorage<OutboxItem>(OutboxItemsCollection, s => s.Delete(id));
            return Task.CompletedTask;
        }

        public async Task UpdatePriority(Guid id, int priority)
        {
            var item = await this.GetItem(id);
            item.Priority = priority;
            WithTenantSystemStorage<OutboxItem>(OutboxItemsCollection, s => s.Save(item));
        }
    }
}