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
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
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
        private readonly TenantSystemStorage _tenantStorage;
        private readonly ILogger<FollowerService> _logger;
        private readonly DriveManager _driveManager;
        private readonly IOdinHttpClientFactory _httpClientFactory;
        private readonly PublicPrivateKeyService _publicPrivatePublicKeyService;
        private readonly TenantContext _tenantContext;

        private readonly StandardFileSystem _standardFileSystem;
        private readonly PeerDriveQueryService _peerDriveQueryService;
        private readonly CircleNetworkService _circleNetworkService;

        private const int MaxRecordsPerChannel = 100; //TODO:config


        public FollowerService(TenantSystemStorage tenantStorage,
            ILogger<FollowerService> logger,
            DriveManager driveManager,
            IOdinHttpClientFactory httpClientFactory,
            PublicPrivateKeyService publicPrivatePublicKeyService,
            TenantContext tenantContext,
            StandardFileSystem standardFileSystem, PeerDriveQueryService peerDriveQueryService,
            CircleNetworkService circleNetworkService)
        {
            _tenantStorage = tenantStorage;
            _logger = logger;
            _driveManager = driveManager;
            _httpClientFactory = httpClientFactory;
            _publicPrivatePublicKeyService = publicPrivatePublicKeyService;
            _tenantContext = tenantContext;

            _standardFileSystem = standardFileSystem;
            _peerDriveQueryService = peerDriveQueryService;
            _circleNetworkService = circleNetworkService;
        }

        /// <summary>
        /// Establishes a follower connection with the recipient
        /// </summary>
        public async Task Follow(FollowRequest request, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageFeed);

            var identityToFollow = (OdinId)request.OdinId;

            if (odinContext.Caller.OdinId == identityToFollow)
            {
                throw new OdinClientException("Cannot follow yourself; at least not in this dimension because that would be like chasing your own tail",
                    OdinClientErrorCode.InvalidRecipient);
            }

            var existingFollow = await this.GetIdentityIFollowInternal(identityToFollow, cn);
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

            async Task<ApiResponse<HttpContent>> TryFollow()
            {
                var rsaEncryptedPayload = await _publicPrivatePublicKeyService.RsaEncryptPayloadForRecipient(
                    PublicPrivateKeyType.OfflineKey, identityToFollow, json.ToUtf8ByteArray(), cn);
                var client = CreateClient(identityToFollow);
                var response = await client.Follow(rsaEncryptedPayload);
                return response;
            }

            if ((await TryFollow()).IsSuccessStatusCode == false)
            {
                //public key might be invalid, destroy the cache item
                await _publicPrivatePublicKeyService.InvalidateRecipientRsaPublicKey(identityToFollow, cn);

                //round 2, fail all together
                if ((await TryFollow()).IsSuccessStatusCode == false)
                {
                    throw new OdinRemoteIdentityException("Remote Server failed to accept follow");
                }
            }

            cn.CreateCommitUnitOfWork(() =>
            {
                //delete all records and update according to the latest follow request.
                _tenantStorage.WhoIFollow.DeleteByIdentity(cn, identityToFollow);
                if (request.NotificationType == FollowerNotificationType.AllNotifications)
                {
                    _tenantStorage.WhoIFollow.Insert(cn, new ImFollowingRecord()
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
                        _tenantStorage.WhoIFollow.Insert(cn, new ImFollowingRecord()
                            { identity = identityToFollow, driveId = channel.Alias });
                    }
                }
            });

            if (request.SynchronizeFeedHistoryNow)
            {
                await SynchronizeChannelFiles(identityToFollow, odinContext, cn);
            }
        }

        /// <summary>
        /// Notifies the recipient you are no longer following them.  This means they
        /// should no longer send you updates/notifications
        /// </summary>
        public async Task Unfollow(OdinId recipient, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageFeed);

            var client = CreateClient(recipient);
            var response = await client.Unfollow();

            if (!response.IsSuccessStatusCode)
            {
                throw new OdinRemoteIdentityException("Failed to unfollow");
            }

            _tenantStorage.WhoIFollow.DeleteByIdentity(cn, recipient);
        }

        public async Task<FollowerDefinition> GetFollower(OdinId odinId, IOdinContext odinContext, DatabaseConnection cn)
        {
            //a follower is allowed to read their own configuration
            if (odinId != odinContext.Caller.OdinId)
            {
                odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadMyFollowers);
            }

            return await GetFollowerInternal(odinId, cn);
        }

        /// <summary>
        /// Gets the details (channels, etc.) of an identity that you follow.
        /// </summary>
        public async Task<FollowerDefinition> GetIdentityIFollow(OdinId odinId, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadWhoIFollow);
            return await GetIdentityIFollowInternal(odinId, cn);
        }

        public async Task<CursoredResult<string>> GetAllFollowers(int max, string cursor, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadMyFollowers);

            var dbResults = _tenantStorage.Followers.GetAllFollowers(cn, DefaultMax(max), cursor, out var nextCursor);

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
        public async Task<CursoredResult<OdinId>> GetFollowers(TargetDrive targetDrive, int max, string cursor, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadMyFollowers);

            if (targetDrive.Type != SystemDriveConstants.ChannelDriveType)
            {
                throw new OdinClientException("Invalid Drive Type", OdinClientErrorCode.InvalidTargetDrive);
            }

            var dbResults = _tenantStorage.Followers.GetFollowers(cn, DefaultMax(max), targetDrive.Alias, cursor, out var nextCursor);
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
        public async Task<CursoredResult<OdinId>> GetFollowersOfAllNotifications(int max, string cursor, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadMyFollowers);

            var dbResults = _tenantStorage.Followers.GetFollowers(cn, DefaultMax(max), Guid.Empty, cursor, out var nextCursor);

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
        public async Task<CursoredResult<string>> GetIdentitiesIFollow(int max, string cursor, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadWhoIFollow);

            var dbResults = _tenantStorage.WhoIFollow.GetAllFollowers(cn, DefaultMax(max), cursor, out var nextCursor);
            var result = new CursoredResult<string>()
            {
                Cursor = nextCursor,
                Results = dbResults
            };
            return await Task.FromResult(result);
        }

        public async Task<CursoredResult<string>> GetIdentitiesIFollow(Guid driveAlias, int max, string cursor, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadWhoIFollow);

            var drive = await _driveManager.GetDrive(driveAlias, cn, true);
            if (drive.TargetDriveInfo.Type != SystemDriveConstants.ChannelDriveType)
            {
                throw new OdinClientException("Invalid Drive Type", OdinClientErrorCode.InvalidTargetDrive);
            }

            var dbResults = _tenantStorage.WhoIFollow.GetFollowers(cn, DefaultMax(max), driveAlias, cursor, out var nextCursor);
            return new CursoredResult<string>()
            {
                Cursor = nextCursor,
                Results = dbResults
            };
        }

        /// <summary>
        /// Allows my followers to write reactions. 
        /// </summary>
        public async Task<PermissionContext> CreateFollowerPermissionContext(OdinId odinId, ClientAuthenticationToken token, DatabaseConnection cn)
        {
            //Note: this check here is basically a replacement for the token
            // meaning - it is required to be an owner to follow an identity
            // so they will only be in the list if the owner added them
            var definition = await GetFollowerInternal(odinId, cn);
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
        public async Task<PermissionContext> CreatePermissionContextForIdentityIFollow(OdinId odinId, ClientAuthenticationToken token, DatabaseConnection cn)
        {
            // Note: this check here is basically a replacement for the token
            // meaning - it is required to be an owner to follow an identity
            // so they will only be in the list if the owner added them
            var definition = await GetIdentityIFollowInternal(odinId, cn);
            if (null == definition)
            {
                throw new OdinSecurityException($"Not following {odinId}");
            }

            var feedDrive = SystemDriveConstants.FeedDrive;
            var permissionSet = new PermissionSet(); //no permissions
            var sharedSecret = Guid.Empty.ToByteArray().ToSensitiveByteArray(); //TODO: what shared secret for this?

            var driveId = (await _driveManager.GetDriveIdByAlias(feedDrive, cn, true)).GetValueOrDefault();
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

        public async Task AssertTenantFollowsTheCaller(IOdinContext odinContext, DatabaseConnection cn)
        {
            var odinId = odinContext.GetCallerOdinIdOrFail();
            var definition = await this.GetIdentityIFollowInternal(odinId, cn);
            if (null == definition)
            {
                throw new OdinSecurityException($"Not following {odinId}");
            }
        }

        public async Task SynchronizeChannelFiles(OdinId odinId, IOdinContext odinContext, DatabaseConnection cn)
        {
            SensitiveByteArray sharedSecret = null;
            var icr = await _circleNetworkService.GetIdentityConnectionRegistration(odinId, odinContext, cn);
            if (icr.IsConnected())
            {
                sharedSecret = icr.CreateClientAccessToken(odinContext.PermissionsContext.GetIcrKey()).SharedSecret;
            }

            await this.SynchronizeChannelFiles(odinId, odinContext, cn, sharedSecret: sharedSecret);
        }

        public async Task SynchronizeChannelFiles(OdinId odinId, IOdinContext odinContext, DatabaseConnection cn, SensitiveByteArray sharedSecret)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageFeed);

            var definition = await this.GetIdentityIFollowInternal(odinId, cn);
            if (definition == null) //not following
            {
                _logger.LogDebug("SynchronizeChannelFiles - not following the requested identity; no synchronization will occur");
                return;
            }

            var feedDriveId = odinContext.PermissionsContext.GetDriveId(SystemDriveConstants.FeedDrive);
            var channelDrives = await GetChannelsIFollow(odinId, odinContext, cn, definition);

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

            var collection = await _peerDriveQueryService.GetBatchCollection(odinId, request, FileSystemType.Standard, odinContext, cn);

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
                        await TryWriteFeedFile(odinId, patchedContext, cn, dsr, feedDriveId);
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
        private async Task TryWriteFeedFile(OdinId odinId, IOdinContext odinContext, DatabaseConnection cn, SharedSecretEncryptedFileHeader dsr,
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
                dsr.FileMetadata.GlobalTransitId.GetValueOrDefault(), odinContext, cn);

            if (null == existingFile)
            {
                _logger.LogDebug("SynchronizeChannelFiles - Writing new file with gtid:{gtid} and uid:{uid}",
                    newFileMetadata.GlobalTransitId.GetValueOrDefault(),
                    newFileMetadata.AppData.UniqueId.GetValueOrDefault());
                await _standardFileSystem.Storage.WriteNewFileToFeedDrive(keyHeader, newFileMetadata, odinContext, cn);
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

                await _standardFileSystem.Storage.ReplaceFileMetadataOnFeedDrive(file, newFileMetadata, odinContext, cn, bypassCallerCheck: true);
            }
        }

        private async Task<IEnumerable<PerimeterDriveData>> GetChannelsIFollow(OdinId odinId, IOdinContext odinContext, DatabaseConnection cn,
            FollowerDefinition definition)
        {
            var channelDrives =
                await _peerDriveQueryService.GetDrivesByType(odinId, SystemDriveConstants.ChannelDriveType, FileSystemType.Standard, odinContext, cn);

            if (null == channelDrives)
            {
                _logger.LogWarning("SynchronizeChannelFiles - Failed to get channel drives from recipient");
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

        private Task<FollowerDefinition> GetIdentityIFollowInternal(OdinId odinId, DatabaseConnection cn)
        {
            var dbRecords = _tenantStorage.WhoIFollow.Get(cn, odinId);
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

        private async Task<FollowerDefinition> GetFollowerInternal(OdinId odinId, DatabaseConnection cn)
        {
            var dbRecords = _tenantStorage.Followers.Get(cn, odinId);
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