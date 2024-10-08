using System;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Mediator;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.AppNotifications.Data;

/// <summary>
/// Storage for notifications
/// </summary>
public class NotificationListService(TenantSystemStorage tenantSystemStorage, IMediator mediator)
{
    private readonly TableAppNotifications _storage = tenantSystemStorage.AppNotifications;

    public async Task<AddNotificationResult> AddNotification(OdinId senderId, AddNotificationRequest request, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);
        return await AddNotificationInternal(senderId, request, odinContext, cn);
    }

    internal async Task<AddNotificationResult> AddNotificationInternal(OdinId senderId, AddNotificationRequest request, IOdinContext odinContext,
        DatabaseConnection cn)
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

        _storage.Insert(cn, record);

        await mediator.Publish(new AppNotificationAddedNotification(request.AppNotificationOptions.TypeId)
        {
            Id = id,
            SenderId = senderId,
            Timestamp = request.Timestamp,
            AppNotificationOptions = request.AppNotificationOptions,
            OdinContext = odinContext,
            DatabaseConnection = cn
        });

        return new AddNotificationResult()
        {
            NotificationId = id
        };
    }

    public Task<NotificationsListResult> GetList(GetNotificationListRequest request, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        var results = _storage.PagingByCreated(cn, request.Count, request.Cursor, out var cursor);

        var list = results.Select(r => new AppNotification()
        {
            Id = r.notificationId,
            SenderId = r.senderId,
            Unread = r.unread == 1,
            Created = r.created.ToUnixTimeUtc(),
            Options = r.data == null ? default : OdinSystemSerializer.Deserialize<AppNotificationOptions>(r.data.ToStringFromUtf8Bytes())
        });

        //Note: this was added long after the db table.  given the assumption there will be
        //very few (relatively speaking) notifications.  we'll do this ugly count for now
        //until it becomes an issue
        if (request.AppId.HasValue)
        {
            list = list.Where(n => n.Options?.AppId == request.AppId);
        }

        var nr = new NotificationsListResult()
        {
            Cursor = cursor,
            Results = list.ToList()
        };

        return Task.FromResult(nr);
    }

    public Task<NotificationsCountResult> GetUnreadCounts(IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        //Note: this was added long after the db table.  given the assumption there will be
        //very few (relatively speaking) notifications.  we'll do this ugly count for now
        //until it becomes an issue
        var results = _storage.PagingByCreated(cn, int.MaxValue, null, out _);

        var list = results.Select(r => new AppNotification()
        {
            Id = r.notificationId,
            SenderId = r.senderId,
            Unread = r.unread == 1,
            Created = r.created.ToUnixTimeUtc(),
            Options = r.data == null ? default : OdinSystemSerializer.Deserialize<AppNotificationOptions>(r.data.ToStringFromUtf8Bytes())
        });

        return Task.FromResult(new NotificationsCountResult()
        {
            UnreadCounts = list.GroupBy(n => (n.Options?.AppId).GetValueOrDefault()).ToDictionary(g => g.Key, g => g.Count(n => n.Unread))
        });
    }

    public Task Delete(DeleteNotificationsRequest request, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        foreach (var id in request.IdList)
        {
            _storage.Delete(cn, id);
        }

        return Task.CompletedTask;
    }

    public async Task UpdateNotifications(UpdateNotificationListRequest request, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        foreach (var update in request.Updates)
        {
            var record = _storage.Get(cn, update.Id);
            if (null != record)
            {
                record.unread = update.Unread ? 1 : 0;
                _storage.Update(cn, record);
            }
        }

        await Task.CompletedTask;
    }

    public async Task MarkReadByApp(Guid appId, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        var allByApp = await this.GetList(new GetNotificationListRequest()
        {
            AppId = appId,
            Count = int.MaxValue,
        }, odinContext, cn);

        var request = new UpdateNotificationListRequest()
        {
            Updates = allByApp.Results.Select(n => new UpdateNotificationRequest()
            {
                Id = n.Id,
                Unread = false
            }).ToList()
        };

        await this.UpdateNotifications(request, odinContext, cn);

        await Task.CompletedTask;
    }
}