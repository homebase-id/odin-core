using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.SQLite;
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
        Task Add(OutboxItem item, DatabaseConnection cn, bool useUpsert = false);

        Task Add(IEnumerable<OutboxItem> items, DatabaseConnection cn);

        Task MarkComplete(Guid marker, DatabaseConnection cn);

        /// <summary>
        /// Add and item back the queue due to a failure
        /// </summary>
        Task MarkFailure(Guid marker, UnixTimeUtc nextRun, DatabaseConnection cn);

        Task RecoverDead(UnixTimeUtc time, DatabaseConnection cn);

        Task<OutboxItem> GetNextItem(DatabaseConnection cn);

        /// <summary>
        /// Checks if this outbox item exists and is of type OutboxItemType.File
        /// </summary>
        Task<bool> HasOutboxFileItem(OutboxItem arg, DatabaseConnection cn);

        Task<OutboxAsync> GetOutboxStatus(Guid driveId, DatabaseConnection cn);
    }
}