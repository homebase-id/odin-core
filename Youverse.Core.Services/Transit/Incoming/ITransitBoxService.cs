using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Transit.Incoming
{
    /// <summary>
    /// Items in the Inbox for a given tenant
    /// </summary>
    public interface ITransitBoxService
    {
        /// <summary>
        /// Adds an item to be encrypted and moved to the Inbox
        /// </summary>
        /// <param name="item"></param>
        Task Add(TransferBoxItem item);
        
        /// <summary>
        /// Gets a list of all items
        /// </summary>
        /// <returns></returns>
        Task<PagedResult<TransferBoxItem>> GetPendingItems(PageOptions pageOptions);

        /// <summary>
        /// Removes the Inbox item for the given recipient and file
        /// </summary>
        Task Remove(DotYouIdentity recipient, DriveFileId file);

        Task<TransferBoxItem> GetItem(Guid id);

        /// <summary>
        /// Removes an item from the Inbox.
        /// </summary>
        Task RemoveItem(Guid id);
    }
}