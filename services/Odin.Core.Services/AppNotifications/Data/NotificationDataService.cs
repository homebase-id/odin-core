using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Services.AppNotifications.Push;
using Odin.Core.Services.Base;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;

namespace Odin.Core.Services.AppNotifications.Data;

/// <summary>
/// Storage for notifications
/// </summary>
public class NotificationDataService
{
    private readonly OdinContextAccessor _contextAccessor;
    private readonly TableAppNotifications _storage;

    public NotificationDataService(TenantSystemStorage storage, OdinContextAccessor contextAccessor)
    {
        _contextAccessor = contextAccessor;
        _storage = storage.AppNotifications;
    }

    public Task AddNotification(AddNotificationRequest request)
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
        var senderId = _contextAccessor.GetCurrent().GetCallerOdinIdOrFail();
        var record = new AppNotificationsRecord()
        {
            notificationId = Guid.NewGuid(),
            created = UnixTimeUtcUnique.Now(),
            senderId = senderId.DomainName,
            unread = 1,
            data = request.Payload.ToUtf8ByteArray()
        };

        _storage.Insert(record);
        return Task.CompletedTask;
    }

    public Task<NotificationsListResult> GetList(GetNotificationListRequest request)
    {
        var results = _storage.PagingByCreated(request.Count, request.Cursor, out var cursor);

        var nr = new NotificationsListResult()
        {
            Cursor = cursor,
            Results = results.Select(r => new AppNotification()
            {
                SenderId = r.senderId,
                Unread = r.unread == 1,
                Data = r.data.ToStringFromUtf8Bytes()
            }).ToList()
        };

        return Task.FromResult(nr);
    }

    public Task Delete(DeleteNotificationsRequest request)
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
        foreach (var id in request.IdList)
        {
            _storage.Delete(id);
        }

        return Task.CompletedTask;
    }


    public async Task UpdateNotifications(UpdateNotificationListRequest request)
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

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