using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.DataSubscription.SendingHost;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Serilog;

namespace Odin.Services.DataSubscription
{
    /// <summary>
    /// Distributes files from channels to follower's feed drives (and only the feed drive)
    /// </summary>
    public class FeedDriveDistributionRouter : INotificationHandler<IDriveNotification>
    {
        public const string IsGroupChannel = "IsGroupChannel";

        private readonly FollowerService _followerService;
        private readonly DriveManager _driveManager;
        private readonly IPeerOutgoingTransferService _peerOutgoingTransferService;
        private readonly TenantContext _tenantContext;
        private readonly ServerSystemStorage _serverSystemStorage;
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly TenantSystemStorage _tenantSystemStorage;
        private readonly CircleNetworkService _circleNetworkService;
        private readonly FeedDistributorService _feedDistributorService;
        private readonly OdinConfiguration _odinConfiguration;
        private readonly ILogger<FeedDriveDistributionRouter> _logger;

        private readonly IDriveAclAuthorizationService _driveAcl;

        /// <summary>
        /// Routes file changes to drives which allow subscriptions to be sent in a background process
        /// </summary>
        public FeedDriveDistributionRouter(
            FollowerService followerService,
            IPeerOutgoingTransferService peerOutgoingTransferService, DriveManager driveManager, TenantContext tenantContext,
            ServerSystemStorage serverSystemStorage,
            FileSystemResolver fileSystemResolver,
            TenantSystemStorage tenantSystemStorage,
            CircleNetworkService circleNetworkService,
            IOdinHttpClientFactory odinHttpClientFactory,
            OdinConfiguration odinConfiguration,
            IDriveAclAuthorizationService driveAcl,
            ILogger<FeedDriveDistributionRouter> logger)
        {
            _followerService = followerService;
            _peerOutgoingTransferService = peerOutgoingTransferService;
            _driveManager = driveManager;
            _tenantContext = tenantContext;
            _serverSystemStorage = serverSystemStorage;
            _fileSystemResolver = fileSystemResolver;
            _tenantSystemStorage = tenantSystemStorage;
            _circleNetworkService = circleNetworkService;
            _odinConfiguration = odinConfiguration;
            _driveAcl = driveAcl;
            _logger = logger;

            _feedDistributorService = new FeedDistributorService(fileSystemResolver, odinHttpClientFactory, driveAcl, odinConfiguration);
        }

        public async Task Handle(IDriveNotification notification, CancellationToken cancellationToken)
        {
            var serverFileHeader = notification.ServerFileHeader;
            var odinContext = notification.OdinContext;
            if (await ShouldDistribute(serverFileHeader))
            {
                if (odinContext.Caller.IsOwner)
                {
                    var deleteNotification = notification as DriveFileDeletedNotification;
                    var isEncryptedFile =
                        (deleteNotification != null &&
                         deleteNotification.PreviousServerFileHeader.FileMetadata.IsEncrypted) ||
                        notification.ServerFileHeader.FileMetadata.IsEncrypted;

                    if (isEncryptedFile)
                    {
                        await this.DistributeToConnectedFollowersUsingTransit(notification);
                    }
                    else
                    {
                        await this.EnqueueFileMetadataNotificationForDistributionUsingFeedEndpoint(notification);
                    }
                }
                else
                {
                    // If this is the reaction preview being updated due to an incoming comment or reaction
                    if (notification is ReactionPreviewUpdatedNotification)
                    {
                        await this.EnqueueFileMetadataNotificationForDistributionUsingFeedEndpoint(notification);
                    }

                    var drive = await _driveManager.GetDrive(notification.File.DriveId);
                    if (drive.Attributes.TryGetValue(IsGroupChannel, out string value) && bool.TryParse(value, out bool isGroupChannel) && isGroupChannel)
                    {
                        await this.DistributeToConnectedFollowersUsingTransit(notification);
                    }
                }

                //Note: intentionally ignoring when the notification is a file and it's not the owner
            }
        }

        private async Task EnqueueFileMetadataNotificationForDistributionUsingFeedEndpoint(
            IDriveNotification notification)
        {
            var item = new ReactionPreviewDistributionItem()
            {
                DriveNotificationType = notification.DriveNotificationType,
                SourceFile = notification.ServerFileHeader.FileMetadata!.File,
                FileSystemType = notification.ServerFileHeader.ServerMetadata.FileSystemType,
                FeedDistroType = FeedDistroType.FileMetadata
            };


            var newContext = OdinContextUpgrades.UpgradeToReadFollowersForDistribution(notification.OdinContext);
            {
                await EnqueueFollowers(notification, item, newContext);
                EnqueueCronJob();
            }
        }

