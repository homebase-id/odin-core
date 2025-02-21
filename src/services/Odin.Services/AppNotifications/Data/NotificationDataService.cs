using System;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
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

        await db.AppNotifications.InsertAsync(record);

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

        var (results, cursor) = await db.AppNotifications.PagingByCreatedAsync(request.Count, request.Cursor);

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
            Cursor = cursor, // Or should null be empty string ""?
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
        var (results, _) = await db.AppNotifications.PagingByCreatedAsync(int.MaxValue, null);

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

    public async Task Delete(DeleteNotificationsRequest request, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        await using var trx = await db.BeginStackedTransactionAsync();

        foreach (var id in request.IdList)
        {
            await db.AppNotifications.DeleteAsync(id);
        }

        trx.Commit();
    }

    public async Task UpdateNotifications(UpdateNotificationListRequest request, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        await using var trx = await db.BeginStackedTransactionAsync();

        foreach (var update in request.Updates)
        {
            var record = await db.AppNotifications.GetAsync(update.Id);
            if (null != record)
            {
                record.unread = update.Unread ? 1 : 0;
                await db.AppNotifications.UpdateAsync(record);
            }
        }

        trx.Commit();
    }

    public async Task MarkReadByApp(Guid appId, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        var allByApp = await this.GetList(new GetNotificationListRequest()
        {
            AppId = appId,
            Count = int.MaxValue,
        }, odinContext);

        var request = new UpdateNotificationListRequest()
        {
            Updates = allByApp.Results.Select(n => new UpdateNotificationRequest()
            {
                Id = n.Id,
                Unread = false
            }).ToList()
        };

        await UpdateNotifications(request, odinContext);
    }

    public async Task MarkReadByAppAndTypeId(Guid appId, Guid typeId, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        var allByAppAndType = await this.GetList(new GetNotificationListRequest()
        {
            AppId = appId,
            TypeId = typeId,
            Count = int.MaxValue,
        }, odinContext);

        var request = new UpdateNotificationListRequest()
        {
            Updates = allByAppAndType.Results.Select(n => new UpdateNotificationRequest()
            {
                Id = n.Id,
                Unread = false
            }).ToList()
        };

        await UpdateNotifications(request, odinContext);
    }
}