using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.Push;
using Odin.Services.AppNotifications.SystemNotifications;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Drives.Reactions;
using Odin.Services.Mediator;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.DataSubscription
{
    /// <summary>
    /// Maps incoming notifications to the feed drive  and ensures they're sent. (this is because things like
    /// reactions are available for any drive but we want to identify a notification with an app)
    ///
    /// Yes, this is more Feed hack'ish stuff that indicates we need to build a feed subsystem in the core
    /// </summary>
    public class FeedNotificationMapper(
        ILogger<FeedNotificationMapper> logger,
        IDriveManager driveManager,
        PushNotificationService pushNotificationService,
        TenantContext tenantContext)
        : INotificationHandler<ReactionContentAddedNotification>, INotificationHandler<NewFeedItemReceived>,
            INotificationHandler<NewFollowerNotification>, INotificationHandler<DriveFileAddedNotification>
    {
        internal static readonly Guid CommentNotificationTypeId = Guid.Parse("1e08b70a-3826-4840-8372-18410bfc02c7");
        private static readonly Guid PostNotificationTypeId = Guid.Parse("ad695388-c2df-47a0-ad5b-fc9f9e1fffc9");

        public async Task Handle(ReactionContentAddedNotification notification, CancellationToken cancellationToken)
        {
            var driveId = notification.Reaction.FileId.DriveId;
            if (await IsFeedDriveRelatedAsync(driveId))
            {
                var sender = (OdinId)notification.Reaction.OdinId;
                if (sender != tenantContext.HostOdinId)
                {
                    var odinContext = notification.OdinContext;
                    var newContext = OdinContextUpgrades.UpgradeToPeerTransferContext(odinContext);
                    await pushNotificationService.EnqueueNotification(sender, new AppNotificationOptions()
                    {
                        AppId = SystemAppConstants.FeedAppId,
                        TypeId = notification.NotificationTypeId,
                        TagId = notification.Reaction.FileId.FileId,
                        Silent = false,
                    },
                        newContext);
                }
            }
        }

        public async Task Handle(NewFeedItemReceived notification, CancellationToken cancellationToken)
        {
            var odinContext = notification.OdinContext;
            var newContext = OdinContextUpgrades.UpgradeToPeerTransferContext(odinContext);

            await pushNotificationService.EnqueueNotification(notification.Sender, new AppNotificationOptions()
            {
                AppId = SystemAppConstants.FeedAppId,
                TypeId = PostNotificationTypeId,
                TagId = notification.GlobalTransitId,
                Silent = false,
            },
                newContext);
        }

        /// <summary>
        /// A comment someone else left on a post in one of my channels, posted here (from my home page) or sent by a
        /// connected identity: the one place a comment is notified, once, when it is first written.  Comments are never
        /// written to the Feed drive.  A failure is logged, so the event's other handlers still run.
        /// </summary>
        public async Task Handle(DriveFileAddedNotification notification, CancellationToken cancellationToken)
        {
            var metadata = notification.ServerFileHeader.FileMetadata;
            if (notification.ServerFileHeader.ServerMetadata.FileSystemType != FileSystemType.Comment
                || string.IsNullOrEmpty(metadata.SenderOdinId)
                || (OdinId)metadata.SenderOdinId == tenantContext.HostOdinId
                || metadata.ReferencedFile == null
                || !await IsChannelDriveAsync(notification.File.DriveId))
            {
                return;
            }

            try
            {
                await pushNotificationService.EnqueueNotification((OdinId)metadata.SenderOdinId, new AppNotificationOptions()
                    {
                        AppId = SystemAppConstants.FeedAppId,
                        TypeId = CommentNotificationTypeId,
                        TagId = metadata.ReferencedFile.GlobalTransitId,
                        Silent = false,
                    },
                    OdinContextUpgrades.UpgradeToPeerTransferContext(notification.OdinContext));
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to notify of comment {fileId} on drive {driveId}", notification.File.FileId,
                    notification.File.DriveId);
            }
        }

        public async Task Handle(NewFollowerNotification notification, CancellationToken cancellationToken)
        {
            var newContext = OdinContextUpgrades.UpgradeToPeerTransferContext(notification.OdinContext);
            await pushNotificationService.EnqueueNotification(notification.Sender,
                new AppNotificationOptions()
                {
                    AppId = SystemAppConstants.SystemAppId,
                    TypeId = notification.NotificationTypeId,
                    TagId = notification.Sender.ToHashId(),
                    Silent = false
                },
                newContext);
        }

        private async Task<bool> IsChannelDriveAsync(Guid driveId)
        {
            var drive = await driveManager.GetDriveAsync(driveId);
            return drive?.TargetDriveInfo.Type == SystemDriveConstants.ChannelDriveType;
        }

        private async Task<bool> IsFeedDriveRelatedAsync(Guid driveId)
        {
            var drive = await driveManager.GetDriveAsync(driveId);
            if (null == drive)
            {
                logger.LogDebug("notification sent with invalid driveId - this is totes rare");
                return false;
            }

            return drive.TargetDriveInfo.Type == SystemDriveConstants.ChannelDriveType ||
                   drive.TargetDriveInfo == WellKnownAppDrives.FeedDrive;
        }
    }
}
