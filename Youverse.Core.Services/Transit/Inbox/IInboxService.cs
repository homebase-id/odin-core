using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Transit.Inbox
{
    /// <summary>
    /// Items in the Inbox for a given tenant
    /// </summary>
    public interface IInboxService
    {
        /// <summary>
        /// Adds an item to be encrypted and moved to the Inbox
        /// </summary>
        /// <param name="item"></param>
        Task Add(InboxItem item);
        
        Task Add(IEnumerable<InboxItem> items);


        Task<PagedResult<InboxItem>> GetNextBatch();

        /// <summary>
        /// Gets a list of all items
        /// </summary>
        /// <returns></returns>
        Task<PagedResult<InboxItem>> GetPendingItems(PageOptions pageOptions);

        /// <summary>
        /// Removes the Inbox item for the given recipient and file
        /// </summary>
        /// <param name="recipient"></param>
        /// <param name="fileId"></param>
        /// <returns></returns>
        Task Remove(DotYouIdentity recipient, Guid fileId);

        Task<InboxItem> GetItem(Guid id);

        /// <summary>
        /// Removes an item from the Inbox.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        Task RemoveItem(Guid id);
    }
}