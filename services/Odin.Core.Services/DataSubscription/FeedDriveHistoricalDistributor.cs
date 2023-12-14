using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.AppNotifications.ClientNotifications;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Base;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.DataSubscription.ReceivingHost;
using Odin.Core.Services.DataSubscription.SendingHost;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Standard;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Mediator;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.SendingHost;

namespace Odin.Core.Services.DataSubscription
{
    /// <summary>
    /// Sends historical feed items to newly connected identities or new followers
    /// </summary>
    public class FeedDriveHistoricalDistributor : INotificationHandler<NewFollowerNotification>
    {
        private readonly DriveManager _driveManager;
        private readonly ITransitService _transitService;
        private readonly TenantContext _tenantContext;
        private readonly ServerSystemStorage _serverSystemStorage;
        private readonly TenantSystemStorage _tenantSystemStorage;
        private readonly OdinContextAccessor _contextAccessor;
        private readonly FeedDistributorService _feedDistributorService;
        private readonly OdinConfiguration _odinConfiguration;
        private readonly StandardFileSystem _standardFileSystem;

        private readonly IDriveAclAuthorizationService _driveAcl;

        public FeedDriveHistoricalDistributor(
            ITransitService transitService,
            DriveManager driveManager,
            TenantContext tenantContext,
            ServerSystemStorage serverSystemStorage,
            FileSystemResolver fileSystemResolver,
            TenantSystemStorage tenantSystemStorage,
            OdinContextAccessor contextAccessor,
            IOdinHttpClientFactory odinHttpClientFactory,
            OdinConfiguration odinConfiguration,
            IDriveAclAuthorizationService driveAcl,
            StandardFileSystem standardFileSystem)
        {
            _transitService = transitService;
            _driveManager = driveManager;
            _tenantContext = tenantContext;
            _serverSystemStorage = serverSystemStorage;
            _tenantSystemStorage = tenantSystemStorage;
            _contextAccessor = contextAccessor;
            _odinConfiguration = odinConfiguration;
            _driveAcl = driveAcl;
            _standardFileSystem = standardFileSystem;

            _feedDistributorService = new FeedDistributorService(fileSystemResolver, odinHttpClientFactory, driveAcl);
        }

        public Task Handle(NewFollowerNotification notification, CancellationToken cancellationToken)
        {
            // When a new follower comes in, we send out some historical content from so their feed is populated
            var recipient = notification.OdinId;

            //the caller is the notification.OdinId, so access to channels and files will be limited based on access
            const int maxRecordsPerChannel = 100; //TODO:config

            var resultOptions = new QueryBatchResultOptionsRequest()
            {
                MaxRecords = maxRecordsPerChannel,
                IncludeMetadataHeader = true,
            };

            var drives = GetChannelDrivesForCaller().GetAwaiter().GetResult();
            var request = new QueryBatchCollectionRequest()
            {
                Queries = new List<CollectionQueryParamSection>()
            };

            foreach (var targetDrive in drives)
            {
                request.Queries.Add(new()
                    {
                        Name = targetDrive.ToKey().ToBase64(),
                        QueryParams = new FileQueryParams()
                        {
                            TargetDrive = targetDrive,
                            FileType = new List<int>() {  } //TODO: need to determine if stef should filter here
                        },
                        ResultOptionsRequest = resultOptions
                    }
                );
            }

            var collection = _standardFileSystem.Query.GetBatchCollection(request, forceIncludeServerMetadata: true).GetAwaiter().GetResult();

            foreach (var results in collection.Results)
            {
                if (!results.InvalidDrive)
                {
                    foreach (var dsr in results.SearchResults)
                    {
                        //try to distribute file
                        TryDistributeFile(recipient, dsr).GetAwaiter().GetResult();
                    }
                }
            }

            //query the channels based on the follower
            return Task.CompletedTask;
        }

        private async Task TryDistributeFile(OdinId recipient, SharedSecretEncryptedFileHeader header)
        {
            if (!await ShouldDistribute(header))
            {
                return;
            }

            //TODO: issue, even if the new follower is connected, he will now show
            //if the new follower is connected; send file using transit
            if (_contextAccessor.GetCurrent().Caller.IsConnected && header.FileMetadata.IsEncrypted)
            {
                await this.DistributeUsingTransit(recipient, header);
                return;
            }

            //if not send using feed
            await this.EnqueueFileMetadataNotificationForDistributionUsingFeedEndpoint(recipient, header);
        }

