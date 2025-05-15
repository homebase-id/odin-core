using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Util;

namespace Odin.Services.DataSubscription.Follower
{
    /// <summary/>
    public class FollowerPerimeterService(IMediator mediator, IdentityDatabase db)
    {
        /// <summary>
        /// Accepts the new or exiting follower by upserting a record to ensure
        /// the follower is notified of content changes.
        /// </summary>
        private async Task DoAcceptFollowerAsync(PerimeterFollowRequest request, IOdinContext odinContext)
        {
            var identityFollowing = new OdinId(request.OdinId);

            await using (var tx = await db.BeginStackedTransactionAsync())
            {
                if (request.NotificationType == FollowerNotificationType.AllNotifications)
                {
                    // Created sample DeleteAndAddFollower() - take a look
                    await db.FollowsMe.DeleteByIdentityAsync(identityFollowing);
                    await db.FollowsMe.InsertAsync(new FollowsMeRecord() { identity = identityFollowing, driveId = System.Guid.Empty });
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

                    var followsMeRecords = new List<FollowsMeRecord>();

                    foreach (var channel in request.Channels)
                    {
                        followsMeRecords.Add(new FollowsMeRecord() { identity = identityFollowing, driveId = channel.Alias });
                    }

                    await db.FollowsMe.DeleteAndInsertManyAsync(identityFollowing, followsMeRecords);
                }

                tx.Commit();
            }
        }


        /// <summary>
        /// Accepts the new or exiting follower by upserting a record to ensure
        /// the follower is notified of content changes.
        /// </summary>
        public async Task AcceptFollowerAsync(PerimeterFollowRequest request, IOdinContext odinContext)
        {
            //
            //TODO: where to store the request.ClientAuthToken ??
            // 

            //
            // Question: Do we check the X509 of the caller and do we check if the caller is blocked?
            //

            await DoAcceptFollowerAsync(request, odinContext);

            await mediator.Publish(new NewFollowerNotification
            {
                Sender = (OdinId)request.OdinId,
                OdinContext = odinContext,
            });
        }

        /// <summary>
        /// Removes the caller from the list of followers so they no longer recieve updates
        /// </summary>
        /// <returns></returns>
        public async Task AcceptUnfollowRequestAsync(IOdinContext odinContext)
        {
            var follower = new OdinId(odinContext.Caller.OdinId);

            await db.FollowsMe.DeleteByIdentityAsync(follower);
        }
    }
}