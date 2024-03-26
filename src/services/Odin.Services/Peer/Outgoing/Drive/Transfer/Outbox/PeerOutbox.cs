using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox
{
    public class OutboxItemState
    {
        public string Recipient { get; set; }

        public List<TransferAttempt> Attempts { get; }

        public bool IsTransientFile { get; set; }
        public EncryptedRecipientTransferInstructionSet TransferInstructionSet { get; set; }

        public TransitOptions OriginalTransitOptions { get; set; }
        public byte[] EncryptedClientAuthToken { get; set; }
    }

    /// <summary>
    /// Services that manages items in a given Tenant's outbox
    /// </summary>
    public class PeerOutbox(ServerSystemStorage serverSystemStorage, TenantSystemStorage tenantSystemStorage, TenantContext tenantContext)
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
                TransferInstructionSet = item.TransferInstructionSet,
                OriginalTransitOptions = item.OriginalTransitOptions,
                EncryptedClientAuthToken = item.EncryptedClientAuthToken,
                Attempts = { },

            }).ToUtf8ByteArray();

            tenantSystemStorage.Outbox.Insert(new OutboxRecord()
            {
                driveId = item.File.DriveId,
                recipient = item.Recipient,
                fileId = item.File.FileId,
                priority = item.Priority,
                type = (int)item.Type,
                value = state
            });

            var sender = tenantContext.HostOdinId;
            serverSystemStorage.EnqueueJob(sender, CronJobType.PendingTransitTransfer, sender.DomainName.ToLower().ToUtf8ByteArray(), UnixTimeUtc.Now());

            return Task.CompletedTask;
        }

        public Task MarkComplete(Guid marker)
        {
            tenantSystemStorage.Outbox.PopCommitAll(marker);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Add and item back the queue due to a failure
        /// </summary>
        public Task MarkFailure(Guid marker, TransferResult reason)
        {
            tenantSystemStorage.Outbox.PopCommitList(marker, listFileId: new List<Guid>());
            //TODO: there is no way to keep information on why an item failed
            tenantSystemStorage.Outbox.PopCancelAll(marker);
            return Task.CompletedTask;
        }

        public Task RecoverDead(UnixTimeUtc time)
        {
            tenantSystemStorage.Outbox.PopRecoverDead(time);
            return Task.CompletedTask;
        }

        public async Task<List<OutboxItem>> GetBatchForProcessing(Guid driveId, int batchSize)
        {
            //CRITICAL NOTE: To integrate this with the existing outbox design, you can only pop one item at a time since the marker defines a set
            var records = tenantSystemStorage.Outbox.PopSpecificBox(driveId, batchSize);

            var items = records.Select(r =>
            {
                var state = OdinSystemSerializer.Deserialize<OutboxItemState>(r.value.ToStringFromUtf8Bytes());
                return new OutboxItem()
                {
                    Recipient = (OdinId)state!.Recipient,
                    IsTransientFile = state!.IsTransientFile,
                    Priority = r.priority,
                    AddedTimestamp = r.timeStamp.seconds,
                    Type = (OutboxItemType)r.type,
                    TransferInstructionSet = state.TransferInstructionSet,
                    File = new InternalDriveFileId()
                    {
                        DriveId = r.driveId,
                        FileId = r.fileId
                    },
                    OriginalTransitOptions = state.OriginalTransitOptions,
                    EncryptedClientAuthToken = state.EncryptedClientAuthToken,
                    Marker = r.popStamp.GetValueOrDefault()
                };
            });

            return await Task.FromResult(items.ToList());
        }

        public Task Remove(OdinId recipient, InternalDriveFileId file)
        {
            //TODO: need to make a better queue here
            throw new NotImplementedException("Sqllite outbox needs ability to query by recipient");
        }
    }
}