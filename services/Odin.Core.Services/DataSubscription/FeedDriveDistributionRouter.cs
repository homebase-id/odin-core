using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.Base;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.DataSubscription.Follower;
using Odin.Core.Services.DataSubscription.SendingHost;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Mediator;
using Odin.Core.Services.Membership.Circles;
using Odin.Core.Services.Membership.Connections;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Services.DataSubscription
{
    /// <summary>
    /// Distributes files from channels to follower's feed drives (and only the feed drive)
    /// </summary>
    public class FeedDriveDistributionRouter : INotificationHandler<IDriveNotification>
    {
        private readonly FollowerService _followerService;
        private readonly DriveManager _driveManager;
        private readonly ITransitService _transitService;
        private readonly TenantContext _tenantContext;
        private readonly ServerSystemStorage _serverSystemStorage;
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly TenantSystemStorage _tenantSystemStorage;
        private readonly OdinContextAccessor _contextAccessor;
        private readonly CircleNetworkService _circleNetworkService;
        private readonly FeedDistributorService _feedDistributorService;
        private readonly OdinConfiguration _odinConfiguration;

        /// <summary>
        /// Routes file changes to drives which allow subscriptions to be sent in a background process
        /// </summary>
        public FeedDriveDistributionRouter(
            FollowerService followerService,
            ITransitService transitService, DriveManager driveManager, TenantContext tenantContext, ServerSystemStorage serverSystemStorage,
            FileSystemResolver fileSystemResolver, TenantSystemStorage tenantSystemStorage, OdinContextAccessor contextAccessor,
            CircleNetworkService circleNetworkService,
            IOdinHttpClientFactory odinHttpClientFactory, OdinConfiguration odinConfiguration)
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
            _odinConfiguration = odinConfiguration;

            _feedDistributorService = new FeedDistributorService(fileSystemResolver, odinHttpClientFactory);
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
                        await this.EnqueueFileMetadataForDistributionUsingFeedEndpoint(notification);
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
                FileSystemType = notification.ServerFileHeader.ServerMetadata.FileSystemType,
                FeedDistroType = FeedDistroType.FileMetadata
            };

            if (_odinConfiguration.Feed.InstantDistribution)
            {
                await DistributeMetadataNow(item);
            }
            else
            {
                await EnqueueFollowers(notification, item);
                EnqueueCronJob();
            }
        }

        private async Task EnqueueFollowers(IDriveNotification notification, ReactionPreviewDistributionItem item)
        {
            var recipients = await GetFollowers(notification.File.DriveId);
            if (!recipients.Any())
            {
                return;
            }

            foreach (var recipient in recipients)
            {
                AddToFeedOutbox(recipient, item);
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

        public async Task DistributeQueuedMetadataItems()
        {
            async Task<(FeedDistributionOutboxRecord record, bool success)> SendFile(FeedDistributionOutboxRecord record)
            {
                var distroItem = OdinSystemSerializer.Deserialize<ReactionPreviewDistributionItem>(record.value.ToStringFromUtf8Bytes());
                var recipient = (OdinId)record.recipient;
                bool success = await _feedDistributorService.SendFile(new InternalDriveFileId()
                    {
                        FileId = record.fileId,
                        DriveId = record.driveId
                    },
                    distroItem.FileSystemType,
                    recipient);
                return (record, success);
            }

            var batch = _tenantSystemStorage.Feedbox.Pop(_odinConfiguration.Feed.DistributionBatchSize);
            var tasks = new List<Task<(FeedDistributionOutboxRecord record, bool success)>>(batch.Select(SendFile));
            await Task.WhenAll(tasks);

            var successes = tasks.Where(t => t.Result.success).Select(t => t.Result.record.popStamp.GetValueOrDefault()).ToList();
            successes.ForEach(_tenantSystemStorage.Feedbox.PopCommitAll);
            
            var failures = tasks.Where(t => !t.Result.success).Select(t => t.Result.record.popStamp.GetValueOrDefault()).ToList();
            failures.ForEach(_tenantSystemStorage.Feedbox.PopCancelAll);
        }

        private async Task DistributeMetadataNow(ReactionPreviewDistributionItem distroItem)
        {
            var driveId = distroItem.SourceFile.DriveId;
            var file = distroItem.SourceFile;

            var recipients = await GetFollowers(driveId);
            foreach (var recipient in recipients)
            {
                var success = await _feedDistributorService.SendFile(file, distroItem.FileSystemType, recipient);
                if (!success)
                {
                    // fall back to queue
                    AddToFeedOutbox(recipient, distroItem);
                }
            }
        }

        /// <summary>
        /// Distributes to connected identities that are followers using
        /// transit; returns the list of unconnected identities
        /// </summary>
        private async Task<List<OdinId>> DistributeToConnectedFollowersUsingTransit(IDriveNotification notification)
        {
            var file = notification.File;

            var recipients = await GetFollowers(notification.File.DriveId);
            if (!recipients.Any())
            {
                return new List<OdinId>();
            }

            //find all followers that are connected, return those which are not to be processed differently
            var connectedIdentities = await _circleNetworkService.GetCircleMembers(CircleConstants.ConnectedIdentitiesSystemCircleId);
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

            var td = _contextAccessor.GetCurrent().PermissionsContext.GetTargetDrive(driveId);
            var driveFollowers = await _followerService.GetFollowers(td, maxRecords, cursor: "");
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
                Schedule = _odinConfiguration.Feed.InstantDistribution ? ScheduleOptions.SendNowAwaitResponse : ScheduleOptions.SendLater,
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

        private void EnqueueCronJob()
        {
            _serverSystemStorage.EnqueueJob(_tenantContext.HostOdinId, CronJobType.FeedDistribution, new FeedDistributionInfo()
            {
                OdinId = _tenantContext.HostOdinId,
            });
        }

        private async Task<bool> SupportsSubscription(Guid driveId)
        {
            var drive = await _driveManager.GetDrive(driveId, false);
            return drive.AllowSubscriptions && drive.TargetDriveInfo.Type == SystemDriveConstants.ChannelDriveType;
        }

        private void AddToFeedOutbox(OdinId recipient, ReactionPreviewDistributionItem item)
        {
            _tenantSystemStorage.Feedbox.Upsert(new()
            {
                recipient = recipient,
                fileId = item.SourceFile.FileId,
                driveId = item.SourceFile.DriveId,
                value = OdinSystemSerializer.Serialize(item).ToUtf8ByteArray()
            });
        }
    }
}