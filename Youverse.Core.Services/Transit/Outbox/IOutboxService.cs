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

        Task MarkComplete(byte[] marker);

        /// <summary>
        /// Add and item back the queue due to a failure
        /// </summary>
        Task MarkFailure(byte[] marker, TransferFailureReason reason);

        Task<List<OutboxItem>> GetBatchForProcessing(Guid driveId, int batchSize);

        /// <summary>
        /// Removes the outbox item for the given recipient and file
        /// </summary>
        /// <returns></returns>
        Task Remove(DotYouIdentity recipient, InternalDriveFileId file);

    }
}