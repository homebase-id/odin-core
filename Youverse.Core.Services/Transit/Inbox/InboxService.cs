using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Notifications;

namespace Youverse.Core.Services.Transit.Inbox
{
    /// <summary>
    /// Services that manages items in a given Tenant's Inbox
    /// </summary>
    public class InboxService : IInboxService
    {
        private const string InboxItemsCollection = "inbxitems";

        private readonly ISystemStorage _systemStorage;

        public InboxService(DotYouContext context, ILogger<IInboxService> logger, NotificationHandler notificationHub, IDotYouHttpClientFactory dotYouHttpClientFactory, ISystemStorage systemStorage)
        {
            _systemStorage = systemStorage;
        }

        /// <summary>
        /// Adds an item to be encrypted and moved to the Inbox
        /// </summary>
        /// <param name="item"></param>
        public Task Add(InboxItem item)
        {
            item.AddedTimestamp = DateTimeExtensions.UnixTimeMilliseconds();
            _systemStorage.WithTenantSystemStorage<InboxItem>(InboxItemsCollection, s => s.Save(item));
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
            return await _systemStorage.WithTenantSystemStorageReturnList<InboxItem>(InboxItemsCollection, s => s.GetList(pageOptions));
        }

        public async Task Remove(DotYouIdentity recipient, DriveFileId file)
        {
            //TODO: need to make a better queue here
            Expression<Func<InboxItem, bool>> predicate = item => item.Sender == recipient && item.File == file;
            var item = await _systemStorage.WithTenantSystemStorageReturnSingle<InboxItem>(InboxItemsCollection, s => s.FindOne(predicate));
            _systemStorage.WithTenantSystemStorage<InboxItem>(InboxItemsCollection, s => s.Delete(item.Id));
        }

        public async Task<InboxItem> GetItem(Guid id)
        {
            var item = await _systemStorage.WithTenantSystemStorageReturnSingle<InboxItem>(InboxItemsCollection, s => s.Get(id));
            return item;
        }

        public Task RemoveItem(Guid id)
        {
            _systemStorage.WithTenantSystemStorage<InboxItem>(InboxItemsCollection, s => s.Delete(id));
            return Task.CompletedTask;
        }
    }
}