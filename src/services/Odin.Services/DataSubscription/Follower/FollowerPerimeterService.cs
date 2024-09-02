using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Util;

namespace Odin.Services.DataSubscription.Follower
{
    /// <summary/>
    public class FollowerPerimeterService
    {
        private readonly TenantSystemStorage _tenantStorage;

        private readonly IMediator _mediator;


        public FollowerPerimeterService(TenantSystemStorage tenantStorage, IMediator mediator)
        {
            _tenantStorage = tenantStorage;

            _mediator = mediator;
        }

        /// <summary>
        /// Accepts the new or exiting follower by upserting a record to ensure
        /// the follower is notified of content changes.
        /// </summary>
        public Task AcceptFollower(PerimeterFollowRequest request, IOdinContext odinContext, IdentityDatabase db)
        {
            //
            //TODO: where to store the request.ClientAuthToken ??
            // 

            if (request.NotificationType == FollowerNotificationType.AllNotifications)
            {
                /* TODO CONNECTIONS - FIX THIS UP!
                cn.CreateCommitUnitOfWork(() =>
                {
                    _tenantStorage.Followers.DeleteByIdentity(cn, request.OdinId);
                    _tenantStorage.Followers.Insert(cn,
                        new FollowsMeRecord() { identity = request.OdinId, driveId = System.Guid.Empty });
                });*/

                // Created sample DeleteAndAddFollower() - take a look
                _tenantStorage.Followers.DeleteByIdentity(request.OdinId);
                _tenantStorage.Followers.Insert(new FollowsMeRecord() { identity = request.OdinId, driveId = System.Guid.Empty });
            }

            if (request.NotificationType == FollowerNotificationType.SelectedChannels)
            {
                OdinValidationUtils.AssertNotNull(request.Channels, nameof(request.Channels));
                OdinValidationUtils.AssertIsTrue(request.Channels.All(c => c.Type == SystemDriveConstants.ChannelDriveType),
                    $"All drives must be of type channel [{SystemDriveConstants.ChannelDriveType}]");

                //Valid the caller has access to the requested channels
                try
                {
                    //use try/catch since GetDriveId will throw an exception
                    //TODO: update PermissionContext with a better method
                    var drives = request.Channels.Select(chan => odinContext.PermissionsContext.GetDriveId(chan));
                    var allHaveReadAccess = drives.All(driveId =>
                        odinContext.PermissionsContext.HasDrivePermission(driveId, DrivePermission.Read));
                    if (!allHaveReadAccess)
                    {
                        throw new OdinSecurityException("Caller does not have read access to one or more channels");
                    }
                }
                catch
                {
                    throw new OdinSecurityException("Caller does not have read access to one or more channels");
                }

                // TODO CONNECTIONS - FIX THIS UP!
                /*cn.CreateCommitUnitOfWork(() =>
                {
                    _tenantStorage.Followers.DeleteByIdentity(cn, request.OdinId);
                    foreach (var channel in request.Channels)
                    {
                        _tenantStorage.Followers.Insert(cn,
                            new FollowsMeRecord() { identity = request.OdinId, driveId = channel.Alias });
                    }
                });*/

                // Created sample DeleteAndAddFollower() - take a look - make it a list so it works both here and above?
                _tenantStorage.Followers.DeleteByIdentity(request.OdinId);
                foreach (var channel in request.Channels)
                {
                    _tenantStorage.Followers.Insert(new FollowsMeRecord() { identity = request.OdinId, driveId = channel.Alias });
                }

                return Task.CompletedTask;
            }

            _mediator.Publish(new NewFollowerNotification
            {
                Sender = (OdinId)request.OdinId,
                OdinContext = odinContext,
                db = db
            });

            return Task.CompletedTask;
        }

        /// <summary>
        /// Removes the caller from the list of followers so they no longer recieve updates
        /// </summary>
        /// <returns></returns>
        public Task AcceptUnfollowRequest(IOdinContext odinContext, IdentityDatabase db)
        {
            var follower = odinContext.Caller.OdinId;

            _tenantStorage.Followers.DeleteByIdentity(follower);
            return Task.CompletedTask;
        }
    }
}