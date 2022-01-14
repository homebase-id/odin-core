using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Transit.Incoming
{
    /// <summary>
    /// Items in the Inbox for a given tenant
    /// </summary>
    public interface ITransferBoxService
    {
        /// <summary>
        /// Adds an item to be encrypted and moved to the Inbox
        /// </summary>
        /// <param name="item"></param>
        Task Add(TransferBoxItem item);
        
        Task Add(IEnumerable<TransferBoxItem> items);

        /// <summary>
        /// Processes incoming transfers by converting their transfer keys and moving files to long term storage
        /// </summary>
        /// <returns></returns>
        Task ProcessTransfers();
        
        Task<PagedResult<TransferBoxItem>> GetNextBatch();

        /// <summary>
        /// Gets a list of all items
        /// </summary>
        /// <returns></returns>
        Task<PagedResult<TransferBoxItem>> GetPendingItems(PageOptions pageOptions);

        /// <summary>
        /// Removes the Inbox item for the given recipient and file
        /// </summary>
        /// <param name="recipient"></param>
        /// <param name="fileId"></param>
        /// <returns></returns>
        Task Remove(DotYouIdentity recipient, DriveFileId file);

        Task<TransferBoxItem> GetItem(Guid id);

        /// <summary>
        /// Removes an item from the Inbox.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        Task RemoveItem(Guid id);
    }
}