        private async Task EnqueueFileMetadataNotificationForDistributionUsingFeedEndpoint(OdinId recipient, SharedSecretEncryptedFileHeader header)
        {
            var file = ResolveInternalFileId(header);
            var item = new ReactionPreviewDistributionItem()
            {
                DriveNotificationType = DriveNotificationType.FileAdded, //pretend we just added a file so it will get distributed like normal
                SourceFile = file,
                FileSystemType = header.ServerMetadata.FileSystemType,
                FeedDistroType = FeedDistroType.FileMetadata
            };

            if (_odinConfiguration.Feed.InstantDistribution)
            {
                await DistributeMetadataNow(recipient, item);
            }
            else
            {
                using (new FeedDriveSecurityContext(_contextAccessor))
                {
                    AddToFeedOutbox(recipient, item);
                    EnqueueCronJob();
                }
            }
        }

        private InternalDriveFileId ResolveInternalFileId(SharedSecretEncryptedFileHeader header)
        {
            return new InternalDriveFileId()
            {
                FileId = header.FileId,
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(header.TargetDrive)
            };
        }

        private Task<bool> ShouldDistribute(SharedSecretEncryptedFileHeader header)
        {
            if (null == header)
            {
                return Task.FromResult(false);
            }

            //if the file was received from another identity, do not redistribute
            var sender = header.FileMetadata?.SenderOdinId;
            var uploadedByThisIdentity = sender == _tenantContext.HostOdinId || string.IsNullOrEmpty(sender?.Trim());
            if (!uploadedByThisIdentity)
            {
                return Task.FromResult(false);
            }

            if (header.FileState == FileState.Deleted)
            {
                return Task.FromResult(false);
            }

            if (!header.ServerMetadata.AllowDistribution)
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }

        private async Task DistributeMetadataNow(OdinId recipient, ReactionPreviewDistributionItem distroItem)
        {
            var file = distroItem.SourceFile;
            var success = await _feedDistributorService.SendFile(file, distroItem.FileSystemType, recipient);
            if (!success)
            {
                // fall back to queue
                AddToFeedOutbox(recipient, distroItem);
            }
        }

        private async Task DistributeUsingTransit(OdinId recipient, SharedSecretEncryptedFileHeader header)
        {
            //find all followers that are connected, return those which are not to be processed differently
            var caller = _contextAccessor.GetCurrent().GetCallerOdinIdOrFail();
            var hasPermission = await _driveAcl.IdentityHasPermission(caller, header.ServerMetadata.AccessControlList);

            if (hasPermission)
            {
                var transitOptions = new TransitOptions()
                {
                    Recipients = new List<string>() { recipient },
                    Schedule = _odinConfiguration.Feed.InstantDistribution
                        ? ScheduleOptions.SendNowAwaitResponse
                        : ScheduleOptions.SendLater,
                    IsTransient = false,
                    UseGlobalTransitId = true,
                    SendContents = SendContents.Header,
                    RemoteTargetDrive = SystemDriveConstants.FeedDrive
                };

                var transferStatusMap = await _transitService.SendFile(
                    ResolveInternalFileId(header),
                    transitOptions,
                    TransferFileType.Normal,
                    header.ServerMetadata.FileSystemType);

                // TODO: need to determine how to handle the transferStatusMap
                // this feed drive router happens in the background so how do
                // we want to handle any TransferStatus that indicates an
                // unsuccessful that is not retried by the transit system
                // i.e. TransferStatus.TotalRejectionClientShouldRetry
            }
        }

        private void EnqueueCronJob()
        {
            _serverSystemStorage.EnqueueJob(_tenantContext.HostOdinId, CronJobType.FeedDistribution,
                new FeedDistributionInfo()
                {
                    OdinId = _tenantContext.HostOdinId,
                });
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

        private async Task<IEnumerable<TargetDrive>> GetChannelDrivesForCaller()
        {
            //filter drives by only returning those the caller can see
            var allDrives = await _driveManager.GetDrives(SystemDriveConstants.ChannelDriveType, PageOptions.All);
            var perms = _contextAccessor.GetCurrent().PermissionsContext;
            var readableDrives = allDrives.Results.Where(drive =>
                drive.AllowSubscriptions && perms.HasDrivePermission(drive.Id, DrivePermission.Read));
            return readableDrives.Select(drive => drive.TargetDriveInfo);
        }
    }
}