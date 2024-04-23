using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Drives;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox
{
    /// <summary>
    /// Items in the outbox for a given tenant
    /// </summary>
    public interface IPeerOutbox
    {
        /// <summary>
        /// Adds an item to be encrypted and moved to the outbox
        /// </summary>
        Task Add(OutboxItem item, bool useUpsert = false);

        Task Add(IEnumerable<OutboxItem> items);

        Task MarkComplete(Guid marker);

        /// <summary>
        /// Add and item back the queue due to a failure
        /// </summary>
        Task MarkFailure(Guid marker, UnixTimeUtc nextRun);

        Task RecoverDead(UnixTimeUtc time);

        Task<OutboxItem> GetNextItem();

        /// <summary>
        /// Checks if this outbox item exists and is of type OutboxItemType.File
        /// </summary>
        Task<bool> HasOutboxFileItem(OutboxItem arg);
    }
}