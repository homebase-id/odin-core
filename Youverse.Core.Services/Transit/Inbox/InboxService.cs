using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Transit.Inbox
{
    /// <summary>
    /// Services that manages items in a given Tenant's Inbox
    /// </summary>
    public class InboxService : DotYouServiceBase<IInboxService>, IInboxService
    {

        private const string InboxItemsCollection = "inbxitems";

        public InboxService(DotYouContext context, ILogger<IInboxService> logger, IHubContext<NotificationHub, INotificationHub> notificationHub, DotYouHttpClientFactory fac) : base(context, logger, notificationHub, fac)
        {
        }

        /// <summary>
        /// Adds an item to be encrypted and moved to the Inbox
        /// </summary>
        /// <param name="item"></param>
        public Task Add(InboxItem item)
        {
            item.AddedTimestamp = DateTimeExtensions.UnixTimeMilliseconds();
            WithTenantSystemStorage<InboxItem>(InboxItemsCollection, s => s.Save(item));
            return Task.CompletedTask;
        }

        public Task Add(IEnumerable<InboxItem> items)
        {
            foreach (var item in items)
            {
                this.Add(item);
            }

            return Task.CompletedTask;
        }


        public Task<PagedResult<InboxItem>> GetNextBatch()
        {
            throw new NotImplementedException();
            //return Array.Empty<InboxItem>();
        }

        /// <summary>
        /// Gets all the items currently in the queue w/o making changes to it 
        /// </summary>
        /// <returns></returns>
        public async Task<PagedResult<InboxItem>> GetPendingItems(PageOptions pageOptions)
        {
            return await WithTenantSystemStorageReturnList<InboxItem>(InboxItemsCollection, s => s.GetList(pageOptions));
        }

        public async Task Remove(DotYouIdentity recipient, Guid fileId)
        {
            //TODO: need to make a better queue here
            Expression<Func<InboxItem, bool>> predicate = item => item.Sender == recipient && item.FileId == fileId;
            var item = await WithTenantSystemStorageReturnSingle<InboxItem>(InboxItemsCollection, s => s.FindOne(predicate));
            WithTenantSystemStorage<InboxItem>(InboxItemsCollection, s => s.Delete(item.Id));
        }

        public async Task<InboxItem> GetItem(Guid id)
        {
            var item = await WithTenantSystemStorageReturnSingle<InboxItem>(InboxItemsCollection, s => s.Get(id));
            return item;
        }

        public Task RemoveItem(Guid id)
        {
            WithTenantSystemStorage<InboxItem>(InboxItemsCollection, s => s.Delete(id));
            return Task.CompletedTask;
        }
    }
}