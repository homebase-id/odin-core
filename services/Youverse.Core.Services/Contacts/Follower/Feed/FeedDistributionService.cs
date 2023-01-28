using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Contacts.Follower.Feed
{
    // Note: drive storage using the ThreeKey KeyValueDatabase
    // key1 = drive id
    // key2 = drive type  + drive alias (see TargetDrive.ToKey() method)
    // key3 = type of data identifier (the fact this is a drive; note: we should put datatype on the KV database)

    public class FeedDistributionService : INotificationHandler<DriveFileAddedNotification>
    {
        private readonly FollowerService _followerService;
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly TenantContext _tenantContext;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IDriveService _driveService;
        private readonly ITransitService _transitService;

        public FeedDistributionService(DotYouContextAccessor contextAccessor, ITenantSystemStorage tenantSystemStorage, ILoggerFactory loggerFactory, IMediator mediator, TenantContext tenantContext,
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
            //TODO: move this to a back ground thread or use ScheduleOptions.SendLater so the original call can finish

            //TODO: first store on this identities feed drive.
            //then send from their feed drive
            
            var driveFollowers = await _followerService.GetFollowers(notification.File.DriveId, "");
            var allDriveFollowers = await _followerService.GetFollowersOfAllNotifications("");

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
                SendContents = SendContents.Header
            };

            //TODO: in order to send over transit like this, the sender needs access to the feed drive
            // this means we need to grant write access to the feed drive when I follow you.
            // this just got super complex because we only grant write access to connections -_-
            await _transitService.SendFile(notification.File, options, TransferFileType.Normal);
            
            
        }
    }
}