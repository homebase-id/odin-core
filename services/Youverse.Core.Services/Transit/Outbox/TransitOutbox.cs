using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Transit.Outbox
{
    public class OutboxItemState
    {
        public string Recipient { get; set; }

        public List<TransferAttempt> Attempts { get; }

        public bool IsTransientFile { get; set; }
        public RsaEncryptedRecipientTransferInstructionSet TransferInstructionSet { get; set; }
        
        public TransitOptions OriginalTransitOptions { get; set; }
        public byte[] EncryptedClientAuthToken { get; set; }
    }

    /// <summary>
    /// Services that manages items in a given Tenant's outbox
    /// </summary>
    public class TransitOutbox : ITransitOutbox
    {
        private readonly IPendingTransfersService _pendingTransfers;
        private readonly ITenantSystemStorage _tenantSystemStorage;
        private readonly TenantContext _tenantContext;

        public TransitOutbox(IPendingTransfersService pendingTransfers, ITenantSystemStorage tenantSystemStorage, TenantContext tenantContext)
        {
            _pendingTransfers = pendingTransfers;
            _tenantSystemStorage = tenantSystemStorage;
            _tenantContext = tenantContext;
        }

        /// <summary>
        /// Adds an item to be encrypted and moved to the outbox
        /// </summary>
        /// <param name="item"></param>
        public Task Add(TransitOutboxItem item)
        {
            //TODO: change to use batching inserts

            //TODO: value should also include transfer attempts, etc.
            var state = DotYouSystemSerializer.Serialize(new OutboxItemState()
            {
                Recipient = item.Recipient,
                IsTransientFile = item.IsTransientFile,
                Attempts = { },
                TransferInstructionSet = item.TransferInstructionSet,
                OriginalTransitOptions = item.OriginalTransitOptions,
                EncryptedClientAuthToken = item.EncryptedClientAuthToken
            }).ToUtf8ByteArray();
            
            
            /*_tenantSystemStorage.Outbox.InsertRow(
                item.File.DriveId.ToByteArray(),
                item.File.FileId.ToByteArray(),
                item.Priority,
                state);*/

            _tenantSystemStorage.Outbox.Insert(new Storage.Sqlite.IdentityDatabase.OutboxItem() {
                boxId = item.File.DriveId,
                recipient = item.Recipient,
                fileId = item.File.FileId,
                priority = item.Priority,
                value = state });

            _pendingTransfers.EnsureIdentityIsPending(_tenantContext.HostOdinId);
            return Task.CompletedTask;
        }

        public Task Add(IEnumerable<TransitOutboxItem> items)
        {
            foreach (var item in items)
            {
                this.Add(item);
            }

            return Task.CompletedTask;
        }

        public Task MarkComplete(Guid marker)
        {
            _tenantSystemStorage.Outbox.PopCommit(marker);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Add and item back the queue due to a failure
        /// </summary>
        public async Task MarkFailure(Guid marker, TransferFailureReason reason)
        {
            _tenantSystemStorage.Outbox.PopCommitList(marker, listFileId: new List<Guid>());
            //TODO: there is no way to keep information on why an item failed
            _tenantSystemStorage.Outbox.PopCancel(marker);

            // if (null == item)
            // {
            //     return;
            // }

            // item.Attempts.Add(new TransferAttempt()
            // {
            //     TransferFailureReason = reason,
            //     Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            // });
            
            await Task.CompletedTask;
        }

        public async Task<List<TransitOutboxItem>> GetBatchForProcessing(Guid driveId, int batchSize)
        {
            //CRITICAL NOTE: To integrate this with the existing outbox design, you can only pop one item at a time since the marker defines a set
            var records = _tenantSystemStorage.Outbox.Pop(driveId, batchSize, out var marker);

            var items = records.Select(r =>
            {
                var state = DotYouSystemSerializer.Deserialize<OutboxItemState>(r.value.ToStringFromUtf8Bytes());
                return new TransitOutboxItem()
                {
                    Recipient = (OdinId)state!.Recipient,
                    IsTransientFile = state!.IsTransientFile,
                    Priority = (int)r.priority,
                    AddedTimestamp = r.timeStamp.seconds,
                    TransferInstructionSet = state.TransferInstructionSet,
                    File = new InternalDriveFileId()
                    {
                        DriveId = r.boxId,
                        FileId = r.fileId
                    },
                    OriginalTransitOptions = state.OriginalTransitOptions,
                    EncryptedClientAuthToken = state.EncryptedClientAuthToken,
                    Marker = marker
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