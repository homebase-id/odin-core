using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Transit.Incoming
{
    public interface IInboxService
    {
        /// <summary>
        /// Adds an item to be encrypted and moved to the Inbox
        /// </summary>
        /// <param name="item"></param>
        Task Add(InboxItem item);

        /// <summary>
        /// Gets a list of all items
        /// </summary>
        /// <returns></returns>
        Task<PagedResult<InboxItem>> GetPendingItems(PageOptions pageOptions);

        /// <summary>
        /// Removes the Inbox item for the given recipient and file
        /// </summary>
        Task Remove(DotYouIdentity recipient, DriveFileId file);

        Task<InboxItem> GetItem(Guid id);

        /// <summary>
        /// Removes an item from the Inbox.
        /// </summary>
        Task RemoveItem(Guid id);
    }
}