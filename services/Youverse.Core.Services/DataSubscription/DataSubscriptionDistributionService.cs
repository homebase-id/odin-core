using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.DataSubscription
{
    public class DataSubscriptionDistributionService : INotificationHandler<DriveFileAddedNotification>
    {
        private readonly FollowerService _followerService;
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly TenantContext _tenantContext;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IDriveService _driveService;
        private readonly ITransitService _transitService;

        public DataSubscriptionDistributionService(DotYouContextAccessor contextAccessor, ITenantSystemStorage tenantSystemStorage, ILoggerFactory loggerFactory, IMediator mediator,
            TenantContext tenantContext,
            FollowerService followerService, IDriveService driveService, ITransitService transitService)
        {
            _contextAccessor = contextAccessor;
            _loggerFactory = loggerFactory;
            _tenantContext = tenantContext;
            _followerService = followerService;
            _driveService = driveService;
            _transitService = transitService;
        }

        public async Task Handle(DriveFileAddedNotification notification, CancellationToken cancellationToken)
        {
            if (!await SupportsSubscription(notification.File.DriveId))
            {
                return;
            }

            //TODO: move this to a background thread or use ScheduleOptions.SendLater so the original call can finish
            //TODO: first store on this identities feed drive.
            //then send from their feed drive
            
            var (driveFollowers, nextCursor1) = await _followerService.GetFollowers(notification.File.DriveId, cursor: "");
            var (allDriveFollowers, nextCursor2) = await _followerService.GetFollowersOfAllNotifications(cursor: "");

            // TODO: You need to do something with the two cursors here, don't you?

            var recipients = new List<string>();
            recipients!.AddRange(driveFollowers.Results);
            recipients.AddRange(allDriveFollowers.Results.Except(driveFollowers.Results));

            //use transit? to send like normal?
            var options = new TransitOptions()
            {
                Recipients = recipients,
                Schedule = ScheduleOptions.SendNowAwaitResponse, //hmm should send later?
                IsTransient = false,
                UseGlobalTransitId = true,
                //SendContents = SendContents.Header //TODO: need to implement
                OverrideTargetDrive = SystemDriveConstants.FeedDrive
            };

            //TODO: in order to send over transit like this, the sender needs access to the feed drive
            await _transitService.SendFile(notification.File, options, TransferFileType.Normal, ClientAccessTokenSource.Follower);
        }
        
        private static readonly List<Guid> DriveTypesSupportingSubscription = new List<Guid>()
        {
            SystemDriveConstants.ChannelDriveType
        };

        private async Task<bool> SupportsSubscription(Guid driveId)
        {
            //TODO: make this a property of the drive
            var drive = await _driveService.GetDrive(driveId, false);
            return DriveTypesSupportingSubscription.Contains(drive.TargetDriveInfo.Type);
        }
    }
}