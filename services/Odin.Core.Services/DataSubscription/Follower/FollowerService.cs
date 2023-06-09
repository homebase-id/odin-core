using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Services.DataSubscription.Follower
{
    /// <summary/>
    public class FollowerService
    {
        private readonly TenantSystemStorage _tenantStorage;
        private readonly DriveManager _driveManager;
        private readonly IOdinHttpClientFactory _httpClientFactory;
        private readonly RsaKeyService _rsaPublicKeyService;
        private readonly TenantContext _tenantContext;
        private readonly OdinContextAccessor _contextAccessor;

        public FollowerService(TenantSystemStorage tenantStorage, DriveManager driveManager, IOdinHttpClientFactory httpClientFactory,
            RsaKeyService rsaPublicKeyService,
            TenantContext tenantContext, OdinContextAccessor contextAccessor)
        {
            _tenantStorage = tenantStorage;
            _driveManager = driveManager;
            _httpClientFactory = httpClientFactory;
            _rsaPublicKeyService = rsaPublicKeyService;
            _tenantContext = tenantContext;
            _contextAccessor = contextAccessor;
        }

        /// <summary>
        /// Establishes a follower connection with the recipient
        /// </summary>
        public async Task Follow(FollowRequest request)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            if (_contextAccessor.GetCurrent().Caller.OdinId == (OdinId)request.OdinId)
            {
                throw new OdinClientException("Cannot follow yourself; at least not in this dimension because that would be like chasing your own tail",
                    OdinClientErrorCode.InvalidRecipient);
            }

            //TODO: use the exchange grant service to create the access reg and CAT 

            var followRequest = new PerimterFollowRequest()
            {
                OdinId = _tenantContext.HostOdinId,
                NotificationType = request.NotificationType,
                Channels = request.Channels,
                // PortableClientAuthToken = accessToken.ToPortableBytes()
            };

            // var payloadBytes = DotYouSystemSerializer.Serialize(followRequest).ToUtf8ByteArray();
            var json = OdinSystemSerializer.Serialize(followRequest);
            var rsaEncryptedPayload = await _rsaPublicKeyService.EncryptPayloadForRecipient(request.OdinId, json.ToUtf8ByteArray());
            var client = CreateClient((OdinId)request.OdinId);
            var response = await client.Follow(rsaEncryptedPayload);

            if (response.IsSuccessStatusCode == false)
            {
                //public key might be invalid, destroy the cache item
                await _rsaPublicKeyService.InvalidatePublicKey((OdinId)request.OdinId);

                rsaEncryptedPayload = await _rsaPublicKeyService.EncryptPayloadForRecipient(request.OdinId, json.ToUtf8ByteArray());
                response = await client.Follow(rsaEncryptedPayload);

                //round 2, fail all together
                if (response.IsSuccessStatusCode == false)
                {
                    throw new OdinRemoteIdentityException("Remote Server failed to accept follow");
                }
            }

            using (_tenantStorage.CreateCommitUnitOfWork())
            {
                //delete all records and update according to the latest follow request.
                _tenantStorage.WhoIFollow.DeleteByIdentity(new OdinId(request.OdinId));
                if (request.NotificationType == FollowerNotificationType.AllNotifications)
                {
                    _tenantStorage.WhoIFollow.Insert(new ImFollowingRecord() { identity = new OdinId(request.OdinId), driveId = Guid.Empty });
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
                        _tenantStorage.WhoIFollow.Insert(new ImFollowingRecord() { identity = new OdinId(request.OdinId), driveId = channel.Alias });
                    }
                }
            }
        }

        /// <summary>
        /// Notifies the recipient you are no longer following them.  This means they
        /// should no longer send you updates/notifications
        /// </summary>
        public async Task Unfollow(OdinId recipient)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

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
                _contextAccessor.GetCurrent().PermissionsContext.HasPermission(PermissionKeys.ReadWhoIFollow);
            }

            return await GetFollowerInternal(odinId);
        }

        /// <summary>
        /// Gets the details (channels, etc.) of an identity that you follow.
        /// </summary>
        public Task<FollowerDefinition> GetIdentityIFollow(OdinId odinId)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
            return GetIdentityIFollowInternal(odinId);
        }

        public async Task<CursoredResult<string>> GetAllFollowers(int max, string cursor)
        {
            _contextAccessor.GetCurrent().PermissionsContext.HasPermission(PermissionKeys.ReadMyFollowers);

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
            _contextAccessor.GetCurrent().PermissionsContext.HasPermission(PermissionKeys.ReadMyFollowers);
            
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
            _contextAccessor.GetCurrent().PermissionsContext.HasPermission(PermissionKeys.ReadMyFollowers);

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
            _contextAccessor.GetCurrent().PermissionsContext.HasPermission(PermissionKeys.ReadWhoIFollow);

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
            _contextAccessor.GetCurrent().PermissionsContext.HasPermission(PermissionKeys.ReadWhoIFollow);

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

            var driveGrants = new List<DriveGrant>()
            {
            };

            var groups = new Dictionary<string, PermissionGroup>()
            {
                { "follower", new PermissionGroup(permissionSet, driveGrants, sharedSecret) }
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
                { "data_subscriber", new PermissionGroup(permissionSet, driveGrants, null) }
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

        ///
        private int DefaultMax(int max)
        {
            return Math.Max(max, 10);
        }

        private IFollowerHttpClient CreateClient(OdinId odinId)
        {
            var httpClient = _httpClientFactory.CreateClient<IFollowerHttpClient>((OdinId)odinId);
            return httpClient;
        }

        private Task<FollowerDefinition> GetIdentityIFollowInternal(OdinId odinId)
        {
            Guard.Argument(odinId, nameof(odinId)).Require(d => d.HasValue());

            var dbRecords = _tenantStorage.WhoIFollow.Get(odinId);
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