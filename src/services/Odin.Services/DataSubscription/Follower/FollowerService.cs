using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Drives.Management;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Drive.Query;
using Refit;
using PeerDriveQueryService = Odin.Services.Peer.Outgoing.Drive.Query.PeerDriveQueryService;

namespace Odin.Services.DataSubscription.Follower
{
    /// <summary/>
    public class FollowerService
    {
        private readonly ILogger<FollowerService> _logger;
        private readonly DriveManager _driveManager;
        private readonly IOdinHttpClientFactory _httpClientFactory;
        private readonly PublicPrivateKeyService _publicPrivatePublicKeyService;
        private readonly TenantContext _tenantContext;

        private readonly StandardFileSystem _standardFileSystem;
        private readonly PeerDriveQueryService _peerDriveQueryService;
        private readonly CircleNetworkService _circleNetworkService;
        private readonly IdentityDatabase _db;

        private const int MaxRecordsPerChannel = 100; //TODO:config

        public FollowerService(
            ILogger<FollowerService> logger,
            DriveManager driveManager,
            IOdinHttpClientFactory httpClientFactory,
            PublicPrivateKeyService publicPrivatePublicKeyService,
            TenantContext tenantContext,
            StandardFileSystem standardFileSystem, PeerDriveQueryService peerDriveQueryService,
            CircleNetworkService circleNetworkService,
            IdentityDatabase db)
        {
            _logger = logger;
            _driveManager = driveManager;
            _httpClientFactory = httpClientFactory;
            _publicPrivatePublicKeyService = publicPrivatePublicKeyService;
            _tenantContext = tenantContext;
            _standardFileSystem = standardFileSystem;
            _peerDriveQueryService = peerDriveQueryService;
            _circleNetworkService = circleNetworkService;
            _db = db;
        }

        /// <summary>
        /// Establishes a follower connection with the recipient
        /// </summary>
        public async Task FollowAsync(FollowRequest request, IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageFeed);

            var identityToFollow = (OdinId)request.OdinId;

            if (odinContext.Caller.OdinId == identityToFollow)
            {
                throw new OdinClientException("Cannot follow yourself; at least not in this dimension because that would be like chasing your own tail",
                    OdinClientErrorCode.InvalidRecipient);
            }

            var existingFollow = await this.GetIdentityIFollowInternalAsync(identityToFollow);
            if (null != existingFollow)
            {
                throw new OdinClientException("You already follow the requested identity", OdinClientErrorCode.IdentityAlreadyFollowed);
            }

            //TODO: use the exchange grant service to create the access reg and CAT 

            var perimeterFollowRequest = new PerimeterFollowRequest()
            {
                OdinId = _tenantContext.HostOdinId,
                NotificationType = request.NotificationType,
                Channels = request.Channels
            };

            var json = OdinSystemSerializer.Serialize(perimeterFollowRequest);

            var keyType = PublicPrivateKeyType.OfflineKey;

            async Task<ApiResponse<HttpContent>> TryFollow()
            {
                var eccEncryptedPayload = await _publicPrivatePublicKeyService.EccEncryptPayloadForRecipientAsync(
                    keyType, identityToFollow, json.ToUtf8ByteArray());
                var client = CreateClient(identityToFollow);
                var response = await client.Follow(eccEncryptedPayload);
                return response;
            }

            if ((await TryFollow()).IsSuccessStatusCode == false)
            {
                //public key might be invalid, destroy the cache item
                await _publicPrivatePublicKeyService.InvalidateRecipientEccPublicKeyAsync(keyType, identityToFollow);

                //round 2, fail all together
                if ((await TryFollow()).IsSuccessStatusCode == false)
                {
                    throw new OdinRemoteIdentityException("Remote Server failed to accept follow");
                }
            }

            await using (var tx = await _db.BeginStackedTransactionAsync())
            {
                await _db.ImFollowing.DeleteByIdentityAsync(identityToFollow);
                if (request.NotificationType == FollowerNotificationType.AllNotifications)
                {
                    await _db.ImFollowing.InsertAsync(new ImFollowingRecord()
                        { identity = identityToFollow, driveId = Guid.Empty });
                }

                if (request.NotificationType == FollowerNotificationType.SelectedChannels)
                {
                    if (request.Channels.Any(c => c.Type != SystemDriveConstants.ChannelDriveType))
                    {
                        throw new OdinClientException("Only drives of type channel can be followed",
                            OdinClientErrorCode.InvalidTargetDrive);
                    }

                    //use the alias because we don't most likely will not have the channel on the callers identity
                    foreach (var channel in request.Channels)
                    {
                        await _db.ImFollowing.InsertAsync(new ImFollowingRecord()
                            { identity = identityToFollow, driveId = channel.Alias });
                    }
                }
                tx.Commit();
            }

