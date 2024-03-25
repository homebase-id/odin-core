using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HttpClientFactoryLite;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Dto;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Core.Util;
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
using Refit;
using WebPush;

namespace Odin.Services.AppNotifications.Push;

public class PushNotificationService(
    ILogger<PushNotificationService> logger,
    ICorrelationContext correlationContext,
    TenantSystemStorage storage,
    OdinContextAccessor contextAccessor,
    PublicPrivateKeyService keyService,
    TenantSystemStorage tenantSystemStorage,
    ServerSystemStorage serverSystemStorage,
    NotificationListService notificationListService,
    IAppRegistrationService appRegistrationService,
    IHttpClientFactory httpClientFactory,
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
                    logger.LogWarning("No app registered with Id {id}", record.Options.AppId);
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

        var tasks = new List<Task>();
        foreach (var subscription in subscriptions)
        {

            if (string.IsNullOrEmpty(subscription.FirebaseDeviceToken))
            {
                tasks.Add(WebPush(subscription, keys, content));
            }
            else
            {
                tasks.Add(DevicePush(subscription, content));
            }
        }
        await Task.WhenAll(tasks);
    }

    private async Task WebPush(PushNotificationSubscription subscription, NotificationEccKeys keys, PushNotificationContent content)
    {
        var pushSubscription = new PushSubscription(subscription.Endpoint, subscription.P256DH, subscription.Auth);
        var vapidDetails = new VapidDetails(configuration.Host.PushNotificationSubject, keys.PublicKey64, keys.PrivateKey64);

        var data = OdinSystemSerializer.Serialize(content);

        //TODO: this will probably need to get an http client via @Seb's work
        var webPushClient = new WebPushClient();
        try
        {
            await webPushClient.SendNotificationAsync(pushSubscription, data, vapidDetails);
        }
        catch (WebPushException exception)
        {
            logger.LogWarning("Failed sending web push notification {notification}", exception.PushSubscription);
            //TODO: collect all errors and send back to client or do something with it
        }
    }

    private async Task DevicePush(PushNotificationSubscription subscription, PushNotificationContent content)
    {
        contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        // SEB:TODO popluate the request
        var request = new DevicePushNotificationRequest()
        {
            CorrelationId = correlationContext.Id,
            DeviceToken = subscription.FirebaseDeviceToken,
            Title = "The Title",
            Body = "The Body",
            OriginDomain = "The Origin Domain",
            Signature = "The Signature"
        };

        logger.LogDebug("Sending push notication to {deviceToken}", subscription.FirebaseDeviceToken);

        try
        {
            await TryRetry.WithBackoffAsync(5, TimeSpan.FromSeconds(1), CancellationToken.None, () =>
            {
                // SEB:TODO this should be a configuration value
                // var httpClient = httpClientFactory.CreateClient<PushNotificationService>(new Uri("https://push.homebase.id"));
                var httpClient = httpClientFactory.CreateClient<PushNotificationService>(new Uri("http://localhost:5123"));
                var push = RestService.For<IDevicePushNotificationApi>(httpClient);
                return push.PostMessage(request);
            });
        }
        catch (Exception)
        {
            // SEB:TODO remove the subscription if push services reports invalid device
            // SEB:TODO log the Refit.ValidationApiException body text if status is 4xx or 503
            logger.LogError(
                "Failed sending device push notification (SEB:TODO log the Refit.ValidationApiException body text if status is 4xx or 503)");
        }
    }

    public Task AddDevice(PushNotificationSubscription subscription)
    {
        contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);
        
        //TODO: validate expiration time
        
        subscription.AccessRegistrationId = GetDeviceKey();
        subscription.SubscriptionStartedDate = UnixTimeUtc.Now();

        _deviceSubscriptionStorage.Upsert(subscription.AccessRegistrationId, _deviceStorageDataType, subscription);
        return Task.CompletedTask;
    }

    public Task<PushNotificationSubscription> GetDeviceSubscription()
    {
        contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);
        
        return Task.FromResult(_deviceSubscriptionStorage.Get<PushNotificationSubscription>(GetDeviceKey()));
    }

    public Task RemoveDevice()
    {
        contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        return this.RemoveDevice(GetDeviceKey());
    }
    
    public Task RemoveDevice(Guid deviceKey)
    {
        contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

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
        contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

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
        serverSystemStorage.EnqueueJob(tenant, CronJobType.PushNotification, tenant.DomainName.ToLower().ToUtf8ByteArray());
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