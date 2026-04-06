using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Drives;

namespace Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage
{
    /// <summary>
    /// Manages items incoming to a DI that have not yet been processed (pre-inbox)
    /// </summary>
    public class TransitInboxBoxStorage(TableInboxCached tableInbox, ILogger<TransitInboxBoxStorage> logger)
    {
        public async Task AddAsync(TransferInboxItem item)
        {
            item.AddedTimestamp = UnixTimeUtc.Now();

            var state = OdinSystemSerializer.Serialize(item).ToUtf8ByteArray();
            await tableInbox.InsertAsync(new InboxRecord() { boxId = item.DriveId, fileId = item.FileId, priority = 1, value = state });

            PerformanceCounter.IncrementCounter("Inbox Item Added");
        }

        /// <summary>
        /// Returns the cached count of items ready to be processed (not yet popped) for the given drive.
        /// This is a cheap cache-guarded check: on the hot path (empty inbox), it returns 0 from the
        /// in-memory FusionCache with zero DB calls. Use this before deciding whether to call
        /// ProcessInboxAsync — if it returns 0, skip processing entirely and avoid all the overhead
        /// of the full inbox pop/decrypt/write pipeline.
        /// </summary>
        public async Task<int> GetReadyCountAsync(Guid driveId)
        {
            return await tableInbox.GetReadyCountAsync(driveId);
        }

        public async Task<InboxStatus> GetStatusAsync(Guid driveId)
        {
            var p = await tableInbox.PopStatusSpecificBoxAsync(driveId);

            return new InboxStatus
            {
                TotalItems = p.totalCount,
                PoppedCount = p.poppedCount,
                OldestItemTimestamp = p.oldestItemTime,
            };
        }


        public async Task<InboxStatus> GetPendingCountAsync(Guid driveId)
        {
            var p = await tableInbox.PopStatusSpecificBoxAsync(driveId);
            return new InboxStatus()
            {
                TotalItems = p.totalCount,
                PoppedCount = p.poppedCount,
                OldestItemTimestamp = p.oldestItemTime,
            };
        }

        public async Task<List<TransferInboxItem>> GetPendingItemsAsync(Guid driveId, int batchSize)
        {
            //CRITICAL NOTE: we can only get back one item since we want to make sure the marker is for that one item in-case the operation fails
            var records = await tableInbox.PopSpecificBoxAsync(driveId, batchSize == 0 ? 1 : batchSize);

            if (null == records)
            {
                return new List<TransferInboxItem>();
            }

            PerformanceCounter.IncrementCounter("Inbox Item Checkout");

            var items = records.Select(r =>
            {
                var item = OdinSystemSerializer.Deserialize<TransferInboxItem>(r.value.ToStringFromUtf8Bytes());

                item.Priority = r.priority;
                item.AddedTimestamp = r.timeStamp;
                item.DriveId = r.boxId;
                item.FileId = r.fileId;
                item.Marker = r.popStamp.GetValueOrDefault();
                item.CorrelationId = r.correlationId;

                return item;
            }).ToList();

            return items;
        }

        public async Task<int> MarkCompleteAsync(InternalDriveFileId file, Guid marker)
        {
            int n = await tableInbox.PopCommitListAsync(marker, file.DriveId, [file.FileId]);

            if (n != 1)
            {
                logger.LogError("Failed to mark inbox record complete. File:{file}.  Marker:{marker}", file, marker);
                throw new OdinSystemException("Hum, unable to mark the inbox record as completed, aborting");
            }

            logger.LogDebug("Mark inbox record complete. File:{file}.  Marker:{marker}", file, marker);

            PerformanceCounter.IncrementCounter("Inbox Mark Complete");

            return n;
        }

        public async Task<int> MarkFailureAsync(InternalDriveFileId file, Guid marker)
        {
            int n = await tableInbox.PopCancelListAsync(marker, file.DriveId, [file.FileId]);
            PerformanceCounter.IncrementCounter("Inbox Mark Failure");
            return n;
        }

        public async Task<int> RecoverDeadAsync(UnixTimeUtc time)
        {
            var recovered = await tableInbox.PopRecoverDeadAsync(time);
            PerformanceCounter.IncrementCounter("Inbox Recover Dead");
            return recovered;
        }
    }
}