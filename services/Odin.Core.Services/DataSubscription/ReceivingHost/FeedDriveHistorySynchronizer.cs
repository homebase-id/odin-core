using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.DataSubscription.Follower;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Standard;
using Odin.Core.Services.Membership.Connections;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Core.Storage;

namespace Odin.Core.Services.DataSubscription.ReceivingHost
{
    /// <summary>
    /// Synchronizes feed history when followers are added
    /// </summary>
    public class FeedDriveHistorySynchronizer
    {
        private readonly OdinContextAccessor _contextAccessor;
        private readonly StandardFileSystem _standardFileSystem;
        private readonly TransitQueryService _transitQueryService;
        private readonly CircleNetworkService _circleNetworkService;
        private readonly FollowerService _followerService;

        private const int MaxRecordsPerChannel = 100; //TODO:config

        public FeedDriveHistorySynchronizer(
            OdinContextAccessor contextAccessor,
            StandardFileSystem standardFileSystem, 
            TransitQueryService transitQueryService, 
            CircleNetworkService circleNetworkService, 
            FollowerService followerService)
        {
            _contextAccessor = contextAccessor;
            _standardFileSystem = standardFileSystem;
            _transitQueryService = transitQueryService;
            _circleNetworkService = circleNetworkService;
            _followerService = followerService;
        }
        
        public async Task SynchronizeChannelFiles(OdinId odinId)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ManageFeed);
            var definition = await _followerService.GetIdentityIFollow(odinId);
            if (definition == null) //not following
            {
                return;
            }

            var feedDriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(SystemDriveConstants.FeedDrive);

            SensitiveByteArray sharedSecret = null;
            var icr = await _circleNetworkService.GetIdentityConnectionRegistration(odinId);
            if (icr.IsConnected())
            {
                sharedSecret = icr.CreateClientAccessToken(_contextAccessor.GetCurrent().PermissionsContext.GetIcrKey()).SharedSecret;
            }

            var channelDrives = await _transitQueryService.GetDrivesByType(odinId, SystemDriveConstants.ChannelDriveType, FileSystemType.Standard);

            //filter the drives to those I want to see
            if (definition.NotificationType == FollowerNotificationType.SelectedChannels)
            {
                channelDrives = channelDrives.IntersectBy(definition.Channels, d => d.TargetDrive);
            }

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
    }
}