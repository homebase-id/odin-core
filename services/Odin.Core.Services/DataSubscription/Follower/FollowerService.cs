using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Dawn;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Standard;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Core.Services.Membership.Connections;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Services.Peer.Outgoing;
using Odin.Core.Services.Peer.Outgoing.Query;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Refit;

namespace Odin.Core.Services.DataSubscription.Follower
{
    /// <summary/>
    public class FollowerService
    {
        private readonly TenantSystemStorage _tenantStorage;
        private readonly DriveManager _driveManager;
        private readonly IOdinHttpClientFactory _httpClientFactory;
        private readonly PublicPrivateKeyService _publicPrivatePublicKeyService;
        private readonly TenantContext _tenantContext;
        private readonly OdinContextAccessor _contextAccessor;
        private readonly StandardFileSystem _standardFileSystem;
        private readonly PeerQueryService _peerQueryService;
        private readonly CircleNetworkService _circleNetworkService;

        private const int MaxRecordsPerChannel = 100; //TODO:config


        public FollowerService(TenantSystemStorage tenantStorage,
            DriveManager driveManager,
            IOdinHttpClientFactory httpClientFactory,
            PublicPrivateKeyService publicPrivatePublicKeyService,
            TenantContext tenantContext,
            OdinContextAccessor contextAccessor, StandardFileSystem standardFileSystem, PeerQueryService peerQueryService, CircleNetworkService circleNetworkService)
        {
            _tenantStorage = tenantStorage;
            _driveManager = driveManager;
            _httpClientFactory = httpClientFactory;
            _publicPrivatePublicKeyService = publicPrivatePublicKeyService;
            _tenantContext = tenantContext;
            _contextAccessor = contextAccessor;
            _standardFileSystem = standardFileSystem;
            _peerQueryService = peerQueryService;
            _circleNetworkService = circleNetworkService;
        }

        /// <summary>
        /// Establishes a follower connection with the recipient
        /// </summary>
        public async Task Follow(FollowRequest request)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ManageFeed);

            var identityToFollow = (OdinId)request.OdinId;

            if (_contextAccessor.GetCurrent().Caller.OdinId == identityToFollow)
            {
                throw new OdinClientException("Cannot follow yourself; at least not in this dimension because that would be like chasing your own tail",
                    OdinClientErrorCode.InvalidRecipient);
            }
            
            var existingFollow = await this.GetIdentityIFollowInternal(identityToFollow);
            if (null != existingFollow)
            {
                throw new OdinClientException("You already follow the requested identity");
            }

            //TODO: use the exchange grant service to create the access reg and CAT 

            var perimeterFollowRequest = new PerimeterFollowRequest()
            {
                OdinId = _tenantContext.HostOdinId,
                NotificationType = request.NotificationType,
                Channels = request.Channels
            };

            var json = OdinSystemSerializer.Serialize(perimeterFollowRequest);

            async Task<ApiResponse<HttpContent>> TryFollow()
            {
                var rsaEncryptedPayload = await _publicPrivatePublicKeyService.EncryptPayloadForRecipient(
                    RsaKeyType.OfflineKey, identityToFollow, json.ToUtf8ByteArray());
                var client = CreateClient(identityToFollow);
                var response = await client.Follow(rsaEncryptedPayload);
                return response;
            }

            if ((await TryFollow()).IsSuccessStatusCode == false)
            {
                //public key might be invalid, destroy the cache item
                await _publicPrivatePublicKeyService.InvalidateRecipientPublicKey(identityToFollow);

                //round 2, fail all together
                if ((await TryFollow()).IsSuccessStatusCode == false)
                {
                    throw new OdinRemoteIdentityException("Remote Server failed to accept follow");
                }
            }

