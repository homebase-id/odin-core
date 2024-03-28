using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.Data;
using Odin.Services.Apps;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Peer.Outgoing.Drive;
using Serilog;
using WebPush;

namespace Odin.Services.AppNotifications.Push;

public class PushNotificationService(
    TenantSystemStorage storage,
    OdinContextAccessor contextAccessor,
    PublicPrivateKeyService keyService,
    TenantSystemStorage tenantSystemStorage,
    ServerSystemStorage serverSystemStorage,
    NotificationListService notificationListService,
    IAppRegistrationService appRegistrationService,
    OdinConfiguration configuration)
    : INotificationHandler<ConnectionRequestAccepted>,
        INotificationHandler<ConnectionRequestReceived>
{
    const string DeviceStorageContextKey = "9a9cacb4-b76a-4ad4-8340-e681691a2ce4";
    const string DeviceStorageDataTypeKey = "1026f96f-f85f-42ed-9462-a18b23327a33";
    private readonly TwoKeyValueStorage _deviceSubscriptionStorage = storage.CreateTwoKeyValueStorage(Guid.Parse(DeviceStorageContextKey));

    private readonly PushNotificationOutbox _pushNotificationOutbox = new(tenantSystemStorage, contextAccessor);
    private readonly byte[] _deviceStorageDataType = Guid.Parse(DeviceStorageDataTypeKey).ToByteArray();

    /// <summary>
    /// Adds a notification to the outbox
    /// </summary>
    public Task<bool> EnqueueNotification(OdinId senderId, AppNotificationOptions options, InternalDriveFileId? fileId = null)
    {
        //TODO: which security to check?
        //app permissions? I.e does the calling app on the recipient server have access to send notifications?

        var item = new PushNotificationOutboxRecord()
        {
            SenderId = senderId,
            Options = options
        };

        _pushNotificationOutbox.Add(item);

        this.EnsureIdentityIsPending();

        return Task.FromResult(true);
    }

    public async Task ProcessBatch()
    {
        int batchSize = configuration.Host.PushNotificationBatchSize;
        var list = await _pushNotificationOutbox.GetBatchForProcessing(batchSize);

        //TODO: add throttling
        //group by appId + typeId
        var groupings = list.GroupBy(r => new Guid(ByteArrayUtil.EquiByteArrayXor(r.Options.AppId.ToByteArray(), r.Options.TypeId.ToByteArray())));

        foreach (var group in groupings)
        {
            var pushContent = new PushNotificationContent()
            {
                Payloads = new List<PushNotificationPayload>()
            };

            foreach (var record in group)
            {
                var (validAppName, appName) = await TryResolveAppName(record.Options.AppId);

                if (validAppName)
                {
                    pushContent.Payloads.Add(new PushNotificationPayload()
                    {
                        AppDisplayName = appName,
                        Options = record.Options,
                        SenderId = record.SenderId,
                        Timestamp = record.Timestamp,
                    });

                    //add to system list
                    await notificationListService.AddNotification(record.SenderId, new AddNotificationRequest()
                    {
                        Timestamp = record.Timestamp,
                        AppNotificationOptions = record.Options,
                    });

                    await _pushNotificationOutbox.MarkComplete(record.Marker);
                }
                else
                {
                    Log.Warning($"No app registered with Id {record.Options.AppId}");
                }
            }

            await this.Push(pushContent);
        }
    }

    private async Task<(bool success, string appName)> TryResolveAppName(Guid appId)
    {
        if (appId == SystemAppConstants.OwnerAppId)
        {
            return (true, "Homebase Owner");
        }

        if (appId == SystemAppConstants.FeedAppId)
        {
            return (true, "Homebase Feed");
        }

        var appReg = await appRegistrationService.GetAppRegistration(appId);
        return (appReg != null, appReg?.Name);
    }

    public async Task Push(PushNotificationContent content)
    {
        contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        var subscriptions = await GetAllSubscriptions();
        var keys = keyService.GetNotificationsKeys();

        foreach (var deviceSubscription in subscriptions)
        {
            //TODO: enforce sub.ExpirationTime

            var subscription = new PushSubscription(deviceSubscription.Endpoint, deviceSubscription.P256DH, deviceSubscription.Auth);
            var vapidDetails = new VapidDetails(configuration.Host.PushNotificationSubject, keys.PublicKey64, keys.PrivateKey64);

            var data = OdinSystemSerializer.Serialize(content);

            //TODO: this will probably need to get an http client via @Seb's work
            var webPushClient = new WebPushClient();
            try
            {
                await webPushClient.SendNotificationAsync(subscription, data, vapidDetails);
            }
            catch (WebPushException exception)
            {
                Log.Warning($"Failed sending push notification [{exception.PushSubscription}]");
                //TODO: collect all errors and send back to client or do something with it
            }
        }
    }

    public Task AddDevice(PushNotificationSubscription subscription)
    {
        contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
        //TODO: validate expiration time

        subscription.AccessRegistrationId = GetDeviceKey();
        subscription.SubscriptionStartedDate = UnixTimeUtc.Now();

        _deviceSubscriptionStorage.Upsert(subscription.AccessRegistrationId, _deviceStorageDataType, subscription);
        return Task.CompletedTask;
    }

    public Task<PushNotificationSubscription> GetDeviceSubscription()
    {
        contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

        return Task.FromResult(_deviceSubscriptionStorage.Get<PushNotificationSubscription>(GetDeviceKey()));
    }

    public Task RemoveDevice()
    {
        return this.RemoveDevice(GetDeviceKey());
    }

    public Task RemoveDevice(Guid deviceKey)
    {
        contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

        _deviceSubscriptionStorage.Delete(deviceKey);
        return Task.CompletedTask;
    }

    public async Task RemoveAllDevices()
    {
        contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
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

    private Guid GetDeviceKey()
    {
        //Transition code: we want to keep existing subscriptions so...
        var key = contextAccessor.GetCurrent().Caller.OdinClientContext.DevicePushNotificationKey;

        if (null == key)
        {
            key = contextAccessor.GetCurrent().Caller.OdinClientContext?.AccessRegistrationId;
        }

        if (key.HasValue)
        {
            return key.GetValueOrDefault();
        }

        throw new OdinSystemException("The access registration id was not set on the context");
    }

    private void EnsureIdentityIsPending()
    {
        var tenant = contextAccessor.GetCurrent().Tenant;
        serverSystemStorage.EnqueueJob(tenant, CronJobType.PushNotification, tenant.DomainName.ToLower().ToUtf8ByteArray(), UnixTimeUtc.Now());
    }

    public Task Handle(ConnectionRequestAccepted notification, CancellationToken cancellationToken)
    {
        this.EnqueueNotification(notification.Recipient, new AppNotificationOptions()
        {
            AppId = SystemAppConstants.OwnerAppId,
            TypeId = notification.NotificationTypeId,
            TagId = notification.Recipient.ToHashId(),
            Silent = false
        });
        return Task.CompletedTask;
    }

    public Task Handle(ConnectionRequestReceived notification, CancellationToken cancellationToken)
    {
        this.EnqueueNotification(notification.Sender, new AppNotificationOptions()
        {
            AppId = SystemAppConstants.OwnerAppId,
            TypeId = notification.NotificationTypeId,
            TagId = notification.Sender.ToHashId(),
            Silent = false
        });

        return Task.CompletedTask;
    }
}