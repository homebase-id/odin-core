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
using Odin.Services.Authorization.Acl;
using Odin.Services.Background;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Management;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Mediator;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Services.DataSubscription
{
    /// <summary>
    /// Distributes files from channels to follower's feed drives (and only the feed drive)
    /// </summary>
    public class FeedDriveDistributionRouter : INotificationHandler<IDriveNotification>
    {
        private readonly FollowerService _followerService;
        private readonly DriveManager _driveManager;
        private readonly PeerOutgoingTransferService _peerOutgoingTransferService;
        private readonly TenantContext _tenantContext;
        private readonly CircleNetworkService _circleNetworkService;
        private readonly ILogger<FeedDriveDistributionRouter> _logger;
        private readonly PublicPrivateKeyService _pkService;
        private readonly IBackgroundServiceTrigger<PeerOutboxProcessorBackgroundService> _backgroundServiceTrigger;
        private readonly PeerOutbox _peerOutbox;

        private readonly IDriveAclAuthorizationService _driveAcl;

        /// <summary>
        /// Routes file changes to drives which allow subscriptions to be sent in a background process
        /// </summary>
        public FeedDriveDistributionRouter(
            FollowerService followerService,
            PeerOutgoingTransferService peerOutgoingTransferService,
            DriveManager driveManager,
            TenantContext tenantContext,
            CircleNetworkService circleNetworkService,
            IDriveAclAuthorizationService driveAcl,
            ILogger<FeedDriveDistributionRouter> logger,
            PublicPrivateKeyService pkService,
            IBackgroundServiceTrigger<PeerOutboxProcessorBackgroundService> backgroundServiceTrigger,
            PeerOutbox peerOutbox)
        {
            _followerService = followerService;
            _peerOutgoingTransferService = peerOutgoingTransferService;
            _driveManager = driveManager;
            _tenantContext = tenantContext;
            _circleNetworkService = circleNetworkService;
            _driveAcl = driveAcl;
            _logger = logger;
            _pkService = pkService;
            _backgroundServiceTrigger = backgroundServiceTrigger;
            _peerOutbox = peerOutbox;
        }

        public async Task Handle(IDriveNotification notification, CancellationToken cancellationToken)
        {
            var odinContext = notification.OdinContext;

            var drive = await _driveManager.GetDriveAsync(notification.File.DriveId);
            var isCollaborationChannel = drive.IsCollaborationDrive();

            if (await ShouldDistribute(notification, isCollaborationChannel))
            {
                var deleteNotification = notification as DriveFileDeletedNotification;
                var isEncryptedFile =
                    (deleteNotification != null &&
                     deleteNotification.PreviousServerFileHeader.FileMetadata.IsEncrypted) ||
                    notification.ServerFileHeader.FileMetadata.IsEncrypted;

                if (odinContext.Caller.IsOwner)
                {
                    if (isEncryptedFile)
                    {
                        await this.DistributeToConnectedFollowersUsingTransit(notification, notification.OdinContext);
                    }
                    else
                    {
                        await this.EnqueueFileMetadataNotificationForDistributionUsingFeedEndpoint(notification);
                    }

                    _backgroundServiceTrigger.PulseBackgroundProcessor();
                }
                else
                {
                    try
                    {
                        if (isCollaborationChannel)
                        {
                            var upgradedContext = OdinContextUpgrades.UpgradeToNonOwnerFeedDistributor(notification.OdinContext);
                            await DistributeToCollaborativeChannelMembers(notification, upgradedContext);
                            _backgroundServiceTrigger.PulseBackgroundProcessor();
                            return;
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "[Experimental support] Failed while DistributeToCollaborativeChannelMembers.");
#if DEBUG
                        throw;
#else
                        return;
#endif
                    }

                    // If this is the reaction preview being updated due to an incoming comment or reaction
                    if (notification is ReactionPreviewUpdatedNotification)
                    {
                        await this.EnqueueFileMetadataNotificationForDistributionUsingFeedEndpoint(notification);
                        _backgroundServiceTrigger.PulseBackgroundProcessor();
                    }
                }
            }
        }

        private async Task EnqueueFileMetadataNotificationForDistributionUsingFeedEndpoint(IDriveNotification notification)
        {
            var item = new FeedDistributionItem()
            {
                DriveNotificationType = notification.DriveNotificationType,
                SourceFile = notification.ServerFileHeader.FileMetadata!.File,
                FileSystemType = notification.ServerFileHeader.ServerMetadata.FileSystemType,
                FeedDistroType = FeedDistroType.Normal
            };

            var newContext = OdinContextUpgrades.UpgradeToReadFollowersForDistribution(notification.OdinContext);
            {
                var recipients = await GetFollowersAsync(notification.File.DriveId, newContext);
                foreach (var recipient in recipients)
                {
                    await AddToFeedOutbox(recipient, item);
                }
            }
        }

        private async Task<bool> ShouldDistribute(IDriveNotification notification, bool isCollaborationChannel)
        {
            if (notification.IgnoreFeedDistribution)
            {
                return false;
            }

            //if the file was received from another identity, do not redistribute
            var serverFileHeader = notification.ServerFileHeader;
            var sender = serverFileHeader?.FileMetadata?.SenderOdinId;
            var uploadedByThisIdentity = sender == _tenantContext.HostOdinId || string.IsNullOrEmpty(sender?.Trim());
            if (!uploadedByThisIdentity && !isCollaborationChannel)
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

        private async Task DistributeToCollaborativeChannelMembers(IDriveNotification notification, IOdinContext odinContext)
        {
            var header = notification.ServerFileHeader;

            var connectedFollowers = await GetConnectedFollowersWithFilePermissionAsync(notification, odinContext);
            if (connectedFollowers.Any())
            {
                // Prepare the file
                var payload = new FeedItemPayload()
                {
                    DriveOriginWasCollaborative = true
                    // CollaborationChannelAuthor = notification.OdinContext.GetCallerOdinIdOrFail(),
                };

                if (header.FileMetadata.IsEncrypted)
                {
                    var storageKey = odinContext.PermissionsContext.GetDriveStorageKey(header.FileMetadata.File.DriveId);
                    var keyHeader = header.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);
                    payload.KeyHeaderBytes = keyHeader.Combine().GetKey();
                }

                foreach (var recipient in connectedFollowers)
                {
                    var encryptedPayload = await _pkService.EccEncryptPayloadForRecipientAsync(
                        PublicPrivateKeyType.OfflineKey,
                        recipient,
                        OdinSystemSerializer.Serialize(payload).ToUtf8ByteArray());

                    await AddToFeedOutbox(recipient, new FeedDistributionItem()
                        {
                            DriveNotificationType = notification.DriveNotificationType,
                            SourceFile = notification.ServerFileHeader.FileMetadata!.File,
                            FileSystemType = notification.ServerFileHeader.ServerMetadata.FileSystemType,
                            FeedDistroType = FeedDistroType.CollaborativeChannel,
                            EncryptedPayload = encryptedPayload
                        }
                    );
                }
            }
        }

        /// <summary>
        /// Distributes to connected identities that are followers using
        /// transit; returns the list of unconnected identities
        /// </summary>
        private async Task DistributeToConnectedFollowersUsingTransit(IDriveNotification notification, IOdinContext odinContext)
        {
            var connectedFollowers = await GetConnectedFollowersWithFilePermissionAsync(notification, odinContext);
            if (connectedFollowers.Any())
            {
                if (notification.DriveNotificationType == DriveNotificationType.FileDeleted)
                {
                    var deletedFileNotification = (DriveFileDeletedNotification)notification;
                    if (!deletedFileNotification.IsHardDelete)
                    {
                        await DeleteFileOverTransit(notification.ServerFileHeader, connectedFollowers, odinContext);
                    }
                }
                else
                {
                    await SendFileOverTransit(notification.ServerFileHeader, connectedFollowers, odinContext);
                }
            }

            // return followers.Except(connectedFollowers).ToList();
        }

        private async Task<List<OdinId>> GetFollowersAsync(Guid driveId, IOdinContext odinContext)
        {
            int maxRecords = 100000; //TODO: cursor thru batches instead

            //
            // Get followers for this drive and merge with followers who want everything
            //
            var td = odinContext.PermissionsContext.GetTargetDrive(driveId);
            var driveFollowers = await _followerService.GetFollowersAsync(td, maxRecords, cursor: "", odinContext);
            var allDriveFollowers = await _followerService.GetFollowersOfAllNotificationsAsync(maxRecords, cursor: "", odinContext);

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
                SendContents = SendContents.Header,
                RemoteTargetDrive = SystemDriveConstants.FeedDrive
            };

            var transferStatusMap = await _peerOutgoingTransferService.SendFile(
                file,
                transitOptions,
                TransferFileType.EncryptedFileForFeedViaTransit,
                header.ServerMetadata.FileSystemType,
                odinContext);

            //Log warnings if, for some reason, transit does not create transfer keys
            foreach (var recipient in recipients)
            {
                if (transferStatusMap.TryGetValue(recipient, out var status))
                {
                    if (status != TransferStatus.Enqueued)
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
                        GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                        TargetDrive = SystemDriveConstants.FeedDrive
                    },
                    fileTransferOptions: new FileTransferOptions()
                    {
                        FileSystemType = header.ServerMetadata.FileSystemType,
                        TransferFileType = TransferFileType.Normal,
                    },
                    recipients.Select(r => r.DomainName).ToList(),
                    odinContext);

                foreach (var (recipient, status) in map)
                {
                    if (status == DeleteLinkedFileStatus.EnqueueFailed)
                    {
                        _logger.LogDebug("Enqueuing failed for recipient: {recipient}", recipient);
                    }
                }
            }
        }

        private async Task<bool> SupportsSubscription(Guid driveId)
        {
            var drive = await _driveManager.GetDriveAsync(driveId);
            return drive.AllowSubscriptions && drive.TargetDriveInfo.Type == SystemDriveConstants.ChannelDriveType;
        }

        private async Task AddToFeedOutbox(OdinId recipient, FeedDistributionItem distroItem)
        {
            var item = new OutboxFileItem()
            {
                Recipient = recipient,
                File = distroItem.SourceFile,
                Priority = 100,
                Type = OutboxItemType.UnencryptedFeedItem,
                State = new OutboxItemState()
                {
                    Data = OdinSystemSerializer.Serialize(distroItem).ToUtf8ByteArray()
                }
            };

            await _peerOutbox.AddItemAsync(item, useUpsert: true);
        }

        private async Task<List<OdinId>> GetConnectedFollowersWithFilePermissionAsync(IDriveNotification notification, IOdinContext odinContext)
        {
            var followers = await GetFollowersAsync(notification.File.DriveId, odinContext);
            if (!followers.Any())
            {
                return [];
            }
            
            // find all followers that are connected, return those which are not to be processed differently
            var connectedIdentities = await _circleNetworkService.GetCircleMembersAsync(SystemCircleConstants.ConfirmedConnectionsCircleId, odinContext);
            
            // NOTE!
            // 
            // ChatGPT has refactored the original code below to run asynchronously.
            //
            // var connectedFollowers = followers.Intersect(connectedIdentities)
            //     .Where(cf => _driveAcl.IdentityHasPermissionAsync(
            //             (OdinId)cf.DomainName,
            //             notification.ServerFileHeader.ServerMetadata.AccessControlList,
            //             odinContext,
            //             db)
            //         .GetAwaiter().GetResult()).ToList();
            // return connectedFollowers;
            
            //
            // ChatGPT from here:
            //

            // Find the intersection of followers and connected identities
            var intersectedFollowers = followers.Intersect(connectedIdentities).ToList();

            // Prepare a list of tasks to check permissions asynchronously
            var permissionTasks = intersectedFollowers.Select(async follower => new
            {
                OdinId = (OdinId)follower.DomainName,
                HasPermission = await _driveAcl.IdentityHasPermissionAsync(
                    (OdinId)follower.DomainName,
                    notification.ServerFileHeader.ServerMetadata.AccessControlList,
                    odinContext)
            }).ToList();

            // Await all permission checks concurrently
            var permissionResults = await Task.WhenAll(permissionTasks);

            // Filter and select the followers who have the necessary permissions
            var connectedFollowers = permissionResults
                .Where(result => result.HasPermission)
                .Select(result => result.OdinId)
                .ToList();

            return connectedFollowers;
        }
    }
}