            if (request.SynchronizeFeedHistoryNow)
            {
                await SynchronizeChannelFilesAsync(identityToFollow, odinContext);
            }
        }

        /// <summary>
        /// Notifies the recipient you are no longer following them.  This means they
        /// should no longer send you updates/notifications
        /// </summary>
        public async Task UnfollowAsync(OdinId recipient, IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageFeed);

            var client = CreateClient(recipient);
            var response = await client.Unfollow();

            if (!response.IsSuccessStatusCode)
            {
                throw new OdinRemoteIdentityException("Failed to unfollow");
            }

            await _db.ImFollowing.DeleteByIdentityAsync(recipient);
        }

        public async Task<FollowerDefinition> GetFollowerAsync(OdinId odinId, IOdinContext odinContext)
        {
            //a follower is allowed to read their own configuration
            if (odinId != odinContext.Caller.OdinId)
            {
                odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadMyFollowers);
            }

            return await GetFollowerInternalAsync(odinId);
        }

        /// <summary>
        /// Gets the details (channels, etc.) of an identity that you follow.
        /// </summary>
        public async Task<FollowerDefinition> GetIdentityIFollowAsync(OdinId odinId, IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadWhoIFollow);
            return await GetIdentityIFollowInternalAsync(odinId);
        }

        public async Task<CursoredResult<string>> GetAllFollowersAsync(int max, string cursor, IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadMyFollowers);

            var (dbResults, nextCursor) = await _db.FollowsMe.GetAllFollowersAsync(DefaultMax(max), cursor);

            var result = new CursoredResult<string>()
            {
                Cursor = nextCursor,
                Results = dbResults
            };

            return result;
        }

        /// <summary>
        /// Gets a list of identities that follow me
        /// </summary>
        public async Task<CursoredResult<OdinId>> GetFollowersAsync(TargetDrive targetDrive, int max, string cursor, IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadMyFollowers);

            if (targetDrive.Type != SystemDriveConstants.ChannelDriveType)
            {
                throw new OdinClientException("Invalid Drive Type", OdinClientErrorCode.InvalidTargetDrive);
            }

            var (dbResults, nextCursor) = await _db.FollowsMe.GetFollowersAsync(DefaultMax(max), targetDrive.Alias, cursor);
            var result = new CursoredResult<OdinId>
            {
                Cursor = nextCursor,
                Results = dbResults.Select(ident => new OdinId(ident))
            };

            return result;
        }

        /// <summary>
        /// Gets followers who want notifications for all channels
        /// </summary>
        public async Task<CursoredResult<OdinId>> GetFollowersOfAllNotificationsAsync(int max, string cursor, IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadMyFollowers);

            var (dbResults, nextCursor) = await _db.FollowsMe.GetFollowersAsync(DefaultMax(max), Guid.Empty, cursor);

            var result = new CursoredResult<OdinId>()
            {
                Cursor = nextCursor,
                Results = dbResults.Select(ident => new OdinId(ident))
            };

            return result;
        }

        /// <summary>
        /// Gets a list of identities I follow
        /// </summary>
        public async Task<CursoredResult<string>> GetIdentitiesIFollowAsync(int max, string cursor, IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadWhoIFollow);

            var (dbResults, nextCursor) = await _db.ImFollowing.GetAllFollowersAsync(DefaultMax(max), cursor);
            var result = new CursoredResult<string>()
            {
                Cursor = nextCursor,
                Results = dbResults
            };
            return result;
        }

        public async Task<CursoredResult<string>> GetIdentitiesIFollowAsync(Guid driveAlias, int max, string cursor, IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadWhoIFollow);

            var drive = await _driveManager.GetDriveAsync(driveAlias, true);
            if (drive.TargetDriveInfo.Type != SystemDriveConstants.ChannelDriveType)
            {
                throw new OdinClientException("Invalid Drive Type", OdinClientErrorCode.InvalidTargetDrive);
            }

            var (dbResults, nextCursor) = await _db.ImFollowing.GetFollowersAsync(DefaultMax(max), driveAlias, cursor);
            return new CursoredResult<string>()
            {
                Cursor = nextCursor,
                Results = dbResults
            };
        }

        /// <summary>
        /// Allows my followers to write reactions. 
        /// </summary>
        public async Task<PermissionContext> CreateFollowerPermissionContextAsync(OdinId odinId, ClientAuthenticationToken token)
        {
            //Note: this check here is basically a replacement for the token
            // meaning - it is required to be an owner to follow an identity
            // so they will only be in the list if the owner added them
            var definition = await GetFollowerInternalAsync(odinId);
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
        public async Task<PermissionContext> CreatePermissionContextForIdentityIFollowAsync(OdinId odinId, ClientAuthenticationToken token)
        {
            // Note: this check here is basically a replacement for the token
            // meaning - it is required to be an owner to follow an identity
            // so they will only be in the list if the owner added them
            var definition = await GetIdentityIFollowInternalAsync(odinId);
            if (null == definition)
            {
                throw new OdinSecurityException($"Not following {odinId}");
            }

            var feedDrive = SystemDriveConstants.FeedDrive;
            var permissionSet = new PermissionSet(); //no permissions
            var sharedSecret = Guid.Empty.ToByteArray().ToSensitiveByteArray(); //TODO: what shared secret for this?

            var driveId = (await _driveManager.GetDriveIdByAliasAsync(feedDrive, true)).GetValueOrDefault();
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

        public async Task AssertTenantFollowsTheCallerAsync(IOdinContext odinContext)
        {
            var odinId = odinContext.GetCallerOdinIdOrFail();
            var definition = await GetIdentityIFollowInternalAsync(odinId);
            if (null == definition)
            {
                throw new OdinSecurityException($"Not following {odinId}");
            }
        }

        public async Task SynchronizeChannelFilesAsync(OdinId odinId, IOdinContext odinContext)
        {
            SensitiveByteArray sharedSecret = null;
            var icr = await _circleNetworkService.GetIcrAsync(odinId, odinContext);
            if (icr.IsConnected())
            {
                sharedSecret = icr.CreateClientAccessToken(odinContext.PermissionsContext.GetIcrKey()).SharedSecret;
            }

            await this.SynchronizeChannelFilesAsync(odinId, odinContext, sharedSecret: sharedSecret);
        }

        public async Task SynchronizeChannelFilesAsync(OdinId odinId, IOdinContext odinContext, SensitiveByteArray sharedSecret)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageFeed);

            var definition = await this.GetIdentityIFollowInternalAsync(odinId);
            if (definition == null) //not following
            {
                _logger.LogDebug("SynchronizeChannelFiles - not following the requested identity; no synchronization will occur");
                return;
            }

            var feedDriveId = odinContext.PermissionsContext.GetDriveId(SystemDriveConstants.FeedDrive);
            var channelDrives = await GetChannelsIFollow(odinId, odinContext, definition);

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

            var collection = await _peerDriveQueryService.GetBatchCollectionAsync(odinId, request, FileSystemType.Standard, odinContext);

            var patchedContext = sharedSecret == null
                ? odinContext
                : OdinContextUpgrades.PatchInSharedSecret(
                    odinContext,
                    sharedSecret: sharedSecret);

            foreach (var results in collection.Results)
            {
                if (results.InvalidDrive)
                {
                    _logger.LogDebug("SynchronizeChannelFiles - Skipping invalid drive found in the results named {drive}.", results.Name);
                    continue;
                }

                foreach (var dsr in results.SearchResults)
                {
                    try
                    {
                        await TryWriteFeedFileAsync(odinId, patchedContext, dsr, feedDriveId);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "SynchronizeChannelFiles - Failed while writing file with gtid:{gtid}.  Shared secret was {ss}",
                            dsr.FileMetadata.GlobalTransitId,
                            patchedContext.PermissionsContext.SharedSecretKey == null ? "null" : "not null");
                    }
                }
            }
        }

        ///
        private async Task TryWriteFeedFileAsync(OdinId odinId, IOdinContext odinContext, SharedSecretEncryptedFileHeader dsr,
            Guid feedDriveId)
        {
            if (dsr.FileMetadata.GlobalTransitId == null)
            {
                throw new OdinSystemException("File is missing a global transit id");
            }

            var sharedSecret = odinContext.PermissionsContext.SharedSecretKey;
            var keyHeader = KeyHeader.Empty();
            if (dsr.FileMetadata.IsEncrypted)
            {
                if (null == sharedSecret)
                {
                    throw new OdinSystemException("File is encrypted but shared secret is not set");
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
                OriginalAuthor = fm.OriginalAuthor,
                VersionTag = fm.VersionTag,
                ReactionPreview = fm.ReactionPreview,
                Created = fm.Created,
                Updated = fm.Updated,
                FileState = dsr.FileState,
                Payloads = fm.Payloads
            };


            var existingFile = await _standardFileSystem.Query.GetFileByGlobalTransitId(feedDriveId,
                dsr.FileMetadata.GlobalTransitId.GetValueOrDefault(), odinContext);

            if (null == existingFile)
            {
                _logger.LogDebug("SynchronizeChannelFiles - Writing new file with gtid:{gtid} and uid:{uid}",
                    newFileMetadata.GlobalTransitId.GetValueOrDefault(),
                    newFileMetadata.AppData.UniqueId.GetValueOrDefault());
                await _standardFileSystem.Storage.WriteNewFileToFeedDriveAsync(keyHeader, newFileMetadata, odinContext);
            }
            else
            {
                _logger.LogDebug("SynchronizeChannelFiles - updating existing file gtid:{gtid} and uid:{uid}",
                    newFileMetadata.GlobalTransitId.GetValueOrDefault(),
                    newFileMetadata.AppData.UniqueId.GetValueOrDefault());

                var file = new InternalDriveFileId()
                {
                    FileId = existingFile.FileId,
                    DriveId = feedDriveId
                };

                await _standardFileSystem.Storage.ReplaceFileMetadataOnFeedDrive(file, newFileMetadata, odinContext, bypassCallerCheck: true);
            }
        }

        private async Task<IEnumerable<PerimeterDriveData>> GetChannelsIFollow(OdinId odinId, IOdinContext odinContext,
            FollowerDefinition definition)
        {
            var channelDrives =
                await _peerDriveQueryService.GetDrivesByTypeAsync(odinId, SystemDriveConstants.ChannelDriveType, FileSystemType.Standard, odinContext);

            if (null == channelDrives)
            {
                _logger.LogInformation("SynchronizeChannelFiles - Failed to get channel drives from recipient");
                return new List<PerimeterDriveData>();
            }

            //filter the drives to those I want to see
            if (definition.NotificationType == FollowerNotificationType.SelectedChannels)
            {
                channelDrives = channelDrives.IntersectBy(definition.Channels, d => d.TargetDrive);
            }

            return channelDrives;
        }

        private int DefaultMax(int max)
        {
            return Math.Max(max, 10);
        }

        private IFollowerHttpClient CreateClient(OdinId odinId)
        {
            var httpClient = _httpClientFactory.CreateClient<IFollowerHttpClient>(odinId);
            return httpClient;
        }

        private async Task<FollowerDefinition> GetIdentityIFollowInternalAsync(OdinId odinId)
        {
            var dbRecords = await _db.ImFollowing.GetAsync(odinId);
            if (!dbRecords?.Any() ?? false)
            {
                return null;
            }

            if (dbRecords!.Any(f => odinId != f.identity))
            {
                throw new OdinSystemException($"Follower data for [{odinId}] is corrupt");
            }

            if (dbRecords.Any(r => r.driveId == Guid.Empty) && dbRecords.Count > 1)
            {
                throw new OdinSystemException($"Follower data for [{odinId}] is corrupt");
            }

            if (dbRecords.All(r => r.driveId == Guid.Empty))
            {
                return new FollowerDefinition
                {
                    OdinId = odinId,
                    NotificationType = FollowerNotificationType.AllNotifications
                };
            }

            return new FollowerDefinition
            {
                OdinId = odinId,
                NotificationType = FollowerNotificationType.SelectedChannels,
                Channels = dbRecords.Select(record => new TargetDrive()
                {
                    Alias = record.driveId,
                    Type = SystemDriveConstants.ChannelDriveType
                }).ToList()
            };
        }

        private async Task<FollowerDefinition> GetFollowerInternalAsync(OdinId odinId)
        {
            var dbRecords = await _db.FollowsMe.GetAsync(odinId);
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

            return result;
        }
    }
}