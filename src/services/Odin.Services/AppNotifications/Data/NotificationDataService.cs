using System;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Mediator;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.AppNotifications.Data;

/// <summary>
/// Storage for notifications
/// </summary>
public class NotificationListService(IdentityDatabase db, IMediator mediator)
{
    public async Task<AddNotificationResult> AddNotification(OdinId senderId, AddNotificationRequest request, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);
        return await AddNotificationInternal(senderId, request, odinContext);
    }

    internal async Task<AddNotificationResult> AddNotificationInternal(OdinId senderId, AddNotificationRequest request,
        IOdinContext odinContext)
    {
        var id = Guid.NewGuid();
        var record = new AppNotificationsRecord()
        {
            notificationId = id,
            senderId = senderId,
            timestamp = request.Timestamp,
            unread = 1,
            data = OdinSystemSerializer.Serialize(request.AppNotificationOptions).ToUtf8ByteArray()
        };

        await db.AppNotificationsCached.InsertAsync(record);

        await mediator.Publish(new AppNotificationAddedNotification(request.AppNotificationOptions.TypeId)
        {
            Id = id,
            SenderId = senderId,
            Timestamp = request.Timestamp,
            AppNotificationOptions = request.AppNotificationOptions,
            OdinContext = odinContext,
        });

        return new AddNotificationResult()
        {
            NotificationId = id
        };
    }

    public async Task<NotificationsListResult> GetList(GetNotificationListRequest request, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        var (results, cursor) = await db.AppNotificationsCached.PagingByCreatedAsync(request.Count, request.Cursor);

        var list = results.Select(r => new AppNotification()
        {
            Id = r.notificationId,
            SenderId = r.senderId,
            Unread = r.unread == 1,
            Created = r.created,
            Options = r.data == null ? default : OdinSystemSerializer.Deserialize<AppNotificationOptions>(r.data.ToStringFromUtf8Bytes())
        });

        //Note: this was added long after the db table.  given the assumption there will be
        //very few (relatively speaking) notifications.  we'll do this ugly count for now
        //until it becomes an issue
        if (request.AppId.HasValue)
        {
            list = list.Where(n => n.Options?.AppId == request.AppId);
        }

        if (request.TypeId.HasValue)
        {
            list = list.Where(n => n.Options?.TypeId == request.TypeId);
        }

        var nr = new NotificationsListResult()
        {
            Cursor = cursor ?? "",
            Results = list.ToList()
        };

        return nr;
    }

    public async Task<NotificationsCountResult> GetUnreadCounts(IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        //Note: this was added long after the db table.  given the assumption there will be
        //very few (relatively speaking) notifications.  we'll do this ugly count for now
        //until it becomes an issue
        var (results, _) = await db.AppNotificationsCached.PagingByCreatedAsync(int.MaxValue, null);

        var list = results.Select(r => new AppNotification()
        {
            Id = r.notificationId,
            SenderId = r.senderId,
            Unread = r.unread == 1,
            Created = r.created,
            Options = r.data == null ? default : OdinSystemSerializer.Deserialize<AppNotificationOptions>(r.data.ToStringFromUtf8Bytes())
        });

        return new NotificationsCountResult()
        {
            UnreadCounts = list.GroupBy(n => (n.Options?.AppId).GetValueOrDefault()).ToDictionary(g => g.Key, g => g.Count(n => n.Unread))
        };
    }

    public Task Delete(DeleteNotificationsRequest request, IOdinContext odinContext)
    {
        return Task.CompletedTask;
    }

    public Task UpdateNotifications(UpdateNotificationListRequest request, IOdinContext odinContext)
    {
        return Task.CompletedTask;
    }

    public Task MarkReadByApp(Guid appId, IOdinContext odinContext)
    {
        return Task.CompletedTask;
    }

    public Task MarkReadByAppAndTypeId(Guid appId, Guid typeId, IOdinContext odinContext)
    {
        return Task.CompletedTask;
    }
}