using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Transit.Outbox
{
    public class OutboxItemState
    {
        public string Recipient { get; set; }

        public List<TransferAttempt> Attempts { get; }

        public bool IsTransientFile { get; set; }
    }

    /// <summary>
    /// Services that manages items in a given Tenant's outbox
    /// </summary>
    public class OutboxService : IOutboxService
    {
        private readonly IPendingTransfersService _pendingTransfers;
        private readonly ISystemStorage _systemStorage;
        private readonly DotYouContextAccessor _contextAccessorAccessor;
        private readonly TenantContext _tenantContext;
        private const string OutboxItemsCollection = "obxitems";

        public OutboxService(DotYouContextAccessor contextAccessor, ILogger<IOutboxService> logger, IPendingTransfersService pendingTransfers, ISystemStorage systemStorage,
            TenantContext tenantContext)
        {
            _contextAccessorAccessor = contextAccessor;
            _pendingTransfers = pendingTransfers;
            _systemStorage = systemStorage;
            _tenantContext = tenantContext;
        }

        /// <summary>
        /// Adds an item to be encrypted and moved to the outbox
        /// </summary>
        /// <param name="item"></param>
        public Task Add(OutboxItem item)
        {
            //TODO: value should also include transfer attempts, etc.
            var state = DotYouSystemSerializer.Serialize(new OutboxItemState()
            {
                Recipient = item.Recipient,
                IsTransientFile = item.IsTransientFile,
                Attempts = { }
            }).ToUtf8ByteArray();

            _systemStorage.Outbox.InsertRow(
                item.File.DriveId.ToByteArray(),
                item.File.FileId.ToByteArray(),
                item.Priority,
                state);

            _pendingTransfers.EnsureSenderIsPending(_tenantContext.HostDotYouId);
            return Task.CompletedTask;
        }

        public Task Add(IEnumerable<OutboxItem> items)
        {
            foreach (var item in items)
            {
                this.Add(item);
            }

            return Task.CompletedTask;
        }

        public Task MarkComplete(byte[] marker)
        {
            _systemStorage.Outbox.PopCommit(marker);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Add and item back the queue due to a failure
        /// </summary>
        public async Task MarkFailure(byte[] marker, TransferFailureReason reason)
        {
            _systemStorage.Outbox.PopCommitList(marker, listFileId: new List<byte[]>());
            //TODO: there is no way to keep information on why an item failed
            _systemStorage.Outbox.PopCancel(marker);

            // if (null == item)
            // {
            //     return;
            // }

            // item.Attempts.Add(new TransferAttempt()
            // {
            //     TransferFailureReason = reason,
            //     Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            // });
        }

        public async Task<List<OutboxItem>> GetBatchForProcessing(Guid driveId, int batchSize)
        {
            //CRITICAL NOTE: To integrate this with the existing outbox design, you can only pop one item at a time since the marker defines a set
            var records = _systemStorage.Outbox.Pop(driveId.ToByteArray(), batchSize, out var marker);

            var items = records.Select(r =>
            {
                var state = DotYouSystemSerializer.Deserialize<OutboxItemState>(r.value.ToStringFromUtf8Bytes());
                return new OutboxItem()
                {
                    Recipient = (DotYouIdentity)state!.Recipient,
                    IsTransientFile = state!.IsTransientFile,
                    Priority = (int)r.priority,
                    AddedTimestamp = r.timeStamp.seconds,
                    File = new InternalDriveFileId()
                    {
                        DriveId = new Guid(r.boxId),
                        FileId = new Guid(r.fileId)
                    },
                    Marker = marker
                };
            });

            return items.ToList();
        }

        public Task Remove(DotYouIdentity recipient, InternalDriveFileId file)
        {
            //TODO: need to make a better queue here
            throw new NotImplementedException("Sqllite outbox needs ability to query by recipient");
        }
    }
}