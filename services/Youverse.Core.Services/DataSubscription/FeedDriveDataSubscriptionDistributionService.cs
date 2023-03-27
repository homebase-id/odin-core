using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.Drives.Management;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Core.Storage;
using Youverse.Core.Util;

namespace Youverse.Core.Services.DataSubscription
{
    /// <summary>
    /// Distributes files from channels to follower's feed drives (and only the feed drive)
    /// </summary>
    public class FeedDriveDataSubscriptionDistributionService : INotificationHandler<IDriveNotification>
    {
        private readonly GuidId _feedItemCategory = GuidId.FromString("feed_items");
        private readonly FollowerService _followerService;
        private readonly DriveManager _driveManager;
        private readonly ITransitService _transitService;
        private readonly TenantContext _tenantContext;
        private readonly ServerSystemStorage _serverSystemStorage;
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly ITenantSystemStorage _tenantSystemStorage;

        public FeedDriveDataSubscriptionDistributionService(
            FollowerService followerService,
            ITransitService transitService, DriveManager driveManager, TenantContext tenantContext, ServerSystemStorage serverSystemStorage,
            FileSystemResolver fileSystemResolver, ITenantSystemStorage tenantSystemStorage)
        {
            _followerService = followerService;
            _transitService = transitService;
            _driveManager = driveManager;
            _tenantContext = tenantContext;
            _serverSystemStorage = serverSystemStorage;
            _fileSystemResolver = fileSystemResolver;
            _tenantSystemStorage = tenantSystemStorage;
        }

        public async Task Handle(IDriveNotification notification, CancellationToken cancellationToken)
        {
            //if the file was received from another identity, do not redistribute

            //TODO: need to move this distribution to a background thread so that the system runs it

            var sender = notification.ServerFileHeader?.FileMetadata?.SenderOdinId;
            var uploadedByThisIdentity = sender == _tenantContext.HostOdinId || string.IsNullOrEmpty(sender?.Trim());
            if (!uploadedByThisIdentity)
            {
                return;
            }

            if (notification.ServerFileHeader == null) //file was hard-deleted
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

            //store an item in the queue to be processed
            var item = new FeedDistributionItem()
            {
                DriveNotificationType = notification.DriveNotificationType,
                File = notification.ServerFileHeader.FileMetadata!.File
            };

            await DistributeFeedItem(item);
                
            // var bytes = HashUtil.ReduceSHA256Hash(ByteArrayUtil.Combine(item.File.FileId.ToByteArray(), item.File.DriveId.ToByteArray()));
            // var key = new Guid(bytes);
            // _tenantSystemStorage.ThreeKeyValueStorage.Upsert(key, _feedItemCategory, null, item);
            //
            // //tell the job we this tenant needs to have their queue processed
            // var jobInfo = new FeedDistributionInfo()
            // {
            //     OdinId = _tenantContext.HostOdinId,
            // };
            // _serverSystemStorage.EnqueueJob(_tenantContext.HostOdinId, CronJobType.FeedDistribution,
            //     DotYouSystemSerializer.Serialize(jobInfo).ToUtf8ByteArray());
        }

        public async Task DistributeItems()
        {
            var items = _tenantSystemStorage.ThreeKeyValueStorage.GetByKey2<FeedDistributionItem>(_feedItemCategory);

            //TODO: consider background thread
            foreach (var item in items)
            {
                //TODO: this needs a Pop queue, etc.
                //TODO when happens when one fails?
                await this.DistributeFeedItem(item);
            }
        }

        public async Task DistributeFeedItem(FeedDistributionItem distroInfo)
        {
            var driveId = distroInfo.File.DriveId;

            int maxRecords = 10000; //TODO: cursor thru batches instead
            var driveFollowers = await _followerService.GetFollowers(driveId, maxRecords, cursor: "");
            var allDriveFollowers = await _followerService.GetFollowersOfAllNotifications(maxRecords, cursor: "");

            var recipients = new List<string>();
            recipients.AddRange(driveFollowers.Results);
            recipients.AddRange(allDriveFollowers.Results.Except(driveFollowers.Results));

            if (!recipients.Any())
            {
                return;
            }

            var fs = _fileSystemResolver.ResolveFileSystem(distroInfo.File);
            var header = await fs.Storage.GetServerFileHeader(distroInfo.File);

            if (distroInfo.DriveNotificationType == DriveNotificationType.FileDeleted)
            {
                await HandleFileDeleted(header, recipients);
            }

            await HandleFileAddOrUpdate(header, recipients);
        }

        private async Task HandleFileDeleted(ServerFileHeader header, List<string> recipients)
        {
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

        private async Task HandleFileAddOrUpdate(ServerFileHeader header, List<string> recipients)
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
                header.FileMetadata.File,
                transitOptions,
                TransferFileType.Normal,
                header.ServerMetadata.FileSystemType,
                ClientAccessTokenSource.Follower);
        }

        private async Task<bool> SupportsSubscription(Guid driveId)
        {
            var drive = await _driveManager.GetDrive(driveId, false);
            return drive.AllowSubscriptions;
        }
    }
}