using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Odin.Core.Identity;
using Odin.Core.Services.AppNotifications.ClientNotifications;
using Odin.Core.Services.AppNotifications.Push;
using Odin.Core.Services.AppNotifications.SystemNotifications;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Drives.Reactions;
using Odin.Core.Services.Mediator;
using Odin.Core.Services.Peer;
using Odin.Core.Storage;
using Serilog;

namespace Odin.Core.Services.DataSubscription
{
    /// <summary>
    /// Maps incoming notifications to the feed drive  and ensures they're sent. (this is because things like
    /// reactions are available for any drive but we want to identify a notification with an app)
    ///
    /// Yes, this is more Feed hack'ish stuff that indicates we need to build a feed subsystem in the core
    /// </summary>
    public class FeedNotificationMapper : INotificationHandler<ReactionContentAddedNotification>, INotificationHandler<NewFeedItemReceived>,
        INotificationHandler<NewFollowerNotification>, INotificationHandler<DriveFileAddedNotification>
    {
        private readonly DriveManager _driveManager;
        private readonly PushNotificationService _pushNotificationService;
        private readonly TenantContext _tenantContext;

        private static readonly Guid CommentNotificationTypeId = Guid.Parse("1e08b70a-3826-4840-8372-18410bfc02c7");

        private static readonly Guid PostNotificationTypeId = Guid.Parse("ad695388-c2df-47a0-ad5b-fc9f9e1fffc9");


        public FeedNotificationMapper(DriveManager driveManager, PushNotificationService pushNotificationService, TenantContext tenantContext)
        {
            _driveManager = driveManager;
            _pushNotificationService = pushNotificationService;
            _tenantContext = tenantContext;
        }

        public Task Handle(ReactionContentAddedNotification notification, CancellationToken cancellationToken)
        {
            var driveId = notification.Reaction.FileId.DriveId;
            if (IsFeedDriveRelated(driveId).GetAwaiter().GetResult())
            {
                var sender = (OdinId)notification.Reaction.OdinId;
                if (sender != _tenantContext.HostOdinId)
                {
                    _pushNotificationService.EnqueueNotification(sender, new AppNotificationOptions()
                    {
                        AppId = SystemAppConstants.FeedAppId,
                        TypeId = notification.NotificationTypeId,
                        TagId = sender.ToHashId(),
                        Silent = false,
                    });
                }
            }

            return Task.CompletedTask;
        }

        public Task Handle(NewFeedItemReceived notification, CancellationToken cancellationToken)
        {
            var typeId = notification.FileSystemType == FileSystemType.Comment ? CommentNotificationTypeId : PostNotificationTypeId;

            _pushNotificationService.EnqueueNotification(notification.Sender, new AppNotificationOptions()
            {
                AppId = SystemAppConstants.FeedAppId,
                TypeId = typeId,
                TagId = notification.Sender.ToHashId(),
                Silent = false,
            });

            return Task.CompletedTask;
        }

        public Task Handle(DriveFileAddedNotification notification, CancellationToken cancellationToken)
        {
            //handle comments when added to my identity from a user who's on my home page
            if (string.IsNullOrEmpty(notification.ServerFileHeader.FileMetadata.SenderOdinId))
            {
                //no need to send a notification to myself
                return Task.CompletedTask;
            }
            
            var sender =(OdinId)notification.ServerFileHeader.FileMetadata.SenderOdinId;
            
            if (notification.ServerFileHeader.ServerMetadata.FileSystemType == FileSystemType.Comment
                && sender != _tenantContext.HostOdinId)
            {
                _pushNotificationService.EnqueueNotification(sender, new AppNotificationOptions()
                {
                    AppId = SystemAppConstants.FeedAppId,
                    TypeId = CommentNotificationTypeId,
                    TagId = sender.ToHashId(),
                    Silent = false,
                });
            }

            return Task.CompletedTask;
        }

        public Task Handle(NewFollowerNotification notification, CancellationToken cancellationToken)
        {
            _pushNotificationService.EnqueueNotification(notification.OdinId, new AppNotificationOptions()
            {
                AppId = SystemAppConstants.OwnerAppId,
                TypeId = notification.NotificationTypeId,
                TagId = notification.OdinId.ToHashId(),
                Silent = false
            });

            return Task.CompletedTask;
        }

        private async Task<bool> IsFeedDriveRelated(Guid driveId)
        {
            var drive = await _driveManager.GetDrive(driveId, false);
            if (null == drive)
            {
                Log.Warning("notification sent with invalid driveId - this is totes rare");
                return false;
            }

            return drive.TargetDriveInfo.Type == SystemDriveConstants.ChannelDriveType ||
                   drive.TargetDriveInfo == SystemDriveConstants.FeedDrive;
        }
    }
}