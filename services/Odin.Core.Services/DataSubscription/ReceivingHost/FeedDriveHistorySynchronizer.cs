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
using Odin.Core.Services.DataSubscription.SendingHost;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Standard;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Mediator;
using Odin.Core.Services.Membership.Connections;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Core.Storage;

namespace Odin.Core.Services.DataSubscription.ReceivingHost
{
    /// <summary>
    /// Synchronizes feed history when followers are added
    /// </summary>
    public class FeedDriveHistorySynchronizer : INotificationHandler<NewFollowerNotification>, INotificationHandler<NewConnectionEstablishedNotification>
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
        private readonly TransitQueryService _transitQueryService;
        private readonly CircleNetworkService _circleNetworkService;
        private readonly IDriveAclAuthorizationService _driveAcl;

        private const int MaxRecordsPerChannel = 100; //TODO:config

        public FeedDriveHistorySynchronizer(
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
            StandardFileSystem standardFileSystem, TransitQueryService transitQueryService, CircleNetworkService circleNetworkService)
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
            _transitQueryService = transitQueryService;
            _circleNetworkService = circleNetworkService;

            _feedDistributorService = new FeedDistributorService(fileSystemResolver, odinHttpClientFactory, driveAcl);
        }

        public Task Handle(NewFollowerNotification notification, CancellationToken cancellationToken)
        {
            // When a new follower comes in, we send out some historical content from so their feed is populated
            DistributeHeaderFiles(notification.OdinId);

            //query the channels based on the follower
            return Task.CompletedTask;
        }

        public Task Handle(NewConnectionEstablishedNotification notification, CancellationToken cancellationToken)
        {
            //If we follow this connection, we pull header files from the new connection

            //TODO: check if i follow this person

            SynchronizeChannelFiles(notification.OdinId).GetAwaiter().GetResult();

            //using transit query, write to feed drive; 
            return Task.CompletedTask;
        }

        public async Task SynchronizeChannelFiles(OdinId odinId)
        {
            var feedDriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(SystemDriveConstants.FeedDrive);

            SensitiveByteArray sharedSecret = null;
            var icr = await _circleNetworkService.GetIdentityConnectionRegistration(odinId);
            if (icr.IsConnected())
            {
                sharedSecret = icr.CreateClientAccessToken(_contextAccessor.GetCurrent().PermissionsContext.GetIcrKey()).SharedSecret;
            }

            var channelDrives = await _transitQueryService.GetDrivesByType(odinId, SystemDriveConstants.ChannelDriveType, FileSystemType.Standard);
            var request = new QueryBatchCollectionRequest()
            {
                Queries = new List<CollectionQueryParamSection>()
            };

            var resultOptions = new QueryBatchResultOptionsRequest()
            {
                MaxRecords = MaxRecordsPerChannel,
                IncludeMetadataHeader = true,
            };

            foreach (var channel in channelDrives)
            {
                var targetDrive = channel.TargetDrive;
                request.Queries.Add(new()
                    {
                        Name = targetDrive.ToKey().ToBase64(),
                        QueryParams = new FileQueryParams()
                        {
                            TargetDrive = targetDrive,
                            FileState = new List<FileState>() { FileState.Active }
                        },
                        ResultOptionsRequest = resultOptions
                    }
                );
            }

            var collection = await _transitQueryService.GetBatchCollection(odinId, request, FileSystemType.Standard);

            foreach (var results in collection.Results)
            {
                if (results.InvalidDrive)
                {
                    continue;
                }

                foreach (var dsr in results.SearchResults)
                {
                    var keyHeader = KeyHeader.Empty();
                    if (dsr.FileMetadata.IsEncrypted)
                    {
                        if (null == sharedSecret)
                        {
                            //skip this file.  i should have received it because i'm not connected
                            continue;
                        }

                        keyHeader = dsr.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);
                    }
                    
                    var fm = dsr.FileMetadata;
                    var newFileMetadata = new FileMetadata()
                    {
                        File = default,
                        GlobalTransitId = fm.GlobalTransitId,
                        ReferencedFile = fm.ReferencedFile,
                        AppData = fm.AppData,

                        IsEncrypted = fm.IsEncrypted,
                        SenderOdinId = odinId,

                        VersionTag = fm.VersionTag,
                        ReactionPreview = fm.ReactionPreview,
                        Created = fm.Created,
                        Updated = fm.Updated,
                        FileState = dsr.FileState,
                        Payloads = fm.Payloads
                    };

                    SharedSecretEncryptedFileHeader existingFile = null;
                    if (dsr.FileMetadata.AppData.UniqueId.HasValue)
                    {
                        existingFile = await _standardFileSystem.Query.GetFileByClientUniqueId(feedDriveId,
                            dsr.FileMetadata.AppData.UniqueId.GetValueOrDefault());
                    }

                    if (null == existingFile)
                    {
                        await _standardFileSystem.Storage.WriteNewFileToFeedDrive(keyHeader, newFileMetadata);
                    }
                    else
                    {
                        var file = new InternalDriveFileId()
                        {
                            FileId = existingFile.FileId,
                            DriveId = feedDriveId
                        };

                        await _standardFileSystem.Storage.ReplaceFileMetadataOnFeedDrive(file, newFileMetadata);
                    }
                }
            }
        }

        private void DistributeHeaderFiles(OdinId recipient)
        {
            //the caller is the notification.OdinId, so access to channels and files will be limited based on access
            var resultOptions = new QueryBatchResultOptionsRequest()
            {
                MaxRecords = MaxRecordsPerChannel,
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
                // We need to upgrade the permission here, temporarily because the caller
                // Is requesting feed files to be distributed but does not have useTransitRead access
                // however, we've built the files being sent and they are only accessible to the caller

                using (new FeedDriveHistoryDistributorSecurityContext(_contextAccessor))
                {
                    await this.DistributeUsingTransit(recipient, header);
                }

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

                var _ = await _transitService.SendFile(
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