        private async Task EnqueueFollowers(IDriveNotification notification, ReactionPreviewDistributionItem item, IOdinContext odinContext)
        {
            var recipients = await GetFollowers(notification.File.DriveId, odinContext);
            if (!recipients.Any())
            {
                return;
            }

            foreach (var recipient in recipients)
            {
                AddToFeedOutbox(recipient, item);
            }
        }

        private async Task<bool> ShouldDistribute(ServerFileHeader serverFileHeader)
        {
            //if the file was received from another identity, do not redistribute
            var sender = serverFileHeader?.FileMetadata?.SenderOdinId;
            var uploadedByThisIdentity = sender == _tenantContext.HostOdinId || string.IsNullOrEmpty(sender?.Trim());
            if (!uploadedByThisIdentity)
            {
                return false;
            }

            if (serverFileHeader == null) //file was hard-deleted
            {
                return false;
            }

            if (!serverFileHeader.ServerMetadata.AllowDistribution)
            {
                return false;
            }

            //We only distribute standard files to populate the feed.  Comments are retrieved by calls over transit query
            if (serverFileHeader.ServerMetadata.FileSystemType != FileSystemType.Standard)
            {
                return false;
            }

            if (!await SupportsSubscription(serverFileHeader.FileMetadata!.File.DriveId))
            {
                return false;
            }

            return true;
        }

        public async Task DistributeQueuedMetadataItems(IOdinContext odinContext)
        {
            async Task<(FeedDistributionOutboxRecord record, bool success)> HandleFileUpdates(FeedDistributionOutboxRecord record)
            {
                var distroItem = OdinSystemSerializer.Deserialize<ReactionPreviewDistributionItem>(record.value.ToStringFromUtf8Bytes());
                var recipient = (OdinId)record.recipient;
                if (distroItem.DriveNotificationType is DriveNotificationType.FileAdded or DriveNotificationType.FileModified)
                {
                    bool success = await _feedDistributorService.SendFile(new InternalDriveFileId()
                        {
                            FileId = record.fileId,
                            DriveId = record.driveId
                        },
                        distroItem.FileSystemType,
                        recipient,
                        odinContext);

                    return (record, success);
                }

                if (distroItem.DriveNotificationType == DriveNotificationType.FileDeleted)
                {
                    var success = await _feedDistributorService.DeleteFile(new InternalDriveFileId()
                        {
                            FileId = record.fileId,
                            DriveId = record.driveId
                        },
                        distroItem.FileSystemType,
                        recipient,
                        odinContext);
                    return (record, success);
                }

                //Note: not throwing exception so we dont block other-valid feed items from being sent
                Log.Warning($"Unhandled Notification Type {distroItem.DriveNotificationType}");
                return (record, false);
            }

            var batch = _tenantSystemStorage.Feedbox.Pop(_odinConfiguration.Feed.DistributionBatchSize);
            var tasks = new List<Task<(FeedDistributionOutboxRecord record, bool success)>>(batch.Select(HandleFileUpdates));
            await Task.WhenAll(tasks);

            var successes = tasks.Where(t => t.Result.success)
                .Select(t => t.Result.record.popStamp.GetValueOrDefault()).ToList();
            successes.ForEach(_tenantSystemStorage.Feedbox.PopCommitAll);

            var failures = tasks.Where(t => !t.Result.success)
                .Select(t => t.Result.record.popStamp.GetValueOrDefault()).ToList();
            failures.ForEach(_tenantSystemStorage.Feedbox.PopCancelAll);
        }

        /// <summary>
        /// Distributes to connected identities that are followers using
        /// transit; returns the list of unconnected identities
        /// </summary>
        private async Task DistributeToConnectedFollowersUsingTransit(IDriveNotification notification)
        {
            var file = notification.File;
            var odinContext = notification.OdinContext;
            var followers = await GetFollowers(notification.File.DriveId, odinContext);
            if (!followers.Any())
            {
                return;
            }

            //find all followers that are connected, return those which are not to be processed differently
            var connectedIdentities = await _circleNetworkService.GetCircleMembers(SystemCircleConstants.ConnectedIdentitiesSystemCircleId, odinContext);
            var connectedFollowers = followers.Intersect(connectedIdentities)
                .Where(cf => _driveAcl.IdentityHasPermission(
                        (OdinId)cf.DomainName,
                        notification.ServerFileHeader.ServerMetadata.AccessControlList,
                        odinContext)
                    .GetAwaiter().GetResult()).ToList();

            if (connectedFollowers.Any())
            {
                var fs = await _fileSystemResolver.ResolveFileSystem(file, odinContext);
                var header = await fs.Storage.GetServerFileHeader(file, odinContext);

                if (notification.DriveNotificationType == DriveNotificationType.FileDeleted)
                {
                    await DeleteFileOverTransit(header, connectedFollowers, odinContext);
                }

                await SendFileOverTransit(header, connectedFollowers, odinContext);
            }

            // return followers.Except(connectedFollowers).ToList();
        }

