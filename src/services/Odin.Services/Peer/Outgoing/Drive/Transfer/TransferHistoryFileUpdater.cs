using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Mediator.Outbox;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer
{
    public class TransferHistoryFileUpdater(
        OdinContextAccessor contextAccessor,
        IOdinHttpClientFactory odinHttpClientFactory,
        CircleNetworkService circleNetworkService,
        FileSystemResolver fileSystemResolver,
        ILogger<TransferHistoryFileUpdater> logger)
        : PeerServiceBase(odinHttpClientFactory, circleNetworkService, contextAccessor, fileSystemResolver),
            INotificationHandler<OutboxItemProcessedNotification>
    {
        private readonly FileSystemResolver _fileSystemResolver = fileSystemResolver;

        public async Task Handle(OutboxItemProcessedNotification notification, CancellationToken cancellationToken)
        {
            var fs = _fileSystemResolver.ResolveFileSystem(notification.FileSystemType);
            var header = await fs.Storage.GetServerFileHeader(notification.File);

            if (null == header)
            {
                logger.LogWarning("OutboxItemProcessedNotification raised for a file that " +
                                  "does not exist (File [{file}] on drive [{driveId}])",
                    notification.File.FileId,
                    notification.File.DriveId);

                return;
            }

            // update the file for the outbox item with the latest status for all recipients

            //TODO: consider the structure here, should i use a dictionary instead?
            var recipient = notification.Recipient;
            var history = header.FileMetadata.TransferHistory ?? new RecipientTransferHistory();
            var items = history.Items ?? new List<RecipientTransferHistoryItem>();

            var recipientItem = items.SingleOrDefault(item => item.Recipient == recipient) ?? new RecipientTransferHistoryItem();
            recipientItem.Recipient = recipient;
            recipientItem.Status = notification.TransferStatus;
            recipientItem.LastUpdated = UnixTimeUtc.Now();

            await fs.Storage.UpdateActiveFileHeader(notification.File, header);
        }
    }
}