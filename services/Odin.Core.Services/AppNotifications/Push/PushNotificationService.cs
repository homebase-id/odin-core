using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.AppNotifications.ClientNotifications;
using Odin.Core.Services.AppNotifications.Data;
using Odin.Core.Services.AppNotifications.SystemNotifications;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Authorization.Apps;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Core.Services.Peer;
using Odin.Core.Storage;
using Odin.Core.Time;
using Serilog;
using WebPush;


namespace Odin.Core.Services.AppNotifications.Push;

public class PushNotificationService : INotificationHandler<NewFollowerNotification>, INotificationHandler<ConnectionRequestAccepted>,
    INotificationHandler<ConnectionRequestReceived>, INotificationHandler<NewFeedItemReceived>
{
    const string DeviceStorageContextKey = "9a9cacb4-b76a-4ad4-8340-e681691a2ce4";
    const string DeviceStorageDataTypeKey = "1026f96f-f85f-42ed-9462-a18b23327a33";
    private readonly TwoKeyValueStorage _deviceSubscriptionStorage;
    private readonly OdinContextAccessor _contextAccessor;

    private readonly OdinConfiguration _configuration;
    private readonly NotificationListService _notificationListService;
    private readonly PushNotificationOutbox _pushNotificationOutbox;
    private readonly PublicPrivateKeyService _keyService;
    private readonly ServerSystemStorage _serverSystemStorage;
    private readonly byte[] _deviceStorageDataType = Guid.Parse(DeviceStorageDataTypeKey).ToByteArray();
    private readonly IAppRegistrationService _appRegistrationService;

    public PushNotificationService(TenantSystemStorage storage, OdinContextAccessor contextAccessor, PublicPrivateKeyService keyService,
        TenantSystemStorage tenantSystemStorage, ServerSystemStorage serverSystemStorage, NotificationListService notificationListService,
        IAppRegistrationService appRegistrationService, OdinConfiguration configuration)
    {
        _contextAccessor = contextAccessor;
        _keyService = keyService;
        _serverSystemStorage = serverSystemStorage;
        _notificationListService = notificationListService;
        _appRegistrationService = appRegistrationService;
        _configuration = configuration;
        _pushNotificationOutbox = new PushNotificationOutbox(tenantSystemStorage, contextAccessor);
        _deviceSubscriptionStorage = storage.CreateTwoKeyValueStorage(Guid.Parse(DeviceStorageContextKey));
    }

    /// <summary>
    /// Adds a notification to the outbox
    /// </summary>
    public Task<bool> EnqueueNotification(OdinId senderId, AppNotificationOptions options)
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
        const int batchSize = 100; //todo: configure
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
            
            foreach(var record in group)
            {
                var appReg = await _appRegistrationService.GetAppRegistration(record.Options.AppId);

                pushContent.Payloads.Add(new PushNotificationPayload()
                {
                    AppDisplayName = appReg?.Name,
                    Options = record.Options,
                    SenderId = record.SenderId,
                    Timestamp = record.Timestamp,
                });

                //add to system list
                await _notificationListService.AddNotification(record.SenderId, new AddNotificationRequest()
                {
                    Timestamp = record.Timestamp,
                    AppNotificationOptions = record.Options,
                });

                await _pushNotificationOutbox.MarkComplete(record.Marker);
            }
            
            await this.Push(pushContent);
        }
    }

    public async Task Push(PushNotificationContent content)
    {
        _contextAccessor.GetCurrent().PermissionsContext.HasPermission(PermissionKeys.SendPushNotifications);

        var subscriptions = await GetAllSubscriptions();
        var keys = _keyService.GetNotificationsKeys();

        foreach (var deviceSubscription in subscriptions)
        {
            //TODO: enforce sub.ExpirationTime

            var subscription = new PushSubscription(deviceSubscription.Endpoint, deviceSubscription.P256DH, deviceSubscription.Auth);
            var vapidDetails = new VapidDetails(_configuration.Host.PushNotificationSubject, keys.PublicKey64, keys.PrivateKey64); 

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

    private Guid GetDeviceKey()
    {
        var key = _contextAccessor.GetCurrent().Caller.OdinClientContext?.AccessRegistrationId ?? Guid.Empty;

        if (key == Guid.Empty)
        {
            throw new OdinSystemException("The access registration id was not set on the context");
        }

        return key;
    }

    private void EnsureIdentityIsPending()
    {
        var tenant = _contextAccessor.GetCurrent().Tenant;
        _serverSystemStorage.EnqueueJob(tenant, CronJobType.PushNotification, tenant.DomainName.ToLower().ToUtf8ByteArray());
    }

    public Task Handle(NewFollowerNotification notification, CancellationToken cancellationToken)
    {
        this.EnqueueNotification(notification.OdinId, new AppNotificationOptions()
        {
            AppId = SystemAppConstants.OwnerAppId,
            TypeId = notification.NotificationTypeId,
            TagId = notification.OdinId.ToHashId(),
            Silent = false
        });

        return Task.CompletedTask;
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

    public Task Handle(NewFeedItemReceived notification, CancellationToken cancellationToken)
    {
        this.EnqueueNotification(notification.Sender, new AppNotificationOptions()
        {
            AppId = SystemAppConstants.FeedAppId,
            TypeId = notification.NotificationTypeId,
            TagId = notification.Sender.ToHashId(),
            Silent = false,
            UnEncryptedMessage = "You have new content in your feed."
        });

        return Task.CompletedTask;
    }
}