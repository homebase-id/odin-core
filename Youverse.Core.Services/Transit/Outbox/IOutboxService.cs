using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Transit.Outbox
{
    /// <summary>
    /// Items in the outbox for a given tenant
    /// </summary>
    public interface IOutboxService
    {
        /// <summary>
        /// Adds an item to be encrypted and moved to the outbox
        /// </summary>
        /// <param name="item"></param>
        Task Add(OutboxItem item);
        
        Task Add(IEnumerable<OutboxItem> items);

        /// <summary>
        /// Add and item back the queue due to a failure
        /// </summary>
        Task MarkFailure(Guid itemId, TransferFailureReason reason);

        Task<PagedResult<OutboxItem>> GetNextBatch();

        /// <summary>
        /// Gets a list of all items
        /// </summary>
        /// <returns></returns>
        Task<PagedResult<OutboxItem>> GetPendingItems(PageOptions pageOptions);

        /// <summary>
        /// Removes the outbox item for the given recipient and file
        /// </summary>
        /// <returns></returns>
        Task Remove(DotYouIdentity recipient, InternalDriveFileId file);
        

        /// <summary>
        /// Removes the outbox item for the give id
        /// </summary>
        Task Remove(Guid id);


        Task<OutboxItem> GetItem(Guid id);
        

        /// <summary>
        /// Removes an item from the outbox.  This does not notify the <see cref="PendingTransfersService"/>.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        Task RemoveItem(Guid id);

        /// <summary>
        /// Updates the priority of a given <see cref="OutboxItem"/>
        /// </summary>
        /// <param name="id"></param>
        /// <param name="priority"></param>
        /// <returns></returns>
        Task UpdatePriority(Guid id, int priority);
    }
}