        private async Task<List<OdinId>> GetFollowers(Guid driveId, IOdinContext odinContext)
        {
            int maxRecords = 100000; //TODO: cursor thru batches instead

            //
            // Get followers for this drive and merge with followers who want everything
            //
            var td = odinContext.PermissionsContext.GetTargetDrive(driveId);
            var driveFollowers = await _followerService.GetFollowers(td, maxRecords, cursor: "", odinContext);
            var allDriveFollowers = await _followerService.GetFollowersOfAllNotifications(maxRecords, cursor: "", odinContext);

            var recipients = new List<OdinId>();
            recipients.AddRange(driveFollowers.Results);
            recipients.AddRange(allDriveFollowers.Results.Except(driveFollowers.Results));

            return recipients;
        }

        private async Task SendFileOverTransit(ServerFileHeader header, List<OdinId> recipients, IOdinContext odinContext)
        {
            var file = header.FileMetadata.File;

            var transitOptions = new TransitOptions()
            {
                Recipients = recipients.Select(r => r.DomainName).ToList(),
                Schedule = ScheduleOptions.SendLater,
                IsTransient = false,
                UseGlobalTransitId = true,
                SendContents = SendContents.Header,
                RemoteTargetDrive = SystemDriveConstants.FeedDrive
            };

            var transferStatusMap = await _peerOutgoingTransferService.SendFile(
                file,
                transitOptions,
                TransferFileType.Normal,
                header.ServerMetadata.FileSystemType,
                odinContext);

            //Log warnings if, for some reason, transit does not create transfer keys
            foreach (var recipient in recipients)
            {
                if (transferStatusMap.TryGetValue(recipient, out var status))
                {
                    if (status != TransferStatus.TransferKeyCreated)
                    {
                        _logger.LogError(
                            "Feed Distribution Router result - {recipient} returned status was [{status}] but should have been TransferKeyCreated " +
                            "for fileId [{fileId}] on drive [{driveId}]", recipient, status, file.FileId, file.DriveId);
                    }
                }
                else
                {
                    // this should not happen
                    _logger.LogError("No transfer status found for recipient [{recipient}] for fileId [{fileId}] on [{drive}]", recipient, file.FileId,
                        file.DriveId);
                }
            }
        }

        private async Task DeleteFileOverTransit(ServerFileHeader header, List<OdinId> recipients, IOdinContext odinContext)
        {
            if (header.FileMetadata.GlobalTransitId.HasValue)
            {
                //send the deleted file
                var map = await _peerOutgoingTransferService.SendDeleteFileRequest(
                    new GlobalTransitIdFileIdentifier()
                    {
                        TargetDrive = SystemDriveConstants.FeedDrive,
                        GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                    },
                    fileTransferOptions: new FileTransferOptions()
                    {
                        FileSystemType = header.ServerMetadata.FileSystemType,
                        TransferFileType = TransferFileType.Normal,
                    },
                    recipients.Select(r => r.DomainName).ToList(),
                    odinContext);

                //TODO: how to handle map?

                foreach (var (recipient, status) in map)
                {
                    if (status == DeleteLinkedFileStatus.RemoteServerFailed)
                    {
                        //TODO: How to handle this in feed distributor?
                        //the issue is that we have no fall back queue.
                    }
                }
            }
        }

        private void EnqueueCronJob()
        {
            _serverSystemStorage.EnqueueJob(_tenantContext.HostOdinId, CronJobType.FeedDistribution,
                new FeedDistributionInfo()
                {
                    OdinId = _tenantContext.HostOdinId,
                },
                UnixTimeUtc.Now());
        }

        private async Task<bool> SupportsSubscription(Guid driveId)
        {
            var drive = await _driveManager.GetDrive(driveId);
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