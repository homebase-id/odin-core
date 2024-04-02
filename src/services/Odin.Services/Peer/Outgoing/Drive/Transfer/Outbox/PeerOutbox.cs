using System;
using System.Collections.Generic;
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
    public class OutboxStatus
    {
        public int TotalItems { get; set; }
        public int CheckedOutCount { get; set; }
        public UnixTimeUtc NextItemRun { get; set; }
    }

    public class OutboxItemState
    {
        public string Recipient { get; set; }

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
        public async Task Add(List<OutboxItem> items)
        {
            using (tenantSystemStorage.CreateCommitUnitOfWork())
            {
                foreach (var item in items)
                {
                    //TODO: change to use batching inserts

                    //TODO: value should also include transfer attempts, etc.
                    var state = OdinSystemSerializer.Serialize(new OutboxItemState()
                    {
                        Recipient = item.Recipient,
                        IsTransientFile = item.IsTransientFile,
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
                        type = (int)item.Type,
                        dependencyFileId = item.OriginalTransitOptions.OutboxDependencyFileId,
                        value = state
                    });
                }

                var sender = tenantContext.HostOdinId;
                serverSystemStorage.EnqueueJob(sender, CronJobType.PendingTransitTransfer, sender.DomainName.ToLower().ToUtf8ByteArray(), UnixTimeUtc.Now());
            }

            await Task.CompletedTask;
        }

        public Task MarkComplete(Guid marker)
        {
            tenantSystemStorage.Outbox.CompleteAndRemove(marker);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Add and item back the queue due to a failure
        /// </summary>
        public Task MarkFailure(Guid marker, UnixTimeUtc nextRunTime)
        {
            tenantSystemStorage.Outbox.CheckInAsCancelled(marker, nextRunTime);
            return Task.CompletedTask;
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
                IsTransientFile = state!.IsTransientFile,
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

        /// <summary>
        /// Gets the status of the specified Drive
        /// </summary>
        public async Task<OutboxStatus> GetOutboxStatus(Guid driveId)
        {
            var (totalCount, poppedCount, utc) = tenantSystemStorage.Outbox.OutboxStatusDrive(driveId);
            return await Task.FromResult<OutboxStatus>(new OutboxStatus()
            {
                CheckedOutCount = poppedCount,
                TotalItems = totalCount,
                NextItemRun = utc
            });
        }
    }
}