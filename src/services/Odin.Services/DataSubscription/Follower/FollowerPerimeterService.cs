using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Util;

namespace Odin.Services.DataSubscription.Follower
{
    /// <summary/>
    public class FollowerPerimeterService(IMediator mediator, IdentityDatabase db, TenantContext tenantContext)
    {
        private readonly TenantContext _tenantContext = tenantContext;

        /// <summary>
        /// Accepts the new or exiting follower by upserting a record to ensure
        /// the follower is notified of content changes.
        /// </summary>
        public async Task AcceptFollowerAsync(PerimeterFollowRequest request, IOdinContext odinContext)
        {
            //
            //TODO: where to store the request.ClientAuthToken ??
            // 

            if (request.NotificationType == FollowerNotificationType.AllNotifications)
            {
                // Created sample DeleteAndAddFollower() - take a look
                await using var trx = await db.BeginStackedTransactionAsync();
                await db.FollowsMeCached.DeleteByIdentityAsync(new OdinId(request.OdinId));
                await db.FollowsMeCached.InsertAsync(new FollowsMeRecord() { identity = request.OdinId, driveId = Guid.Empty });
                trx.Commit();
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
                    var drives = request.Channels.Select(chan => chan.Alias);
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
                    followsMeRecords.Add(new FollowsMeRecord() { identity = request.OdinId, driveId = channel.Alias });
                }

                await db.FollowsMeCached.DeleteAndInsertManyAsync(new OdinId(request.OdinId), followsMeRecords);
            }

            // Add to subscription tables on the source side to track subscribers for push notifications
            var feedDriveId = SystemDriveConstants.FeedDrive.Alias;
            var now = UnixTimeUtc.Now();

            // Record subscription to all channel drives if all notifications requested
            if (request.NotificationType == FollowerNotificationType.AllNotifications)
            {
                var record = new MySubscribersRecord
                {
                    identityId = _tenantContext.DotYouRegistryId,
                    subscriberOdinId = new OdinId(request.OdinId),
                    sourceDriveTypeId = SystemDriveConstants.ChannelDriveType,
                    targetDriveId = feedDriveId,
                    created = now,
                    modified = now
                };
                await db.MySubscribersCached.UpsertAsync(record);
            }

            // Record subscription to each selected channel drive
            if (request.NotificationType == FollowerNotificationType.SelectedChannels)
            {
                foreach (var channel in request.Channels)
                {
                    var record = new MySubscribersRecord
                    {
                        identityId = _tenantContext.DotYouRegistryId,
                        subscriberOdinId = new OdinId(request.OdinId),
                        sourceDriveId = channel.Alias,
                        targetDriveId = feedDriveId,
                        created = now,
                        modified = now
                    };
                    await db.MySubscribersCached.UpsertAsync(record);
                }
            }

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
            var follower = odinContext.Caller.OdinId;

            await db.FollowsMeCached.DeleteByIdentityAsync(new OdinId(follower));

            // Remove from subscription tables to stop tracking the subscriber for push notifications
            await db.MySubscribersCached.DeleteBySubscriberAsync(_tenantContext.DotYouRegistryId, new OdinId(follower));
        }
    }
}