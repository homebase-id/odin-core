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
using Odin.Core.Services.Peer;
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
        INotificationHandler<NewFollowerNotification>
    {
        private readonly DriveManager _driveManager;
        private readonly PushNotificationService _pushNotificationService;
        private readonly TenantContext _tenantContext;

        public FeedNotificationMapper(DriveManager driveManager, PushNotificationService pushNotificationService, TenantContext tenantContext)
        {
            _driveManager = driveManager;
            _pushNotificationService = pushNotificationService;
            _tenantContext = tenantContext;
        }

        public Task Handle(ReactionContentAddedNotification notification, CancellationToken cancellationToken)
        {
            if (IsFeedDriveRelated(notification).GetAwaiter().GetResult())
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
            _pushNotificationService.EnqueueNotification(notification.Sender, new AppNotificationOptions()
            {
                AppId = SystemAppConstants.FeedAppId,
                TypeId = notification.NotificationTypeId,
                TagId = notification.Sender.ToHashId(),
                Silent = false,
            });

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

        private async Task<bool> IsFeedDriveRelated(ReactionContentAddedNotification notification)
        {
            var driveId = notification.Reaction.FileId.DriveId;
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
