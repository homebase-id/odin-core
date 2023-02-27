using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Storage;
using Youverse.Core.Storage.SQLite.IdentityDatabase;

namespace Youverse.Core.Services.DataSubscription.Follower
{
    /// <summary/>
    public class FollowerPerimeterService
    {
        private readonly ITenantSystemStorage _tenantStorage;
        private readonly DotYouContextAccessor _contextAccessor;

        public FollowerPerimeterService(ITenantSystemStorage tenantStorage, DotYouContextAccessor contextAccessor)
        {
            _tenantStorage = tenantStorage;
            _contextAccessor = contextAccessor;
        }

        /// <summary>
        /// Accepts the new or exiting follower by upserting a record to ensure
        /// the follower is notified of content changes.
        /// </summary>
        public Task AcceptFollower(PerimterFollowRequest request)
        {
            Guard.Argument(request, nameof(request)).NotNull();
            Guard.Argument(request.DotYouId, nameof(request.DotYouId)).NotNull().NotEmpty();
            OdinId.Validate(request.DotYouId);
            // Guard.Argument(request.PortableClientAuthToken, nameof(request.PortableClientAuthToken)).NotNull().NotEmpty();
            // var clientAccessToken = ClientAccessToken.FromPortableBytes(request.PortableClientAuthToken);
            // Guard.Argument(clientAccessToken, nameof(clientAccessToken)).NotNull().Require(cat => cat.IsValid());
            
            //
            //TODO: where to store the request.ClientAuthToken ??
            // 
            
            if (request.NotificationType == FollowerNotificationType.SelectedChannels)
            {
                Guard.Argument(request.Channels, nameof(request.Channels)).NotNull().NotEmpty().Require(channels => channels.All(c => c.Type == SystemDriveConstants.ChannelDriveType));

                var driveIdList = request.Channels.Select(chan => _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(chan));

                _tenantStorage.Followers.DeleteFollower(request.DotYouId);
                foreach (var driveId in driveIdList)
                {
                    _tenantStorage.Followers.Insert(new FollowsMeItem() { identity = request.DotYouId, driveId = driveId });
                }

                return Task.CompletedTask;
            }

            if (request.NotificationType == FollowerNotificationType.AllNotifications)
            {
                _tenantStorage.Followers.DeleteFollower(request.DotYouId);
                _tenantStorage.Followers.Insert(new FollowsMeItem() { identity = request.DotYouId, driveId = System.Guid.Empty });
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Removes the caller from the list of followers so they no longer recieve updates
        /// </summary>
        /// <returns></returns>
        public Task AcceptUnfollowRequest()
        {
            var follower = _contextAccessor.GetCurrent().Caller.DotYouId;
            _tenantStorage.Followers.DeleteFollower(follower);
            return Task.CompletedTask;
        }
    }
}