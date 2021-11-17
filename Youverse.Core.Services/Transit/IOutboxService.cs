using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Transit
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
        Task Add(OutboxItem item, TransferFailureReason reason);

        Task<PagedResult<OutboxItem>> GetNextBatch();

        /// <summary>
        /// Gets a list of all items
        /// </summary>
        /// <returns></returns>
        Task<PagedResult<OutboxItem>> GetPendingItems(PageOptions pageOptions);

        /// <summary>
        /// Removes the outbox item for the given recipient and file
        /// </summary>
        /// <param name="recipient"></param>
        /// <param name="fileId"></param>
        /// <returns></returns>
        Task Remove(DotYouIdentity recipient, Guid fileId);
    }
}