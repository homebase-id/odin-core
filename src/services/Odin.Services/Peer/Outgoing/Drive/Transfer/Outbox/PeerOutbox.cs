using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox
{
    /// <summary>
    /// Services that manages items in a given Tenant's outbox
    /// </summary>
    public class PeerOutbox(TenantSystemStorage tenantSystemStorage) : IPeerOutbox
    {
        /// <summary>
        /// Adds an item to be encrypted and moved to the outbox
        /// </summary>
        public Task Add(OutboxFileItem fileItem, DatabaseConnection cn, bool useUpsert = false)
        {
            var state = OdinSystemSerializer.Serialize(new OutboxItemState()
            {
                Recipient = fileItem.Recipient,
                IsTransientFile = fileItem.IsTransientFile,
                Attempts = { },
                TransferInstructionSet = fileItem.TransferInstructionSet,
                OriginalTransitOptions = fileItem.OriginalTransitOptions,
                EncryptedClientAuthToken = fileItem.EncryptedClientAuthToken
            }).ToUtf8ByteArray();

            var record = new OutboxRecord()
            {
                driveId = fileItem.File.DriveId,
                recipient = fileItem.Recipient,
                fileId = fileItem.File.FileId,
                dependencyFileId = fileItem.DependencyFileId,
                type = (int)OutboxItemType.File,
                priority = fileItem.Priority,
                value = state
            };

            if (useUpsert)
            {
                tenantSystemStorage.Outbox.Upsert(cn, record);
            }
            else
            {
                tenantSystemStorage.Outbox.Insert(cn, record);
            }

            return Task.CompletedTask;
        }

        public Task MarkComplete(Guid marker, DatabaseConnection cn)
        {
            tenantSystemStorage.Outbox.CompleteAndRemove(cn, marker);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Add and item back the queue due to a failure
        /// </summary>
        public Task MarkFailure(Guid marker, UnixTimeUtc nextRun, DatabaseConnection cn)
        {
            tenantSystemStorage.Outbox.CheckInAsCancelled(cn, marker, nextRun);
            return Task.CompletedTask;
        }

        public Task RecoverDead(UnixTimeUtc time, DatabaseConnection cn)
        {
            tenantSystemStorage.Outbox.RecoverCheckedOutDeadItems(cn, time);
            return Task.CompletedTask;
        }

        public async Task<OutboxFileItem> GetNextItem(DatabaseConnection cn)
        {
            var record = tenantSystemStorage.Outbox.CheckOutItem(cn);

            if (null == record)
            {
                return await Task.FromResult<OutboxFileItem>(null);
            }

            var state = OdinSystemSerializer.Deserialize<OutboxItemState>(record.value.ToStringFromUtf8Bytes());
            var item = new OutboxFileItem()
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

        public Task<bool> HasOutboxFileItem(OutboxFileItem fileItem, DatabaseConnection cn)
        {
            var records = tenantSystemStorage.Outbox.Get(cn, fileItem.File.DriveId, fileItem.File.FileId);
            var hasRecord = records?.Any(r => r.type == (int)OutboxItemType.File) ?? false;
            return Task.FromResult(hasRecord);
        }

        /// <summary>
        /// Gets the status of the specified Drive
        /// </summary>
        public async Task<OutboxDriveStatus> GetOutboxStatus(Guid driveId, DatabaseConnection cn)
        {
            var (totalCount, poppedCount, utc) = tenantSystemStorage.Outbox.OutboxStatusDrive(cn, driveId);
            return await Task.FromResult(new OutboxDriveStatus()
            {
                CheckedOutCount = poppedCount,
                TotalItems = totalCount,
                NextItemRun = utc
            });
        }
    }
}