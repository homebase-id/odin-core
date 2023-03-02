using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.Management;
using Youverse.Core.Services.EncryptionKeyService;
using Youverse.Core.Storage;
using Youverse.Core.Storage.SQLite.IdentityDatabase;

namespace Youverse.Core.Services.DataSubscription.Follower
{
    /// <summary/>
    public class FollowerService
    {
        private readonly ITenantSystemStorage _tenantStorage;
        private readonly DriveManager _driveManager;
        private readonly IDotYouHttpClientFactory _httpClientFactory;
        private readonly IPublicKeyService _rsaPublicKeyService;
        private readonly TenantContext _tenantContext;
        private readonly DotYouContextAccessor _contextAccessor;

        public FollowerService(ITenantSystemStorage tenantStorage, DriveManager driveManager, IDotYouHttpClientFactory httpClientFactory, IPublicKeyService rsaPublicKeyService,
            TenantContext tenantContext, DotYouContextAccessor contextAccessor)
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
                throw new YouverseClientException("Cannot follow yourself; at least not in this dimension because that would be like chasing your own tail", YouverseClientErrorCode.InvalidRecipient);
            }

            //TODO: use the exchange grant service to create the access reg and CAT 
            // var accessToken = await _appRegistrationService.RegisterClientRaw(SystemAppConstants.FeedAppId, $"Feed App Client for {request.OdinId}");

            var followRequest = new PerimterFollowRequest()
            {
                OdinId = _tenantContext.HostOdinId,
                NotificationType = request.NotificationType,
                Channels = request.Channels,
                // PortableClientAuthToken = accessToken.ToPortableBytes()
            };

