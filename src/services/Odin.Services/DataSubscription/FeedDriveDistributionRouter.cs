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
using Odin.Core.Storage.SQLite;
using Odin.Services.Authorization.Acl;
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
        public const string IsCollaborativeChannel = "IsCollaborativeChannel";

        private readonly FollowerService _followerService;
        private readonly DriveManager _driveManager;
        private readonly PeerOutgoingTransferService _peerOutgoingTransferService;
        private readonly TenantContext _tenantContext;
        private readonly CircleNetworkService _circleNetworkService;
        private readonly ILogger<FeedDriveDistributionRouter> _logger;
        private readonly PublicPrivateKeyService _pkService;
        private readonly PeerOutboxProcessorBackgroundService _peerOutboxProcessorBackgroundService;
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
            PeerOutboxProcessorBackgroundService peerOutboxProcessorBackgroundService,
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
            _peerOutboxProcessorBackgroundService = peerOutboxProcessorBackgroundService;
            _peerOutbox = peerOutbox;
        }

        public async Task Handle(IDriveNotification notification, CancellationToken cancellationToken)
        {
            var odinContext = notification.OdinContext;

            var drive = await _driveManager.GetDrive(notification.File.DriveId, notification.DatabaseConnection);
            var isCollabChannel = drive.Attributes.TryGetValue(IsCollaborativeChannel, out string value) &&
                                  bool.TryParse(value, out bool collabChannelFlagValue) &&
                                  collabChannelFlagValue;

            if (await ShouldDistribute(notification, isCollabChannel))
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
                        await this.DistributeToConnectedFollowersUsingTransit(notification, notification.OdinContext, notification.DatabaseConnection);
                    }
                    else
                    {
                        await this.EnqueueFileMetadataNotificationForDistributionUsingFeedEndpoint(notification, notification.DatabaseConnection);
                    }

                    _peerOutboxProcessorBackgroundService.PulseBackgroundProcessor();
                }
                else
                {
                    try
                    {
                        if (isCollabChannel)
                        {
                            var upgradedContext = OdinContextUpgrades.UpgradeToNonOwnerFeedDistributor(notification.OdinContext);
                            await DistributeToCollaborativeChannelMembers(notification, upgradedContext, notification.DatabaseConnection);
                            _peerOutboxProcessorBackgroundService.PulseBackgroundProcessor();
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
                        await this.EnqueueFileMetadataNotificationForDistributionUsingFeedEndpoint(notification, notification.DatabaseConnection);
                        _peerOutboxProcessorBackgroundService.PulseBackgroundProcessor();
                        return;
                    }
                }
            }
        }

        private async Task EnqueueFileMetadataNotificationForDistributionUsingFeedEndpoint(IDriveNotification notification, DatabaseConnection cn)
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
                var recipients = await GetFollowers(notification.File.DriveId, newContext, cn);
                foreach (var recipient in recipients)
                {
                    await AddToFeedOutbox(recipient, item, cn);
                }
            }
        }

        private async Task<bool> ShouldDistribute(IDriveNotification notification, bool isCollabChannel)
        {
            if (notification.IgnoreFeedDistribution)
            {
                return false;
            }

            //if the file was received from another identity, do not redistribute
            var serverFileHeader = notification.ServerFileHeader;
            var sender = serverFileHeader?.FileMetadata?.SenderOdinId;
            var uploadedByThisIdentity = sender == _tenantContext.HostOdinId || string.IsNullOrEmpty(sender?.Trim());
            if (!uploadedByThisIdentity && !isCollabChannel)
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

            if (!await SupportsSubscription(serverFileHeader.FileMetadata!.File.DriveId, notification.DatabaseConnection))
            {
                return false;
            }

            return true;
        }

        private async Task DistributeToCollaborativeChannelMembers(IDriveNotification notification, IOdinContext odinContext, DatabaseConnection cn)
        {
            var header = notification.ServerFileHeader;

            var connectedFollowers = await GetConnectedFollowersWithFilePermission(notification, odinContext, cn);

            // var author = odinContext.GetCallerOdinIdOrFail();
            // connectedFollowers = connectedFollowers.Where(f => (OdinId)f.AsciiDomain != author).ToList();

            if (connectedFollowers.Any())
            {
                foreach (var recipient in connectedFollowers)
                {
                    // Prepare the file
                    EccEncryptedPayload encryptedPayload = null;

                    if (header.FileMetadata.IsEncrypted)
                    {
                        var storageKey = odinContext.PermissionsContext.GetDriveStorageKey(header.FileMetadata.File.DriveId);
                        var keyHeader = header.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);

                        var payload = new FeedItemPayload()
                        {
                            KeyHeaderBytes = keyHeader.Combine().GetKey()
                        };

                        //TODO: encryption - need to convert to the online key
                        encryptedPayload = await _pkService.EccEncryptPayloadForRecipient(
                            PublicPrivateKeyType.OfflineKey,
                            recipient,
                            OdinSystemSerializer.Serialize(payload).ToUtf8ByteArray(),
                            cn);
                    }

                    await AddToFeedOutbox(recipient, new FeedDistributionItem()
                        {
                            DriveNotificationType = notification.DriveNotificationType,
                            SourceFile = notification.ServerFileHeader.FileMetadata!.File,
                            FileSystemType = notification.ServerFileHeader.ServerMetadata.FileSystemType,
                            FeedDistroType = FeedDistroType.CollaborativeChannel,
                            EncryptedPayload = encryptedPayload
                        },
                        cn
                    );
                }
            }
        }

        /// <summary>
        /// Distributes to connected identities that are followers using
        /// transit; returns the list of unconnected identities
        /// </summary>
        private async Task DistributeToConnectedFollowersUsingTransit(IDriveNotification notification, IOdinContext odinContext, DatabaseConnection cn)
        {
            var connectedFollowers = await GetConnectedFollowersWithFilePermission(notification, odinContext, cn);
            if (connectedFollowers.Any())
            {
                if (notification.DriveNotificationType == DriveNotificationType.FileDeleted)
                {
                    var deletedFileNotification = (DriveFileDeletedNotification)notification;
                    if (!deletedFileNotification.IsHardDelete)
                    {
                        await DeleteFileOverTransit(notification.ServerFileHeader, connectedFollowers, odinContext, cn);
                    }
                }
                else
                {
                    await SendFileOverTransit(notification.ServerFileHeader, connectedFollowers, odinContext, cn);
                }
            }

            // return followers.Except(connectedFollowers).ToList();
        }

        private async Task<List<OdinId>> GetFollowers(Guid driveId, IOdinContext odinContext, DatabaseConnection cn)
        {
            int maxRecords = 100000; //TODO: cursor thru batches instead

            //
            // Get followers for this drive and merge with followers who want everything
            //
            var td = odinContext.PermissionsContext.GetTargetDrive(driveId);
            var driveFollowers = await _followerService.GetFollowers(td, maxRecords, cursor: "", odinContext, cn);
            var allDriveFollowers = await _followerService.GetFollowersOfAllNotifications(maxRecords, cursor: "", odinContext, cn);

            var recipients = new List<OdinId>();
            recipients.AddRange(driveFollowers.Results);
            recipients.AddRange(allDriveFollowers.Results.Except(driveFollowers.Results));

            return recipients;
        }

        private async Task SendFileOverTransit(ServerFileHeader header, List<OdinId> recipients, IOdinContext odinContext, DatabaseConnection cn)
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
                TransferFileType.Normal,
                header.ServerMetadata.FileSystemType,
                odinContext,
                cn);

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

        private async Task DeleteFileOverTransit(ServerFileHeader header, List<OdinId> recipients, IOdinContext odinContext, DatabaseConnection cn)
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
                    odinContext,
                    cn);

                foreach (var (recipient, status) in map)
                {
                    if (status == DeleteLinkedFileStatus.EnqueueFailed)
                    {
                        _logger.LogWarning("Enqueuing failed for recipient: {recipient}", recipient);
                    }
                }
            }
        }

        private async Task<bool> SupportsSubscription(Guid driveId, DatabaseConnection cn)
        {
            var drive = await _driveManager.GetDrive(driveId, cn);
            return drive.AllowSubscriptions && drive.TargetDriveInfo.Type == SystemDriveConstants.ChannelDriveType;
        }

        private async Task AddToFeedOutbox(OdinId recipient, FeedDistributionItem distroItem, DatabaseConnection cn)
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

            await _peerOutbox.AddItem(item, cn, useUpsert: true);
        }

        private async Task<List<OdinId>> GetConnectedFollowersWithFilePermission(IDriveNotification notification, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            var followers = await GetFollowers(notification.File.DriveId, odinContext, cn);
            if (!followers.Any())
            {
                return [];
            }

            //find all followers that are connected, return those which are not to be processed differently
            var connectedIdentities = await _circleNetworkService.GetCircleMembers(SystemCircleConstants.ConnectedIdentitiesSystemCircleId, odinContext, cn);
            var connectedFollowers = followers.Intersect(connectedIdentities)
                .Where(cf => _driveAcl.IdentityHasPermission(
                        (OdinId)cf.DomainName,
                        notification.ServerFileHeader.ServerMetadata.AccessControlList,
                        odinContext,
                        cn)
                    .GetAwaiter().GetResult()).ToList();
            return connectedFollowers;
        }
    }
}