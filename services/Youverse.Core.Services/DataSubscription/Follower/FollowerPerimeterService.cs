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
            Guard.Argument(request.OdinId, nameof(request.OdinId)).NotNull().NotEmpty();
            OdinId.Validate(request.OdinId);
            // Guard.Argument(request.PortableClientAuthToken, nameof(request.PortableClientAuthToken)).NotNull().NotEmpty();
            // var clientAccessToken = ClientAccessToken.FromPortableBytes(request.PortableClientAuthToken);
            // Guard.Argument(clientAccessToken, nameof(clientAccessToken)).NotNull().Require(cat => cat.IsValid());
            
            //
            //TODO: where to store the request.ClientAuthToken ??
            // 
            
            if (request.NotificationType == FollowerNotificationType.AllNotifications)
            {
                _tenantStorage.Followers.DeleteFollower(request.OdinId);
                _tenantStorage.Followers.Insert(new FollowsMeItem() { identity = request.OdinId, driveId = System.Guid.Empty });
            }

            
            //Issue with select channels
            // we need to look up the driveId but it's not available in the permission context
            // because the caller is totally public.
            if (request.NotificationType == FollowerNotificationType.SelectedChannels)
            {
                Guard.Argument(request.Channels, nameof(request.Channels)).NotNull().NotEmpty().Require(channels => channels.All(c => c.Type == SystemDriveConstants.ChannelDriveType));

                var driveIdList = request.Channels.Select(chan => _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(chan));

               using( _tenantStorage.CreateCommitUnitOfWork())
               {
                   _tenantStorage.Followers.DeleteFollower(request.OdinId);
                   foreach (var driveId in driveIdList)
                   {
                       _tenantStorage.Followers.Insert(new FollowsMeItem() { identity = request.OdinId, driveId = driveId });
                   }
               }

                return Task.CompletedTask;
            }
            
            return Task.CompletedTask;
        }

        /// <summary>
        /// Removes the caller from the list of followers so they no longer recieve updates
        /// </summary>
        /// <returns></returns>
        public Task AcceptUnfollowRequest()
        {
            var follower = _contextAccessor.GetCurrent().Caller.OdinId;
            _tenantStorage.Followers.DeleteFollower(follower);
            return Task.CompletedTask;
        }
    }
}