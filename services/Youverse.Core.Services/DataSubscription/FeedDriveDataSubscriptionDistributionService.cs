using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.Management;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.DataSubscription
{
    /// <summary>
    /// Distributes files from channels to follower's feed drives (and only the feed drive)
    /// </summary>
    public class FeedDriveDataSubscriptionDistributionService : INotificationHandler<IDriveNotification>
    {
        private readonly FollowerService _followerService;
        private readonly DriveManager _driveManager;
        private readonly ITransitService _transitService;
        private readonly TenantContext _tenantContext;

        public FeedDriveDataSubscriptionDistributionService(
            FollowerService followerService,
            ITransitService transitService, DriveManager driveManager, TenantContext tenantContext)
        {
            _followerService = followerService;
            _transitService = transitService;
            _driveManager = driveManager;
            _tenantContext = tenantContext;
        }

        public async Task Handle(IDriveNotification notification, CancellationToken cancellationToken)
        {
            //if the file was received from another identity, do not redistribute
            var sender = notification.ServerFileHeader?.FileMetadata?.SenderOdinId;
            var uploadedByThisIdentity = sender == _tenantContext.HostOdinId || string.IsNullOrEmpty(sender?.Trim());
            if (!uploadedByThisIdentity)
            {
                return;
            }

            if (notification.ServerFileHeader == null) //file was deleted
            {
                return;
            }

            if (!notification.ServerFileHeader.ServerMetadata.AllowDistribution)
            {
                return;
            }

            //We only distribute standard files to populate the feed.  Comments are retrieved by calls over transit query
            if (notification.ServerFileHeader.ServerMetadata.FileSystemType != FileSystemType.Standard)
            {
                return;
            }

            //TODO: move this to a background thread or use ScheduleOptions.SendLater so the original call can finish
            //this will come into play when someone has a huge number of subscribers

            if (!await SupportsSubscription(notification.File.DriveId))
            {
                return;
            }

            int maxRecords = 10000; //TODO: cursor thru batches instead
            var driveFollowers = await _followerService.GetFollowers(notification.File.DriveId, maxRecords, cursor: "");
            var allDriveFollowers = await _followerService.GetFollowersOfAllNotifications(maxRecords, cursor: "");

            var recipients = new List<string>();
            recipients.AddRange(driveFollowers.Results);
            recipients.AddRange(allDriveFollowers.Results.Except(driveFollowers.Results));

            if (!recipients.Any())
            {
                return;
            }

            if (notification.DriveNotificationType == DriveNotificationType.FileDeleted)
            {
                await HandleFileDeleted(notification, recipients);
            }

            await HandleFileAddOrUpdate(notification, recipients);
        }

        private async Task HandleFileDeleted(IDriveNotification notification, List<string> recipients)
        {
            var header = notification.ServerFileHeader;

            if (header.FileMetadata.GlobalTransitId.HasValue)
            {
                //send the deleted file
                var map = await _transitService.SendDeleteLinkedFileRequest(
                    new GlobalTransitIdFileIdentifier()
                    {
                        TargetDrive = SystemDriveConstants.FeedDrive,
                        GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                    },
                    sendFileOptions: new SendFileOptions()
                    {
                        FileSystemType = header.ServerMetadata.FileSystemType,
                        TransferFileType = TransferFileType.Normal,
                        ClientAccessTokenSource = ClientAccessTokenSource.Follower
                    },
                    recipients);

                //TODO: Handle issues/results 
                // foreach (var (key, value) in map)
                // {
                //     switch (value)
                //     {
                //         case TransitResponseCode.Accepted:
                //             break;
                //
                //         case TransitResponseCode.Rejected:
                //         case TransitResponseCode.QuarantinedPayload:
                //         case TransitResponseCode.QuarantinedSenderNotConnected:
                //             break;
                //
                //         default:
                //     }
                // }
            }
        }

        private async Task HandleFileAddOrUpdate(IDriveNotification notification, List<string> recipients)
        {
            //use transit? to send like normal?
            var transitOptions = new TransitOptions()
            {
                Recipients = recipients,
                Schedule = ScheduleOptions.SendNowAwaitResponse, //hmm should send later?
                IsTransient = false,
                UseGlobalTransitId = true,
                SendContents = SendContents.Header,
                RemoteTargetDrive = SystemDriveConstants.FeedDrive
            };

            //
            //TODO: in order to send over transit like this, the sender needs access to the feed drive
            var _ = await _transitService.SendFile(
                notification.File,
                transitOptions,
                TransferFileType.Normal,
                notification.ServerFileHeader.ServerMetadata.FileSystemType,
                ClientAccessTokenSource.Follower);
        }

        private async Task<bool> SupportsSubscription(Guid driveId)
        {
            var drive = await _driveManager.GetDrive(driveId, false);
            return drive.AllowSubscriptions;
        }
    }
}