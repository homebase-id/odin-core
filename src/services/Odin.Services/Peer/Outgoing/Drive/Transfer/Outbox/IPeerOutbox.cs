﻿using System;
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
        Task Add(OutboxFileItem fileItem, DatabaseConnection cn, bool useUpsert = false);

        Task MarkComplete(Guid marker, DatabaseConnection cn);

        /// <summary>
        /// Add and item back the queue due to a failure
        /// </summary>
        Task MarkFailure(Guid marker, UnixTimeUtc nextRun, DatabaseConnection cn);

        Task RecoverDead(UnixTimeUtc time, DatabaseConnection cn);

        Task<OutboxFileItem> GetNextItem(DatabaseConnection cn);

        /// <summary>
        /// Checks if this outbox item exists and is of type OutboxItemType.File
        /// </summary>
        Task<bool> HasOutboxFileItem(OutboxFileItem arg, DatabaseConnection cn);

        Task<OutboxDriveStatus> GetOutboxStatus(Guid driveId, DatabaseConnection cn);
    }
}