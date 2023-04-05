using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.DataSubscription.SendingHost;
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
    public class FeedDriveDistributionRouter : INotificationHandler<IDriveNotification>
    {
        private readonly GuidId _feedItemCategory = GuidId.FromString("feed_items");
        private readonly GuidId _metadataFeedItemCategory = GuidId.FromString("header_feed_item");
        private readonly GuidId _reactionPreviewFeedItemCategory = GuidId.FromString("reaction_preview_feed_item");
        private readonly FollowerService _followerService;
        private readonly DriveManager _driveManager;
        private readonly ITransitService _transitService;
        private readonly TenantContext _tenantContext;
        private readonly ServerSystemStorage _serverSystemStorage;
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly ITenantSystemStorage _tenantSystemStorage;
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ICircleNetworkService _circleNetworkService;
        private readonly FeedDistributorService _feedDistributorService;
        private readonly YouverseConfiguration _youverseConfiguration;

        /// <summary>
        /// Routes file changes to drives which allow subscriptions to be sent in a background process
        /// </summary>
        public FeedDriveDistributionRouter(
            FollowerService followerService,
            ITransitService transitService, DriveManager driveManager, TenantContext tenantContext, ServerSystemStorage serverSystemStorage,
            FileSystemResolver fileSystemResolver, ITenantSystemStorage tenantSystemStorage, DotYouContextAccessor contextAccessor,
            ICircleNetworkService circleNetworkService,
            IDotYouHttpClientFactory dotYouHttpClientFactory, YouverseConfiguration youverseConfiguration)
        {
            _followerService = followerService;
            _transitService = transitService;
            _driveManager = driveManager;
            _tenantContext = tenantContext;
            _serverSystemStorage = serverSystemStorage;
            _fileSystemResolver = fileSystemResolver;
            _tenantSystemStorage = tenantSystemStorage;
            _contextAccessor = contextAccessor;
            _circleNetworkService = circleNetworkService;
            _youverseConfiguration = youverseConfiguration;

            _feedDistributorService = new FeedDistributorService(fileSystemResolver, contextAccessor, dotYouHttpClientFactory, circleNetworkService);
        }

        public async Task Handle(IDriveNotification notification, CancellationToken cancellationToken)
        {
            if (await ShouldDistribute(notification))
            {
                if (_contextAccessor.GetCurrent().Caller.IsOwner)
                {
                    if (notification.ServerFileHeader.FileMetadata.PayloadIsEncrypted)
                    {
                        await this.DistributeToConnectedFollowersUsingTransit(notification);
                    }
                    else
                    {
                        await this.EnqueueFileMetadataForDistributionUsingFeedEndpoint(notification);
                    }
                }
                else
                {
                    // If this is the reaction preview being updated due to an incoming comment or reaction
                    if (notification is ReactionPreviewUpdatedNotification)
                    {
                        await EnqueueReactionPreviewForDistributionUsingFeedEndpoint(notification);
                    }
                }

                //Note: intentionally ignoring when the notification is a file and it's not the owner
            }
        }

        private async Task EnqueueFileMetadataForDistributionUsingFeedEndpoint(IDriveNotification notification)
        {
            var item = new ReactionPreviewDistributionItem()
            {
                DriveNotificationType = notification.DriveNotificationType,
                SourceFile = notification.ServerFileHeader.FileMetadata!.File,
                FileSystemType = notification.ServerFileHeader.ServerMetadata.FileSystemType
            };
            
            if (_youverseConfiguration.Feed.InstantDistribution)
            {
                await DistributeMetadata(item);
            }
            else
            {
                var fileKey = CreateFileKey(item.SourceFile);
                _tenantSystemStorage.ThreeKeyValueStorage.Upsert(fileKey, _feedItemCategory, _metadataFeedItemCategory, item);

                //tell the job this tenant needs to have their queue processed
                _serverSystemStorage.EnqueueJob(_tenantContext.HostOdinId, CronJobType.FeedReactionPreviewDistribution, new FeedDistributionInfo()
                {
                    OdinId = _tenantContext.HostOdinId,
                });
            }
        }

        private async Task<bool> ShouldDistribute(IDriveNotification notification)
        {
            //if the file was received from another identity, do not redistribute
            var sender = notification.ServerFileHeader?.FileMetadata?.SenderOdinId;
            var uploadedByThisIdentity = sender == _tenantContext.HostOdinId || string.IsNullOrEmpty(sender?.Trim());
            if (!uploadedByThisIdentity)
            {
                return false;
            }

            if (notification.ServerFileHeader == null) //file was hard-deleted
            {
                return false;
            }

            if (!notification.ServerFileHeader.ServerMetadata.AllowDistribution)
            {
                return false;
            }

            //We only distribute standard files to populate the feed.  Comments are retrieved by calls over transit query
            if (notification.ServerFileHeader.ServerMetadata.FileSystemType != FileSystemType.Standard)
            {
                return false;
            }

            if (!await SupportsSubscription(notification.File.DriveId))
            {
                return false;
            }

            return true;
        }

        public async Task DistributeMetadataItems()
        {
            var items = _tenantSystemStorage.ThreeKeyValueStorage.GetByKey2And3<ReactionPreviewDistributionItem>(_feedItemCategory, _metadataFeedItemCategory);

            foreach (var distroItem in items)
            {
                await DistributeMetadata(distroItem);
            }
        }

        public async Task DistributeReactionPreviews()
        {
            var items = _tenantSystemStorage.ThreeKeyValueStorage.GetByKey2And3<ReactionPreviewDistributionItem>(_feedItemCategory,
                _reactionPreviewFeedItemCategory);

            //TODO: this needs a Pop queue, etc.
            //TODO when happens when one fails?

            foreach (var distroItem in items)
            {
                await DistributeReactionPreview(distroItem);
            }
        }

        private async Task DistributeMetadata(ReactionPreviewDistributionItem distroItem)
        {
            //TODO: this needs a Pop queue, etc.
            //TODO when happens when one fails?

            var driveId = distroItem.SourceFile.DriveId;
            var file = distroItem.SourceFile;

            var recipients = await GetFollowers(driveId);
            if (!recipients.Any())
            {
                return;
            }

            var results = await _feedDistributorService.SendFiles(file, distroItem.FileSystemType, recipients);
            if (null != results)
            {
                var failedRecipients = recipients.Where(r =>
                    results.TryGetValue(r, out var status) &&
                    status != TransitResponseCode.AcceptedDirectWrite);

                //TODO: Process the results and put into retry queue
            }
        }

        private async Task DistributeReactionPreview(ReactionPreviewDistributionItem distroItem)
        {
            var driveId = distroItem.SourceFile.DriveId;
            var file = distroItem.SourceFile;

            var recipients = await GetFollowers(driveId);
            if (!recipients.Any())
            {
                return;
            }

            var results = await _feedDistributorService.SendReactionPreview(file, distroItem.FileSystemType, recipients);
            if (null != results)
            {
                var failedRecipients = recipients.Where(r =>
                    results.TryGetValue(r, out var status) &&
                    status != TransitResponseCode.AcceptedDirectWrite);

                //TODO: Process the results and put into retry queue
            }
        }

        /// <summary>
        /// Distributes to connected identities that are followers using
        /// transit; returns the list of unconnected identites
        /// </summary>
        private async Task<List<OdinId>> DistributeToConnectedFollowersUsingTransit(IDriveNotification notification)
        {
            var driveId = notification.File.DriveId;
            var file = notification.File;

            var recipients = await GetFollowers(driveId);
            if (!recipients.Any())
            {
                return new List<OdinId>();
            }

            //find all followers that are connected, return those which are not to be processed differently
            var connectedIdentities = await _circleNetworkService.GetCircleMembers(CircleConstants.SystemCircleId);
            var connectedRecipients = recipients.Intersect(connectedIdentities).ToList();

            if (connectedRecipients.Any())
            {
                var fs = _fileSystemResolver.ResolveFileSystem(file);
                var header = await fs.Storage.GetServerFileHeader(file);

                if (notification.DriveNotificationType == DriveNotificationType.FileDeleted)
                {
                    await DeleteFileOverTransit(header, connectedRecipients);
                }

                await SendFileOverTransit(header, connectedRecipients);
            }

            return recipients.Except(connectedRecipients).ToList();
        }

        private async Task<List<OdinId>> GetFollowers(Guid driveId)
        {
            int maxRecords = 10000; //TODO: cursor thru batches instead
            var driveFollowers = await _followerService.GetFollowers(driveId, maxRecords, cursor: "");
            var allDriveFollowers = await _followerService.GetFollowersOfAllNotifications(maxRecords, cursor: "");

            var recipients = new List<OdinId>();
            recipients.AddRange(driveFollowers.Results);
            recipients.AddRange(allDriveFollowers.Results.Except(driveFollowers.Results));

            return recipients;
        }

        private async Task SendFileOverTransit(ServerFileHeader header, List<OdinId> recipients)
        {
            var transitOptions = new TransitOptions()
            {
                Recipients = recipients.Select(r => r.DomainName).ToList(),
                Schedule = _youverseConfiguration.Feed.InstantDistribution ? ScheduleOptions.SendNowAwaitResponse : ScheduleOptions.SendLater,
                IsTransient = false,
                UseGlobalTransitId = true,
                SendContents = SendContents.Header,
                RemoteTargetDrive = SystemDriveConstants.FeedDrive
            };

            var _ = await _transitService.SendFile(
                header.FileMetadata.File,
                transitOptions,
                TransferFileType.Normal,
                header.ServerMetadata.FileSystemType);
        }

        private async Task DeleteFileOverTransit(ServerFileHeader header, List<OdinId> recipients)
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
                        ClientAccessTokenSource = ClientAccessTokenSource.Circle
                    },
                    recipients.Select(r => r.DomainName).ToList());

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

        private async Task EnqueueReactionPreviewForDistributionUsingFeedEndpoint(IDriveNotification notification)
        {
            var item = new ReactionPreviewDistributionItem()
            {
                DriveNotificationType = notification.DriveNotificationType,
                SourceFile = notification.ServerFileHeader.FileMetadata!.File,
                FileSystemType = notification.ServerFileHeader.ServerMetadata.FileSystemType
            };

            if (_youverseConfiguration.Feed.InstantDistribution)
            {
                await this.DistributeReactionPreview(item);
            }
            else
            {
                var fileKey = CreateFileKey(item.SourceFile);
                _tenantSystemStorage.ThreeKeyValueStorage.Upsert(fileKey, _feedItemCategory, _reactionPreviewFeedItemCategory, item);
                _serverSystemStorage.EnqueueJob(_tenantContext.HostOdinId, CronJobType.FeedFileDistribution, new FeedDistributionInfo()
                {
                    OdinId = _tenantContext.HostOdinId,
                });
            }
        }

        private Guid CreateFileKey(InternalDriveFileId file)
        {
            var bytes = HashUtil.ReduceSHA256Hash(ByteArrayUtil.Combine(file.FileId.ToByteArray(), file.DriveId.ToByteArray()));
            return new Guid(bytes);
        }

        private async Task<bool> SupportsSubscription(Guid driveId)
        {
            var drive = await _driveManager.GetDrive(driveId, false);
            return drive.AllowSubscriptions && drive.TargetDriveInfo.Type == SystemDriveConstants.ChannelDriveType;
        }
    }
}