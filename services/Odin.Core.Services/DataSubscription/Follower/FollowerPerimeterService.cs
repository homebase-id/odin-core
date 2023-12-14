using System.Linq;
using System.Threading.Tasks;
using Dawn;
using MediatR;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Services.AppNotifications.ClientNotifications;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Services.DataSubscription.Follower
{
    /// <summary/>
    public class FollowerPerimeterService
    {
        private readonly TenantSystemStorage _tenantStorage;
        private readonly OdinContextAccessor _contextAccessor;
        private readonly IMediator _mediator;


        public FollowerPerimeterService(TenantSystemStorage tenantStorage, OdinContextAccessor contextAccessor, IMediator mediator)
        {
            _tenantStorage = tenantStorage;
            _contextAccessor = contextAccessor;
            _mediator = mediator;
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
                _tenantStorage.Followers.DeleteByIdentity(request.OdinId);
                _tenantStorage.Followers.Insert(new FollowsMeRecord() { identity = request.OdinId, driveId = System.Guid.Empty });
            }

            if (request.NotificationType == FollowerNotificationType.SelectedChannels)
            {
                Guard.Argument(request.Channels, nameof(request.Channels)).NotNull().NotEmpty().Require(channels => channels.All(c => c.Type == SystemDriveConstants.ChannelDriveType));

                //Valid the caller has access to the requested channels
                try
                {
                    //use try/catch since GetDriveId will throw an exception
                    //TODO: update PermissionContext with a better method
                    var drives = request.Channels.Select(chan => _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(chan));
                    var allHaveReadAccess = drives.All(driveId => _contextAccessor.GetCurrent().PermissionsContext.HasDrivePermission(driveId, DrivePermission.Read));
                    if (!allHaveReadAccess)
                    {
                        throw new OdinSecurityException("Caller does not have read access to one or more channels");
                    }
                }
                catch
                {
                    throw new OdinSecurityException("Caller does not have read access to one or more channels");
                }

                using (_tenantStorage.CreateCommitUnitOfWork())
                {
                    _tenantStorage.Followers.DeleteByIdentity(request.OdinId);
                    foreach (var channel in request.Channels)
                    {
                        _tenantStorage.Followers.Insert(new FollowsMeRecord() { identity = request.OdinId, driveId = channel.Alias });
                    }
                }
                
                return Task.CompletedTask;
            }
            
            _mediator.Publish(new NewFollowerNotification()
            {
                OdinId = (OdinId)request.OdinId
            });

            return Task.CompletedTask;
        }

        /// <summary>
        /// Removes the caller from the list of followers so they no longer recieve updates
        /// </summary>
        /// <returns></returns>
        public Task AcceptUnfollowRequest()
        {
            var follower = _contextAccessor.GetCurrent().Caller.OdinId;
            _tenantStorage.Followers.DeleteByIdentity(follower);
            return Task.CompletedTask;
        }
    }
}