            // var payloadBytes = DotYouSystemSerializer.Serialize(followRequest).ToUtf8ByteArray();
            var json = DotYouSystemSerializer.Serialize(followRequest);
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
                    throw new YouverseRemoteIdentityException("Remote Server failed to accept follow");
                }
            }

            using (_tenantStorage.CreateCommitUnitOfWork())
            {
                //delete all records and update according to the latest follow request.
                _tenantStorage.WhoIFollow.DeleteFollower(new OdinId(request.OdinId));
                if (request.NotificationType == FollowerNotificationType.AllNotifications)
                {
                    _tenantStorage.WhoIFollow.Insert(new ImFollowingItem() { identity = new OdinId(request.OdinId), driveId = Guid.Empty });
                }

                if (request.NotificationType == FollowerNotificationType.SelectedChannels)
                {
                    if (request.Channels.Any(c => c.Type != SystemDriveConstants.ChannelDriveType))
                    {
                        throw new YouverseClientException("Only drives of type channel can be followed", YouverseClientErrorCode.InvalidTargetDrive);
                    }

                    //use the alias because we don't most likely will not have the channel on the callers identity
                    foreach (var channel in request.Channels)
                    {
                        _tenantStorage.WhoIFollow.Insert(new ImFollowingItem() { identity = new OdinId(request.OdinId), driveId = channel.Alias });
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
                throw new YouverseRemoteIdentityException("Failed to unfollow");
            }

            _tenantStorage.WhoIFollow.DeleteFollower(recipient);
        }

        public async Task<FollowerDefinition> GetFollower(OdinId odinId)
        {
            _contextAccessor.GetCurrent().PermissionsContext.HasPermission(PermissionKeys.ReadWhoIFollow);

            Guard.Argument(odinId, nameof(odinId)).Require(d => d.HasValue());

            var dbRecords = _tenantStorage.Followers.Get(odinId);
            if (!dbRecords?.Any() ?? false)
            {
                return null;
            }

            if (dbRecords!.Any(f => odinId != (OdinId)f.identity))
            {
                throw new YouverseSystemException($"Follower data for [{odinId}] is corrupt");
            }

            if (dbRecords.Any(r => r.driveId == Guid.Empty) && dbRecords.Count > 1)
            {
                throw new YouverseSystemException($"Follower data for [{odinId}] is corrupt");
            }

            if (dbRecords.All(r => r.driveId == Guid.Empty))
            {
                return new FollowerDefinition()
                {
                    OdinId = odinId,
                    NotificationType = FollowerNotificationType.AllNotifications
                };
            }

            return new FollowerDefinition()
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
            
            return new CursoredResult<string>()
            {
                Cursor = nextCursor,
                Results = dbResults
            };
        }

        /// <summary>
        /// Gets a list of identities that follow me
        /// </summary>
        public async Task<CursoredResult<string>> GetFollowers(Guid driveId, int max, string cursor)
        {
            _contextAccessor.GetCurrent().PermissionsContext.HasPermission(PermissionKeys.ReadMyFollowers);

            var drive = await _driveManager.GetDrive(driveId, true);
            if (drive.TargetDriveInfo.Type != SystemDriveConstants.ChannelDriveType)
            {
                throw new YouverseClientException("Invalid Drive Type", YouverseClientErrorCode.InvalidTargetDrive);
            }

            var dbResults = _tenantStorage.Followers.GetFollowers(DefaultMax(max), driveId, cursor, out var nextCursor);
            var result = new CursoredResult<string>()
            {
                Cursor = nextCursor,
                Results = dbResults
            };

            return result;
        }

        /// <summary>
        /// Gets followers who want notifications for all channels
        /// </summary>
        public async Task<CursoredResult<string>> GetFollowersOfAllNotifications(int max, string cursor)
        {
            _contextAccessor.GetCurrent().PermissionsContext.HasPermission(PermissionKeys.ReadMyFollowers);

            var dbResults = _tenantStorage.Followers.GetFollowers(DefaultMax(max), Guid.Empty, cursor, out var nextCursor);
            return new CursoredResult<string>()
            {
                Cursor = nextCursor,
                Results = dbResults
            };
        }

        /// <summary>
        /// Gets a list of identities I follow
        /// </summary>
        public async Task<CursoredResult<string>> GetIdentitiesIFollow(int max, string cursor)
        {
            _contextAccessor.GetCurrent().PermissionsContext.HasPermission(PermissionKeys.ReadWhoIFollow);

            var dbResults = _tenantStorage.WhoIFollow.GetAllFollowers(DefaultMax(max), cursor, out var nextCursor);
            return new CursoredResult<string>()
            {
                Cursor = nextCursor,
                Results = dbResults
            };
        }

        public async Task<CursoredResult<string>> GetIdentitiesIFollow(Guid driveAlias, int max, string cursor)
        {
            _contextAccessor.GetCurrent().PermissionsContext.HasPermission(PermissionKeys.ReadWhoIFollow);

            var drive = await _driveManager.GetDrive(driveAlias, true);
            if (drive.TargetDriveInfo.Type != SystemDriveConstants.ChannelDriveType)
            {
                throw new YouverseClientException("Invalid Drive Type", YouverseClientErrorCode.InvalidTargetDrive);
            }

            var dbResults = _tenantStorage.WhoIFollow.GetFollowers(DefaultMax(max), driveAlias, cursor, out var nextCursor);
            return new CursoredResult<string>()
            {
                Cursor = nextCursor,
                Results = dbResults
            };
        }

        public async Task<PermissionContext> CreatePermissionContext(OdinId odinId, ClientAuthenticationToken token)
        {
            //Note: this check here is basically a replacement for the token
            // meaning - it is required to be an owner to follow an identity
            // so they will only be in the list if the owner added them
            var definition = await GetIdentityIFollowInternal(odinId);
            if (null == definition)
            {
                throw new YouverseSecurityException($"Not following {odinId}");
            }

            var targetDrive = SystemDriveConstants.FeedDrive;
            var permissionSet = new PermissionSet(); //no permissions
            var sharedSecret = Guid.Empty.ToByteArray().ToSensitiveByteArray();

            var driveGrants = new List<DriveGrant>()
            {
                new DriveGrant()
                {
                    DriveId = (await _driveManager.GetDriveIdByAlias(targetDrive, true)).GetValueOrDefault(),
                    KeyStoreKeyEncryptedStorageKey = null,
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.Write
                    }
                }
            };

            var groups = new Dictionary<string, PermissionGroup>()
            {
                { "data_subscriber", new PermissionGroup(permissionSet, driveGrants, sharedSecret) }
            };

            return new PermissionContext(groups, null);
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
                throw new YouverseSystemException($"Follower data for [{odinId}] is corrupt");
            }

            if (dbRecords.Any(r => r.driveId == Guid.Empty) && dbRecords.Count > 1)
            {
                throw new YouverseSystemException($"Follower data for [{odinId}] is corrupt");
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
    }
}