using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Odin.Core.Exceptions;
using Odin.Core.Services.AppNotifications.ClientNotifications;
using Odin.Core.Services.Base;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Core.Services.Mediator;
using Odin.Core.Storage;
using Odin.Core.Time;
using WebPush;


namespace Odin.Core.Services.AppNotifications.Push;

public class PushNotificationService : INotificationHandler<IClientNotification>, INotificationHandler<IDriveNotification>,
    INotificationHandler<TransitFileReceivedNotification>
{
    const string DeviceStorageContextKey = "9a9cacb4-b76a-4ad4-8340-e681691a2ce4";
    const string DeviceStorageDataTypeKey = "1026f96f-f85f-42ed-9462-a18b23327a33";
    private readonly TwoKeyValueStorage _deviceSubscriptionStorage;
    private readonly OdinContextAccessor _contextAccessor;

    private readonly PublicPrivateKeyService _keyService;

    private readonly byte[] _deviceStorageDataType = Guid.Parse(DeviceStorageDataTypeKey).ToByteArray();

    public PushNotificationService(TenantSystemStorage storage, OdinContextAccessor contextAccessor, PublicPrivateKeyService keyService)
    {
        _contextAccessor = contextAccessor;
        _keyService = keyService;
        _deviceSubscriptionStorage = storage.CreateTwoKeyValueStorage(Guid.Parse(DeviceStorageContextKey));
    }

    public Task AddDevice(PushNotificationSubscription subscription)
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
        //TODO: validate expiration time

        if (subscription == null ||
            string.IsNullOrEmpty(subscription.Endpoint) || string.IsNullOrWhiteSpace(subscription.Endpoint) ||
            string.IsNullOrEmpty(subscription.Auth) || string.IsNullOrWhiteSpace(subscription.Auth) ||
            string.IsNullOrEmpty(subscription.P256DH) || string.IsNullOrWhiteSpace(subscription.P256DH))
        {
            throw new OdinClientException("Invalid Push notification subscription request");
        }

        subscription.AccessRegistrationId = GetDeviceKey();
        subscription.SubscriptionStartedDate = UnixTimeUtc.Now();

        _deviceSubscriptionStorage.Upsert(subscription.AccessRegistrationId, _deviceStorageDataType, subscription);
        return Task.CompletedTask;
    }

    public Task<PushNotificationSubscription> GetDeviceSubscription()
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

        return Task.FromResult(_deviceSubscriptionStorage.Get<PushNotificationSubscription>(GetDeviceKey()));
    }

    public Task RemoveDevice()
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

        _deviceSubscriptionStorage.Delete(GetDeviceKey());
        return Task.CompletedTask;
    }


    public async Task Push(PushNotificationContent content)
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

        var subscriptions = await GetAllSubscriptions();

        var publicKey = (await _keyService.GetOfflinePublicKey()).publicKey.ToBase64();
        var privateKey = PublicPrivateKeyService.OfflinePrivateKeyEncryptionKey.ToBase64();

        foreach (var deviceSubscription in subscriptions)
        {
            //TODO: enforce sub.ExpirationTime

            var subscription = new PushSubscription(deviceSubscription.Endpoint, deviceSubscription.P256DH, deviceSubscription.Auth);
            var vapidDetails = new VapidDetails(content.Subject, publicKey, privateKey);

            //TODO: this will probably need to get an http client via @Seb's work
            var webPushClient = new WebPushClient();
            try
            {
                await webPushClient.SendNotificationAsync(subscription, content.Payload, vapidDetails);
            }
            catch (WebPushException exception)
            {
                //TODO: collect all errors and send back to client or do something with it
                throw new OdinClientException("Failed to send one or more notifications.", exception);
                // Console.WriteLine("Http STATUS code" + exception.StatusCode);
            }
        }
    }

    public async Task RemoveAllDevices()
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
        var subscriptions = await GetAllSubscriptions();
        foreach (var sub in subscriptions)
        {
            _deviceSubscriptionStorage.Delete(sub.AccessRegistrationId);
        }
    }

    public Task<List<PushNotificationSubscription>> GetAllSubscriptions()
    {
        var subscriptions = _deviceSubscriptionStorage.GetByDataType<PushNotificationSubscription>(_deviceStorageDataType);
        return Task.FromResult(subscriptions?.ToList() ?? new List<PushNotificationSubscription>());
    }

    public Task Handle(IClientNotification notification, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task Handle(IDriveNotification notification, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task Handle(TransitFileReceivedNotification notification, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private Guid GetDeviceKey()
    {
        var key = _contextAccessor.GetCurrent().Caller.OdinClientContext?.AccessRegistrationId ?? Guid.Empty;

        if (key == Guid.Empty)
        {
            throw new OdinSystemException("The access registration id was not set on the context");
        }

        return key;
    }
}