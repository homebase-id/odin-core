using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
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
        DriveManager driveManager,
        PushNotificationService pushNotificationService,
        TenantContext tenantContext)
        : INotificationHandler<ReactionContentAddedNotification>, INotificationHandler<NewFeedItemReceived>,
            INotificationHandler<NewFollowerNotification>, INotificationHandler<DriveFileAddedNotification>
    {
        private static readonly Guid CommentNotificationTypeId = Guid.Parse("1e08b70a-3826-4840-8372-18410bfc02c7");
        private static readonly Guid PostNotificationTypeId = Guid.Parse("ad695388-c2df-47a0-ad5b-fc9f9e1fffc9");

        public async Task Handle(ReactionContentAddedNotification notification, CancellationToken cancellationToken)
        {
            var driveId = notification.Reaction.FileId.DriveId;
            if (await IsFeedDriveRelated(driveId, notification.db))
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
                        newContext,
                        notification.db);
                }
            }
        }

        public async Task Handle(NewFeedItemReceived notification, CancellationToken cancellationToken)
        {
            var typeId = notification.FileSystemType == FileSystemType.Comment ? CommentNotificationTypeId : PostNotificationTypeId;
            var odinContext = notification.OdinContext;
            var newContext = OdinContextUpgrades.UpgradeToPeerTransferContext(odinContext);

            await pushNotificationService.EnqueueNotification(notification.Sender, new AppNotificationOptions()
            {
                AppId = SystemAppConstants.FeedAppId,
                TypeId = typeId,
                TagId = notification.GlobalTransitId,
                Silent = false,
            },
                newContext,
                notification.db);
        }

        public async Task Handle(DriveFileAddedNotification notification, CancellationToken cancellationToken)
        {
            //handle comments when added to my identity from a user who's on my home page
            if (string.IsNullOrEmpty(notification.ServerFileHeader.FileMetadata.SenderOdinId))
            {
                //no need to send a notification to myself
                return;
            }

            var sender = (OdinId)notification.ServerFileHeader.FileMetadata.SenderOdinId;

            if (notification.ServerFileHeader.ServerMetadata.FileSystemType == FileSystemType.Comment
                && sender != tenantContext.HostOdinId)
            {
                var odinContext = notification.OdinContext;
                var newContext = OdinContextUpgrades.UpgradeToPeerTransferContext(odinContext);
                await pushNotificationService.EnqueueNotification(sender, new AppNotificationOptions()
                {
                    AppId = SystemAppConstants.FeedAppId,
                    TypeId = CommentNotificationTypeId,
                    TagId = notification.ServerFileHeader.FileMetadata.ReferencedFile != null ? notification.ServerFileHeader.FileMetadata.ReferencedFile.GlobalTransitId : notification.ServerFileHeader.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                    Silent = false,
                },
                    newContext,
                    notification.db);
            }
        }

        public async Task Handle(NewFollowerNotification notification, CancellationToken cancellationToken)
        {
            var newContext = OdinContextUpgrades.UpgradeToPeerTransferContext(notification.OdinContext);
            await pushNotificationService.EnqueueNotification(notification.Sender,
                new AppNotificationOptions()
                {
                    AppId = SystemAppConstants.OwnerAppId,
                    TypeId = notification.NotificationTypeId,
                    TagId = notification.Sender.ToHashId(),
                    Silent = false
                },
                newContext,
                notification.db);
        }

        private async Task<bool> IsFeedDriveRelated(Guid driveId, IdentityDatabase db)
        {
            var drive = await driveManager.GetDrive(driveId, db);
            if (null == drive)
            {
                logger.LogWarning("notification sent with invalid driveId - this is totes rare");
                return false;
            }

            return drive.TargetDriveInfo.Type == SystemDriveConstants.ChannelDriveType ||
                   drive.TargetDriveInfo == SystemDriveConstants.FeedDrive;
        }
    }
}
