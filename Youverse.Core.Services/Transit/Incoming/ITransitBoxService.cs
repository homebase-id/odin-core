using System;
using System.Threading.Tasks;


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
        Task<PagedResult<TransferBoxItem>> GetPendingItems(Guid driveId, PageOptions pageOptions);

        Task<TransferBoxItem> GetItem(Guid driveId, Guid id);

        /// <summary>
        /// Removes an item from the Inbox.
        /// </summary>
        Task Remove(Guid driveId, Guid id);
    }
}