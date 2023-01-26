using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using Dawn;
using MediatR.Pipeline;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.EncryptionKeyService;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Contacts.Follower
{
    public class FollowRequest
    {
        public string DotYouId { get; set; }
        public FollowerNotificationType NotificationType { get; set; }
        public IEnumerable<TargetDrive> Channels { get; set; }
    }

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
        public async Task Follow(string recipient, FollowerNotificationType notificationType, List<TargetDrive> channels)
        {
            if (notificationType == FollowerNotificationType.SelectedChannels)
            {
                Guard.Argument(channels, nameof(channels)).NotNull().NotEmpty().Require(list => list.All(c => c.Type == SystemDriveConstants.ChannelDriveType));
            }

            var followRequest = new FollowRequest()
            {
                DotYouId = _tenantContext.HostDotYouId,
                NotificationType = notificationType,
                Channels = channels
            };

            var payloadBytes = DotYouSystemSerializer.Serialize(followRequest).ToUtf8ByteArray();
            var rsaEncryptedPayload = await _rsaPublicKeyService.EncryptPayloadForRecipient(recipient, payloadBytes);
            var client = CreateClient((DotYouIdentity)recipient);
            var response = await client.Follow(rsaEncryptedPayload);

            if (response.IsSuccessStatusCode == false)
            {
                //public key might be invalid, destroy the cache item
                await _rsaPublicKeyService.InvalidatePublicKey((DotYouIdentity)recipient);

                rsaEncryptedPayload = await _rsaPublicKeyService.EncryptPayloadForRecipient(recipient, payloadBytes);
                response = await client.Follow(rsaEncryptedPayload);

                //round 2, fail all together
                if (response.IsSuccessStatusCode == false)
                {
                    throw new YouverseRemoteIdentityException("Remote Server failed to accept follow");
                }
            }

            _tenantStorage.FollowedIdentities.DeleteFollower(recipient);
            if (notificationType == FollowerNotificationType.AllNotifications)
            {
                _tenantStorage.FollowedIdentities.InsertFollower(recipient, null);
            }
            else
            {
                foreach (var channel in channels)
                {
                    //TODO: im using alias here beacuse driveid on the follower's identity does make sense
                    //this works because all drives must be of type :channel
                    _tenantStorage.FollowedIdentities.InsertFollower(recipient, channel.Alias);
                }
            }
        }

        /// <summary>
        /// Notifies the recipient you are no longer following them.  This means they
        /// should no longer send you updates/notifications
        /// </summary>
        public async Task Unfollow(string recipient)
        {
            var client = CreateClient((DotYouIdentity)recipient);
            var response = await client.Unfollow();

            if (!response.IsSuccessStatusCode)
            {
                throw new YouverseRemoteIdentityException("Failed to unfollow");
            }
            
            _tenantStorage.FollowedIdentities.DeleteFollower(recipient);
        }
        /// <summary>
        /// Accepts the new or exiting follower by upserting a record to ensure
        /// the follower is notified of content changes.
        /// </summary>
        public Task AcceptFollower(FollowRequest request)
        {
            Guard.Argument(request, nameof(request)).NotNull();
            Guard.Argument(request.DotYouId, nameof(request.DotYouId)).NotNull().NotEmpty();
            DotYouIdentity.Validate(request.DotYouId);

            if (request.NotificationType == FollowerNotificationType.SelectedChannels)
            {
                Guard.Argument(request.Channels, nameof(request.Channels)).NotNull().NotEmpty().Require(channels => channels.All(c => c.Type == SystemDriveConstants.ChannelDriveType));

                var driveIdList = request.Channels.Select(chan => _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(chan));

                _tenantStorage.Followers.DeleteFollower(request.DotYouId);
                foreach (var driveId in driveIdList)
                {
                    _tenantStorage.Followers.InsertFollower(request.DotYouId, driveId);
                }

                return Task.CompletedTask;
            }

            if (request.NotificationType == FollowerNotificationType.AllNotifications)
            {
                _tenantStorage.Followers.DeleteFollower(request.DotYouId);
                _tenantStorage.Followers.InsertFollower(request.DotYouId, null);
            }

            return Task.CompletedTask;
        }

        public Task AcceptUnfollowRequest()
        {
            var follower = _contextAccessor.GetCurrent().Caller.DotYouId;
            _tenantStorage.Followers.DeleteFollower(follower);
            return Task.CompletedTask;
        }


        /// <summary>
        /// Gets a list of identities that follow me
        /// </summary>
        public async Task<CursoredResult<string>> GetFollowers(Guid driveId, string cursor)
        {
            var drive = await _driveService.GetDrive(driveId, false);
            if (drive.TargetDriveInfo.Type != SystemDriveConstants.ChannelDriveType)
            {
                throw new YouverseClientException("Invalid Drive Type", YouverseClientErrorCode.InvalidTargetDrive);
            }

            var count = 10;
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
        public Task<CursoredResult<FollowerDefinition>> GetIdentitiesIFollow(string cursor)
        {
            return null;
            // var drive = await _driveService.GetDrive(driveId, false);
            // if (drive.TargetDriveInfo.Type != SystemDriveConstants.ChannelDriveType)
            // {
            //     throw new YouverseClientException("Invalid Drive Type", YouverseClientErrorCode.InvalidTargetDrive);
            // }
            //
            // var count = 10;
            // var dbResults = _tenantStorage.FollowedIdentities.GetFollowers(count, driveId, cursor);
            // var result = new CursoredResult<string>()
            // {
            //     Cursor = dbResults.Last(),
            //     Results = dbResults
            // };
            //
            // return result;
        }

        ///
        private IFollowerHttpClient CreateClient(DotYouIdentity dotYouId)
        {
            var httpClient = _httpClientFactory.CreateClient<IFollowerHttpClient>((DotYouIdentity)dotYouId);
            return httpClient;
        }

    }
}