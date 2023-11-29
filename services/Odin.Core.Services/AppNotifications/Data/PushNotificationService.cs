using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Services.AppNotifications.Push;
using Odin.Core.Services.Base;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Core.Storage;

namespace Odin.Core.Services.AppNotifications.Data;

public class NotificationListService
{
    const string StorageContextKey = "1fb084ba-bcdc-42fb-8ca4-dbe09998e5c1";
    const string StorageDataTypeKey = "791e7759-4552-4e33-9c3c-d0a9d367dcd5";
    private readonly TwoKeyValueStorage _storage;
    private readonly OdinContextAccessor _contextAccessor;


    private readonly byte[] _deviceStorageDataType = Guid.Parse(StorageDataTypeKey).ToByteArray();

    public NotificationListService(TenantSystemStorage storage, OdinContextAccessor contextAccessor, PublicPrivateKeyService keyService)
    {
        _contextAccessor = contextAccessor;
        _storage = storage.CreateTwoKeyValueStorage(Guid.Parse(StorageContextKey));
    }

    public Task AddNotification(AddNotificationRequest request)
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
        _storage.Upsert(notificationId, _deviceStorageDataType, notificationData);
        return Task.CompletedTask;
    }

    public Task RemoveNotification(Guid id)
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

        _storage.Delete(id);
        return Task.CompletedTask;
    }

    public Task<List<PushNotificationSubscription>> GetAllNotifications()
    {
        var subscriptions = _storage.GetByDataType<PushNotificationSubscription>(_deviceStorageDataType);
        return Task.FromResult(subscriptions?.ToList() ?? new List<PushNotificationSubscription>());
    }

}