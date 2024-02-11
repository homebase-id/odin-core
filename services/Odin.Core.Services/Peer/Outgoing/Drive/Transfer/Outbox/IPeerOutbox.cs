using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Services.Drives;

namespace Odin.Core.Services.Peer.Outgoing.Drive.Transfer.Outbox
{
    /// <summary>
    /// Items in the outbox for a given tenant
    /// </summary>
    public interface IPeerOutbox
    {
        /// <summary>
        /// Adds an item to be encrypted and moved to the outbox
        /// </summary>
        /// <param name="item"></param>
        Task Add(TransitOutboxItem item);

        Task Add(IEnumerable<TransitOutboxItem> items);

        Task MarkComplete(Guid marker);

        /// <summary>
        /// Add and item back the queue due to a failure
        /// </summary>
        Task MarkFailure(Guid marker, TransferFailureReason reason);

        Task<List<TransitOutboxItem>> GetBatchForProcessing(Guid driveId, int batchSize);

        /// <summary>
        /// Removes the outbox item for the given recipient and file
        /// </summary>
        /// <returns></returns>
        Task Remove(OdinId recipient, InternalDriveFileId file);

    }
}