            using (_tenantStorage.CreateCommitUnitOfWork())
            {
                //delete all records and update according to the latest follow request.
                _tenantStorage.WhoIFollow.DeleteByIdentity(identityToFollow);
                if (request.NotificationType == FollowerNotificationType.AllNotifications)
                {
                    _tenantStorage.WhoIFollow.Insert(new ImFollowingRecord() { identity = identityToFollow, driveId = Guid.Empty });
                }

                if (request.NotificationType == FollowerNotificationType.SelectedChannels)
                {
                    if (request.Channels.Any(c => c.Type != SystemDriveConstants.ChannelDriveType))
                    {
                        throw new OdinClientException("Only drives of type channel can be followed", OdinClientErrorCode.InvalidTargetDrive);
                    }

                    //use the alias because we don't most likely will not have the channel on the callers identity
                    foreach (var channel in request.Channels)
                    {
                        _tenantStorage.WhoIFollow.Insert(new ImFollowingRecord() { identity = identityToFollow, driveId = channel.Alias });
                    }
                }
            }

            if (request.SynchronizeFeedHistoryNow)
            {
                await this.SynchronizeChannelFiles(identityToFollow);
            }
        }

        /// <summary>
        /// Notifies the recipient you are no longer following them.  This means they
        /// should no longer send you updates/notifications
        /// </summary>
        public async Task Unfollow(OdinId recipient)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ManageFeed);

            var client = CreateClient(recipient);
            var response = await client.Unfollow();

            if (!response.IsSuccessStatusCode)
            {
                throw new OdinRemoteIdentityException("Failed to unfollow");
            }

            _tenantStorage.WhoIFollow.DeleteByIdentity(recipient);
        }

        public async Task<FollowerDefinition> GetFollower(OdinId odinId)
        {
            //a follower is allowed to read their own configuration
            if (odinId != _contextAccessor.GetCurrent().Caller.OdinId)
            {
                _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadMyFollowers);
            }

            return await GetFollowerInternal(odinId);
        }

        /// <summary>
        /// Gets the details (channels, etc.) of an identity that you follow.
        /// </summary>
        public async Task<FollowerDefinition> GetIdentityIFollow(OdinId odinId)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadWhoIFollow);
            return await GetIdentityIFollowInternal(odinId);
        }

        public async Task<CursoredResult<string>> GetAllFollowers(int max, string cursor)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadMyFollowers);

            var dbResults = _tenantStorage.Followers.GetAllFollowers(DefaultMax(max), cursor, out var nextCursor);

            var result = new CursoredResult<string>()
            {
                Cursor = nextCursor,
                Results = dbResults
            };

            return await Task.FromResult(result);
        }

        /// <summary>
        /// Gets a list of identities that follow me
        /// </summary>
        public async Task<CursoredResult<OdinId>> GetFollowers(TargetDrive targetDrive, int max, string cursor)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadMyFollowers);

            if (targetDrive.Type != SystemDriveConstants.ChannelDriveType)
            {
                throw new OdinClientException("Invalid Drive Type", OdinClientErrorCode.InvalidTargetDrive);
            }

            var dbResults = _tenantStorage.Followers.GetFollowers(DefaultMax(max), targetDrive.Alias, cursor, out var nextCursor);
            var result = new CursoredResult<OdinId>()
            {
                Cursor = nextCursor,
                Results = dbResults.Select(ident => new OdinId(ident))
            };

            return await Task.FromResult(result);
        }

        /// <summary>
        /// Gets followers who want notifications for all channels
        /// </summary>
        public async Task<CursoredResult<OdinId>> GetFollowersOfAllNotifications(int max, string cursor)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadMyFollowers);

            var dbResults = _tenantStorage.Followers.GetFollowers(DefaultMax(max), Guid.Empty, cursor, out var nextCursor);

            var result = new CursoredResult<OdinId>()
            {
                Cursor = nextCursor,
                Results = dbResults.Select(ident => new OdinId(ident))
            };

            return await Task.FromResult(result);
        }

        /// <summary>
        /// Gets a list of identities I follow
        /// </summary>
        public async Task<CursoredResult<string>> GetIdentitiesIFollow(int max, string cursor)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadWhoIFollow);

            var dbResults = _tenantStorage.WhoIFollow.GetAllFollowers(DefaultMax(max), cursor, out var nextCursor);
            var result = new CursoredResult<string>()
            {
                Cursor = nextCursor,
                Results = dbResults
            };
            return await Task.FromResult(result);
        }

        public async Task<CursoredResult<string>> GetIdentitiesIFollow(Guid driveAlias, int max, string cursor)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadWhoIFollow);

            var drive = await _driveManager.GetDrive(driveAlias, true);
            if (drive.TargetDriveInfo.Type != SystemDriveConstants.ChannelDriveType)
            {
                throw new OdinClientException("Invalid Drive Type", OdinClientErrorCode.InvalidTargetDrive);
            }

            var dbResults = _tenantStorage.WhoIFollow.GetFollowers(DefaultMax(max), driveAlias, cursor, out var nextCursor);
            return new CursoredResult<string>()
            {
                Cursor = nextCursor,
                Results = dbResults
            };
        }

        /// <summary>
        /// Allows my followers to write reactions. 
        /// </summary>
        public async Task<PermissionContext> CreateFollowerPermissionContext(OdinId odinId, ClientAuthenticationToken token)
        {
            //Note: this check here is basically a replacement for the token
            // meaning - it is required to be an owner to follow an identity
            // so they will only be in the list if the owner added them
            var definition = await GetFollowerInternal(odinId);
            if (null == definition)
            {
                throw new OdinSecurityException($"Not following {odinId}");
            }

            var permissionSet = new PermissionSet(); //no permissions
            var sharedSecret = Guid.Empty.ToByteArray().ToSensitiveByteArray();

            //need to grant access to connected drives

            var driveGrants = new List<DriveGrant>();

            var groups = new Dictionary<string, PermissionGroup>()
            {
                { "follower", new PermissionGroup(permissionSet, driveGrants, sharedSecret, null) }
            };

            return new PermissionContext(groups, null);
        }

        /// <summary>
        /// Allows an identity I follow to write to my feed drive.
        /// </summary>
        public async Task<PermissionContext> CreatePermissionContextForIdentityIFollow(OdinId odinId, ClientAuthenticationToken token)
        {
            // Note: this check here is basically a replacement for the token
            // meaning - it is required to be an owner to follow an identity
            // so they will only be in the list if the owner added them
            var definition = await GetIdentityIFollowInternal(odinId);
            if (null == definition)
            {
                throw new OdinSecurityException($"Not following {odinId}");
            }

            var feedDrive = SystemDriveConstants.FeedDrive;
            var permissionSet = new PermissionSet(); //no permissions
            var sharedSecret = Guid.Empty.ToByteArray().ToSensitiveByteArray(); //TODO: what shared secret for this?

            var driveId = (await _driveManager.GetDriveIdByAlias(feedDrive, true)).GetValueOrDefault();
            var driveGrants = new List<DriveGrant>()
            {
                new()
                {
                    DriveId = driveId,
                    KeyStoreKeyEncryptedStorageKey = null,
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = feedDrive,
                        Permission = DrivePermission.Write
                    }
                }
            };

            var groups = new Dictionary<string, PermissionGroup>()
            {
                { "data_subscriber", new PermissionGroup(permissionSet, driveGrants, null, null) }
            };

            return new PermissionContext(groups, sharedSecret);
        }

        public async Task AssertTenantFollowsTheCaller()
        {
            var odinId = _contextAccessor.GetCurrent().GetCallerOdinIdOrFail();
            var definition = await this.GetIdentityIFollowInternal(odinId);
            if (null == definition)
            {
                throw new OdinSecurityException($"Not following {odinId}");
            }
        }


        public async Task SynchronizeChannelFiles(OdinId odinId)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ManageFeed);
            var definition = await this.GetIdentityIFollowInternal(odinId);
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

            var channelDrives = await _peerQueryService.GetDrivesByType(odinId, SystemDriveConstants.ChannelDriveType, FileSystemType.Standard);

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

            var collection = await _peerQueryService.GetBatchCollection(odinId, request, FileSystemType.Standard);

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
                    else if (dsr.FileMetadata.GlobalTransitId.HasValue)
                    {
                        existingFile = await _standardFileSystem.Query.GetFileByGlobalTransitId(feedDriveId,
                            dsr.FileMetadata.GlobalTransitId.GetValueOrDefault());
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

                        await _standardFileSystem.Storage.ReplaceFileMetadataOnFeedDrive(file, newFileMetadata, bypassCallerCheck:true);
                    }
                }
            }
        }


        ///
        private int DefaultMax(int max)
        {
            return Math.Max(max, 10);
        }

        private IFollowerHttpClient CreateClient(OdinId odinId)
        {
            var httpClient = _httpClientFactory.CreateClient<IFollowerHttpClient>(odinId);
            return httpClient;
        }

        private Task<FollowerDefinition> GetIdentityIFollowInternal(OdinId odinId)
        {
            Guard.Argument(odinId, nameof(odinId)).Require(d => d.HasValue());

            var dbRecords = _tenantStorage.WhoIFollow.Get(odinId);
            if (!dbRecords?.Any() ?? false)
            {
                return Task.FromResult<FollowerDefinition>(null);
            }

            if (dbRecords!.Any(f => odinId != (OdinId)f.identity))
            {
                throw new OdinSystemException($"Follower data for [{odinId}] is corrupt");
            }

            if (dbRecords.Any(r => r.driveId == Guid.Empty) && dbRecords.Count > 1)
            {
                throw new OdinSystemException($"Follower data for [{odinId}] is corrupt");
            }

            if (dbRecords.All(r => r.driveId == Guid.Empty))
            {
                return Task.FromResult(new FollowerDefinition()
                {
                    OdinId = odinId,
                    NotificationType = FollowerNotificationType.AllNotifications
                });
            }

            return Task.FromResult(new FollowerDefinition()
            {
                OdinId = odinId,
                NotificationType = FollowerNotificationType.SelectedChannels,
                Channels = dbRecords.Select(record => new TargetDrive()
                {
                    Alias = record.driveId,
                    Type = SystemDriveConstants.ChannelDriveType
                }).ToList()
            });
        }

        private async Task<FollowerDefinition> GetFollowerInternal(OdinId odinId)
        {
            Guard.Argument(odinId, nameof(odinId)).Require(d => d.HasValue());

            var dbRecords = _tenantStorage.Followers.Get(odinId);
            if (!dbRecords?.Any() ?? false)
            {
                return null;
            }

            if (dbRecords!.Any(f => odinId != (OdinId)f.identity))
            {
                throw new OdinSystemException($"Follower data for [{odinId}] is corrupt");
            }

            if (dbRecords.Any(r => r.driveId == Guid.Empty) && dbRecords.Count > 1)
            {
                throw new OdinSystemException($"Follower data for [{odinId}] is corrupt");
            }

            if (dbRecords.All(r => r.driveId == Guid.Empty))
            {
                return new FollowerDefinition()
                {
                    OdinId = odinId,
                    NotificationType = FollowerNotificationType.AllNotifications
                };
            }

            var result = new FollowerDefinition()
            {
                OdinId = odinId,
                NotificationType = FollowerNotificationType.SelectedChannels,
                Channels = dbRecords.Select(record => new TargetDrive()
                {
                    Alias = record.driveId, //Note: i really store the alias
                    Type = SystemDriveConstants.ChannelDriveType
                }).ToList()
            };

            return await Task.FromResult(result);
        }
    }
}