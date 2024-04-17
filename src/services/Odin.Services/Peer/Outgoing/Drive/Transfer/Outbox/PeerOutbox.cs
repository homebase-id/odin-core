using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox
{
    /// <summary>
    /// Services that manages items in a given Tenant's outbox
    /// </summary>
    public class PeerOutbox(ServerSystemStorage serverSystemStorage, TenantSystemStorage tenantSystemStorage, TenantContext tenantContext) : IPeerOutbox
    {
        /// <summary>
        /// Adds an item to be encrypted and moved to the outbox
        /// </summary>
        /// <param name="item"></param>
        public Task Add(OutboxItem item)
        {
            //TODO: change to use batching inserts

            //TODO: value should also include transfer attempts, etc.
            var state = OdinSystemSerializer.Serialize(new OutboxItemState()
            {
                Recipient = item.Recipient,
                IsTransientFile = item.IsTransientFile,
                Attempts = { },
                TransferInstructionSet = item.TransferInstructionSet,
                OriginalTransitOptions = item.OriginalTransitOptions,
                EncryptedClientAuthToken = item.EncryptedClientAuthToken
            }).ToUtf8ByteArray();

            tenantSystemStorage.Outbox.Insert(new OutboxRecord()
            {
                driveId = item.File.DriveId,
                recipient = item.Recipient,
                fileId = item.File.FileId,
                priority = item.Priority,
                value = state
            });

            var sender = tenantContext.HostOdinId;
            serverSystemStorage.EnqueueJob(sender, CronJobType.PendingTransitTransfer, sender.DomainName.ToLower().ToUtf8ByteArray(), UnixTimeUtc.Now());

            return Task.CompletedTask;
        }

        public Task Add(IEnumerable<OutboxItem> items)
        {
            foreach (var item in items)
            {
                Add(item);
            }

            return Task.CompletedTask;
        }

        public Task MarkComplete(Guid marker)
        {
            tenantSystemStorage.Outbox.CompleteAndRemove(marker);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Add and item back the queue due to a failure
        /// </summary>
        public async Task MarkFailure(Guid marker, UnixTimeUtc nextRun)
        {
            
            tenantSystemStorage.Outbox.CheckInAsCancelled(marker, nextRun);
            await Task.CompletedTask;
        }

        public Task RecoverDead(UnixTimeUtc time)
        {
            tenantSystemStorage.Outbox.RecoverCheckedOutDeadItems(time);
            return Task.CompletedTask;
        }

        public async Task<OutboxItem> GetNextItem()
        {
            var record = tenantSystemStorage.Outbox.CheckOutItem();

            if (null == record)
            {
                return await Task.FromResult<OutboxItem>(null);
            }

            var state = OdinSystemSerializer.Deserialize<OutboxItemState>(record.value.ToStringFromUtf8Bytes());
            var item = new OutboxItem()
            {
                Recipient = (OdinId)record.recipient,
                Priority = record.priority,
                AddedTimestamp = record.created.ToUnixTimeUtc().seconds,
                Type = (OutboxItemType)record.type,
                TransferInstructionSet = state.TransferInstructionSet,
                File = new InternalDriveFileId()
                {
                    DriveId = record.driveId,
                    FileId = record.fileId
                },
                AttemptCount = record.checkOutCount,
                OriginalTransitOptions = state.OriginalTransitOptions,
                EncryptedClientAuthToken = state.EncryptedClientAuthToken,
                Marker = record.checkOutStamp.GetValueOrDefault(),
                RawValue = record.value
            };

            return await Task.FromResult(item);
        }
        
        public Task Remove(OdinId recipient, InternalDriveFileId file)
        {
            //TODO: need to make a better queue here
            throw new NotImplementedException("Sqllite outbox needs ability to query by recipient");
        }
    }
}