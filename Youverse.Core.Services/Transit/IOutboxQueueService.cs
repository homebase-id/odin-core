using System.Collections.Generic;

namespace Youverse.Core.Services.Transit
{
    public interface IOutboxQueueService
    {
        /// <summary>
        /// Adds an item to be encrypted and moved to the outbox
        /// </summary>
        /// <param name="item"></param>
        void Enqueue(TransferQueueItem item);

        /// <summary>
        /// Add and item back the queue due to a failure
        /// </summary>
        void EnqueueFailure(TransferQueueItem item, TransferFailureReason reason);

        IEnumerable<TransferQueueItem> GetNextBatch();

        /// <summary>
        /// Gets all the items currently in the queue w/o making changes to it 
        /// </summary>
        /// <returns></returns>
        IEnumerable<TransferQueueItem> GetPendingItems();
    }
}