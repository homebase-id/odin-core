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
        /// <param name="item"></param>
        Task Add(OutboxItem item);

        Task Add(IEnumerable<OutboxItem> items);

        Task MarkComplete(Guid marker);

        /// <summary>
        /// Add and item back the queue due to a failure
        /// </summary>
        Task MarkFailure(Guid marker, UnixTimeUtc nextRun);

        Task RecoverDead(UnixTimeUtc time);

        Task<OutboxItem> GetNextItem();

        /// <summary>
        /// Removes the outbox item for the given recipient and file
        /// </summary>
        /// <returns></returns>
        Task Remove(OdinId recipient, InternalDriveFileId file);

    }
}