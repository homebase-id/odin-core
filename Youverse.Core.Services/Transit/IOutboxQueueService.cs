using System.Collections;
using System.Collections.Generic;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    /// Items in the outbox for a given tenant
    /// </summary>
    public interface IOutboxQueueService
    {
        /// <summary>
        /// Adds an item to be encrypted and moved to the outbox
        /// </summary>
        /// <param name="item"></param>
        void Enqueue(OutboxQueueItem item);
        
        void Enqueue(IEnumerable<OutboxQueueItem> items);

        /// <summary>
        /// Add and item back the queue due to a failure
        /// </summary>
        void EnqueueFailure(OutboxQueueItem item, TransferFailureReason reason);

        IEnumerable<OutboxQueueItem> GetNextBatch();

        /// <summary>
        /// Gets all the items currently in the queue w/o making changes to it 
        /// </summary>
        /// <returns></returns>
        IEnumerable<OutboxQueueItem> GetPendingItems();
    }
}