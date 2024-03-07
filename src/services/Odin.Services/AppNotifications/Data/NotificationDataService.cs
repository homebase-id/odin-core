using System;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.AppNotifications.Data;

/// <summary>
/// Storage for notifications
/// </summary>
public class NotificationListService
{
    private readonly OdinContextAccessor _contextAccessor;
    private readonly TableAppNotifications _storage;
    private readonly TenantSystemStorage _tenantSystemStorage;

    public NotificationListService(TenantSystemStorage tenantSystemStorage, OdinContextAccessor contextAccessor)
    {
        _contextAccessor = contextAccessor;
        _storage = tenantSystemStorage.AppNotifications;
        _tenantSystemStorage = tenantSystemStorage;
    }

    public Task<AddNotificationResult> AddNotification(OdinId senderId, AddNotificationRequest request)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);
        
        var id = Guid.NewGuid();
        var record = new AppNotificationsRecord()
        {
            notificationId = id,
            senderId = senderId,
            timestamp = request.Timestamp,
            unread = 1,
            data = OdinSystemSerializer.Serialize(request.AppNotificationOptions).ToUtf8ByteArray()
        };

        _storage.Insert(record);

        return Task.FromResult(new AddNotificationResult()
        {
            NotificationId = id
        });
    }

    public Task<NotificationsListResult> GetList(GetNotificationListRequest request)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        var results = _storage.PagingByCreated(request.Count, request.Cursor, out var cursor);

        var nr = new NotificationsListResult()
        {
            Cursor = cursor,
            Results = results.Select(r => new AppNotification()
            {
                Id = r.notificationId,
                SenderId = r.senderId,
                Unread = r.unread == 1,
                Created = r.created.ToUnixTimeUtc(),
                Options = r.data == null ? default : OdinSystemSerializer.Deserialize<AppNotificationOptions>(r.data.ToStringFromUtf8Bytes())
            }).ToList()
        };

        return Task.FromResult(nr);
    }

    public Task Delete(DeleteNotificationsRequest request)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        foreach (var id in request.IdList)
        {
            _storage.Delete(id);
        }

        return Task.CompletedTask;
    }


    public async Task UpdateNotifications(UpdateNotificationListRequest request)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        foreach (var update in request.Updates)
        {
            var record = _storage.Get(update.Id);
            if (null != record)
            {
                record.unread = update.Unread ? 1 : 0;
                _storage.Update(record);
            }
        }

        await Task.CompletedTask;
    }
}