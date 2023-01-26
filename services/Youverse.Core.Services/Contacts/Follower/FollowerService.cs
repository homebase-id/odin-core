using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.EncryptionKeyService;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Contacts.Follower
{
    /// <summary/>
    public class FollowerService
    {
        private readonly ITenantSystemStorage _tenantStorage;
        private readonly IDriveService _driveService;
        private readonly IDotYouHttpClientFactory _httpClientFactory;
        private readonly IPublicKeyService _rsaPublicKeyService;
        private readonly TenantContext _tenantContext;
        private readonly DotYouContextAccessor _contextAccessor;

        public FollowerService(ITenantSystemStorage tenantStorage, IDriveService driveService, IDotYouHttpClientFactory httpClientFactory, IPublicKeyService rsaPublicKeyService,
            TenantContext tenantContext, DotYouContextAccessor contextAccessor)
        {
            _tenantStorage = tenantStorage;
            _driveService = driveService;
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

            if (request.NotificationType == FollowerNotificationType.SelectedChannels)
            {
                Guard.Argument(request.Channels, nameof(request.Channels)).NotNull().NotEmpty().Require(list => list.All(c => c.Type == SystemDriveConstants.ChannelDriveType));
            }

            var followRequest = new FollowRequest()
            {
                DotYouId = _tenantContext.HostDotYouId,
                NotificationType = request.NotificationType,
                Channels = request.Channels
            };

            var payloadBytes = DotYouSystemSerializer.Serialize(followRequest).ToUtf8ByteArray();
            var rsaEncryptedPayload = await _rsaPublicKeyService.EncryptPayloadForRecipient(request.DotYouId, payloadBytes);
            var client = CreateClient((DotYouIdentity)request.DotYouId);
            var response = await client.Follow(rsaEncryptedPayload);

            if (response.IsSuccessStatusCode == false)
            {
                //public key might be invalid, destroy the cache item
                await _rsaPublicKeyService.InvalidatePublicKey((DotYouIdentity)request.DotYouId);

                rsaEncryptedPayload = await _rsaPublicKeyService.EncryptPayloadForRecipient(request.DotYouId, payloadBytes);
                response = await client.Follow(rsaEncryptedPayload);

                //round 2, fail all together
                if (response.IsSuccessStatusCode == false)
                {
                    throw new YouverseRemoteIdentityException("Remote Server failed to accept follow");
                }
            }

            _tenantStorage.WhoIFollow.DeleteFollower(request.DotYouId);
            if (request.NotificationType == FollowerNotificationType.AllNotifications)
            {
                _tenantStorage.WhoIFollow.InsertFollower(request.DotYouId, null);
            }
            else
            {
                foreach (var channel in request.Channels)
                {
                    //TODO: im using alias here beacuse driveid on the follower's identity does make sense
                    //this works because all drives must be of type :channel
                    _tenantStorage.WhoIFollow.InsertFollower(request.DotYouId, channel.Alias);
                }
            }
        }

        /// <summary>
        /// Notifies the recipient you are no longer following them.  This means they
        /// should no longer send you updates/notifications
        /// </summary>
        public async Task Unfollow(string recipient)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var client = CreateClient((DotYouIdentity)recipient);
            var response = await client.Unfollow();

            if (!response.IsSuccessStatusCode)
            {
                throw new YouverseRemoteIdentityException("Failed to unfollow");
            }

            _tenantStorage.WhoIFollow.DeleteFollower(recipient);
        }

        public async Task<CursoredResult<string>> GetFollowers(string cursor)
        {
            if (!string.IsNullOrEmpty(cursor))
            {
                throw new NotImplementedException("cursor not yet supported");
            }

            _contextAccessor.GetCurrent().PermissionsContext.HasPermission(PermissionKeys.ReadMyFollowers);

            //get all channel drives
            var channelDriveResult = await _driveService.GetDrives(SystemDriveConstants.ChannelDriveType, PageOptions.All);
            var readableChannelDrives = channelDriveResult.Results.Where(drive => _contextAccessor.GetCurrent().PermissionsContext.HasDrivePermission(drive.Id, DrivePermission.Read));

            var count = 10000;
            var buffer = new List<string>();
            foreach (var drive in readableChannelDrives)
            {
                var list = await this.GetFollowers(drive.Id, cursor: string.Empty);
                buffer.AddRange(list.Results.Except(buffer)); //exclude followers we already have
            }

            return new CursoredResult<string>()
            {
                Cursor = "",
                Results = buffer
            };
        }

        /// <summary>
        /// Gets a list of identities that follow me
        /// </summary>
        public async Task<CursoredResult<string>> GetFollowers(Guid driveId, string cursor)
        {
            _contextAccessor.GetCurrent().PermissionsContext.HasPermission(PermissionKeys.ReadMyFollowers);

            var drive = await _driveService.GetDrive(driveId, true);
            if (drive.TargetDriveInfo.Type != SystemDriveConstants.ChannelDriveType)
            {
                throw new YouverseClientException("Invalid Drive Type", YouverseClientErrorCode.InvalidTargetDrive);
            }

            var count = 10000;
            var dbResults = _tenantStorage.Followers.GetFollowers(count, driveId, cursor);
            var result = new CursoredResult<string>()
            {
                Cursor = dbResults.Last(),
                Results = dbResults
            };

            return result;
        }

        /// <summary>
        /// Gets a list of identities I follow
        /// </summary>
        public async Task<CursoredResult<string>> GetIdentitiesIFollow(string cursor)
        {
            if (!string.IsNullOrEmpty(cursor))
            {
                throw new NotImplementedException("cursor not yet supported");
            }

            _contextAccessor.GetCurrent().PermissionsContext.HasPermission(PermissionKeys.ReadWhoIFollow);

            //get all channel drives
            var channelDriveResult = await _driveService.GetDrives(SystemDriveConstants.ChannelDriveType, PageOptions.All);
            var readableChannelDrives = channelDriveResult.Results.Where(drive => _contextAccessor.GetCurrent().PermissionsContext.HasDrivePermission(drive.Id, DrivePermission.Read));

            var count = 10000;
            var buffer = new List<string>();
            foreach (var drive in readableChannelDrives)
            {
                var list = await this.GetIdentitiesIFollow(drive.Id, cursor: string.Empty);
                buffer.AddRange(list.Results.Except(buffer)); //exclude followers we already have
            }

            return new CursoredResult<string>()
            {
                Cursor = "",
                Results = buffer
            };
        }

        public async Task<CursoredResult<string>> GetIdentitiesIFollow(Guid driveId, string cursor)
        {
            _contextAccessor.GetCurrent().PermissionsContext.HasPermission(PermissionKeys.ReadWhoIFollow);

            var drive = await _driveService.GetDrive(driveId, true);
            if (drive.TargetDriveInfo.Type != SystemDriveConstants.ChannelDriveType)
            {
                throw new YouverseClientException("Invalid Drive Type", YouverseClientErrorCode.InvalidTargetDrive);
            }

            var count = 10000;
            var dbResults = _tenantStorage.WhoIFollow.GetFollowers(count, driveId, cursor);
            var result = new CursoredResult<string>()
            {
                Cursor = dbResults.Last(),
                Results = dbResults
            };

            return result;
        }

        ///
        private IFollowerHttpClient CreateClient(DotYouIdentity dotYouId)
        {
            var httpClient = _httpClientFactory.CreateClient<IFollowerHttpClient>((DotYouIdentity)dotYouId);
            return httpClient;
        }
    }
}