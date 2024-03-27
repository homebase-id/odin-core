using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Mediator.Outbox;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer
{
    public class PeerTransferHistoryFileUpdater(
        OdinContextAccessor contextAccessor,
        IOdinHttpClientFactory odinHttpClientFactory,
        CircleNetworkService circleNetworkService,
        FileSystemResolver fileSystemResolver,
        ILogger<PeerTransferHistoryFileUpdater> logger)
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

            //TODO: consider the structure here, should i use a dictionary instead?
            var recipient = notification.Recipient.ToString().ToLower();
            var history = header.ServerMetadata.TransferHistory ?? new RecipientTransferHistory();
            if (history.Items == null)
            {
                history.Items = new Dictionary<string, RecipientTransferHistoryItem>(StringComparer.InvariantCultureIgnoreCase);
            }

            if (!history.Items.TryGetValue(recipient, out var recipientItem))
            {
                recipientItem = new RecipientTransferHistoryItem();
                history.Items.Add(recipient, recipientItem);
            }

            var problemStatus = MapStatus(notification.TransferStatus);
            recipientItem.LastUpdated = UnixTimeUtc.Now();
            recipientItem.LatestProblemStatus = problemStatus;
            if (problemStatus == null)
            {
                recipientItem.LatestSuccessfullyDeliveredVersionTag = notification.VersionTag;
            }

            header.ServerMetadata.TransferHistory = history;
            await fs.Storage.UpdateActiveFileHeader(notification.File, header, true);
        }

        private LatestProblemStatus? MapStatus(TransferStatus status)
        {
            switch (status)
            {
                case TransferStatus.PendingRetry:
                    return LatestProblemStatus.ServerPendingRetry;

                case TransferStatus.TotalRejectionClientShouldRetry:
                    return LatestProblemStatus.ClientMustRetry;

                case TransferStatus.FileDoesNotAllowDistribution:
                case TransferStatus.RecipientDoesNotHavePermissionToFileAcl:
                    return LatestProblemStatus.LocalFileDistributionDenied;

                case TransferStatus.RecipientReturnedAccessDenied:
                    return LatestProblemStatus.AccessDenied;

                case TransferStatus.AwaitingTransferKey:
                case TransferStatus.TransferKeyCreated:
                case TransferStatus.DeliveredToInbox:
                case TransferStatus.DeliveredToTargetDrive:
                    return null;
            }

            return null;
        }
    }
}