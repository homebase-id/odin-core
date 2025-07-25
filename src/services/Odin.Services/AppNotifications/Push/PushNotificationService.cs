using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Dto;
using Odin.Core.Exceptions;
using Odin.Core.Http;
using Odin.Core.Identity;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Refit;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Core.X509;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.Data;
using Odin.Services.Apps;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Mediator;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Refit;
using WebPush;

namespace Odin.Services.AppNotifications.Push;

public class PushNotificationService(
    ILogger<PushNotificationService> logger,
    ICorrelationContext correlationContext,
    PublicPrivateKeyService keyService,
    NotificationListService notificationListService,
    IDynamicHttpClientFactory httpClientFactory,
    ICertificateStore certificateStore,
    OdinConfiguration configuration,
    PeerOutbox peerOutbox,
    IMediator mediator,
    ILifetimeScope scope,
    TableKeyTwoValue twoKeyValue)
    : INotificationHandler<ConnectionRequestAcceptedNotification>,
        INotificationHandler<ConnectionRequestReceivedNotification>
{
    const string DeviceStorageContextKey = "9a9cacb4-b76a-4ad4-8340-e681691a2ce4";
    const string DeviceStorageDataTypeKey = "1026f96f-f85f-42ed-9462-a18b23327a33";

    private static readonly TwoKeyValueStorage DeviceSubscriptionStorage =
        TenantSystemStorage.CreateTwoKeyValueStorage(Guid.Parse(DeviceStorageContextKey));

    private static readonly byte[] DeviceStorageDataType = Guid.Parse(DeviceStorageDataTypeKey).ToByteArray();

    /// <summary>
    /// Adds a notification to the outbox
    /// </summary>
    public async Task<bool> EnqueueNotification(OdinId senderId, AppNotificationOptions options, IOdinContext odinContext)
    {
        //validate the calling app on the recipient server have access to send notifications
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);
        return await EnqueueNotificationInternalAsync(senderId, options, odinContext);
    }


    public async Task AddDeviceAsync(PushNotificationSubscription subscription, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        //TODO: validate expiration time

        subscription.AccessRegistrationId = GetDeviceKey(odinContext);
        subscription.SubscriptionStartedDate = UnixTimeUtc.Now();

        await DeviceSubscriptionStorage.UpsertAsync(twoKeyValue, subscription.AccessRegistrationId, DeviceStorageDataType, subscription);
    }

    public async Task<PushNotificationSubscription> GetDeviceSubscriptionAsync(IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);
        return await DeviceSubscriptionStorage.GetAsync<PushNotificationSubscription>(twoKeyValue, GetDeviceKey(odinContext));
    }

    public async Task RemoveDeviceAsync(IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);
        await RemoveDeviceAsync(GetDeviceKey(odinContext), odinContext);
    }

    public async Task RemoveDeviceAsync(Guid deviceKey, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);
        await DeviceSubscriptionStorage.DeleteAsync(twoKeyValue, deviceKey);
    }

    public static async Task RemoveDeviceAsync(TableKeyTwoValue twoKeyValue, Guid deviceKey, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);
        await DeviceSubscriptionStorage.DeleteAsync(twoKeyValue, deviceKey);
    }

    public async Task RemoveAllDevicesAsync(IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        var subscriptions = await GetAllSubscriptionsAsync(odinContext);
        foreach (var sub in subscriptions)
        {
            await DeviceSubscriptionStorage.DeleteAsync(twoKeyValue, sub.AccessRegistrationId);
        }
    }

    public async Task<List<PushNotificationSubscription>> GetAllSubscriptionsAsync(IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);
        var subscriptions =
            await DeviceSubscriptionStorage.GetByDataTypeAsync<PushNotificationSubscription>(twoKeyValue, DeviceStorageDataType);
        return subscriptions?.ToList() ?? new List<PushNotificationSubscription>();
    }

    public async Task Handle(ConnectionRequestAcceptedNotification notification, CancellationToken cancellationToken)
    {
        await this.EnqueueNotificationInternalAsync(notification.Recipient, new AppNotificationOptions()
            {
                AppId = SystemAppConstants.OwnerAppId,
                TypeId = notification.NotificationTypeId,
                TagId = notification.Recipient.ToHashId(),
                Silent = false
            },
            notification.OdinContext);
    }

    public async Task Handle(ConnectionRequestReceivedNotification notification, CancellationToken cancellationToken)
    {
        await this.EnqueueNotificationInternalAsync(notification.Sender, new AppNotificationOptions()
            {
                AppId = SystemAppConstants.OwnerAppId,
                TypeId = notification.NotificationTypeId,
                TagId = notification.Sender.ToHashId(),
                Silent = false
            },
            notification.OdinContext);
    }

    public async Task PushAsync(PushNotificationContent content, IOdinContext odinContext,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Attempting push notification");

        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        var subscriptions = await GetAllSubscriptionsAsync(odinContext);
        var keys = await keyService.GetEccNotificationsKeysAsync();

        var tasks = new List<Task>();
        foreach (var subscription in subscriptions)
        {
            if (string.IsNullOrEmpty(subscription.FirebaseDeviceToken))
            {
                tasks.Add(WebPushAsync(subscription, keys, content, odinContext, cancellationToken));
            }
            else
            {
                foreach (var payload in content.Payloads)
                {
                    tasks.Add(DevicePushAsync(subscription, payload, odinContext));
                }
            }
        }

        await Task.WhenAll(tasks);
    }

    private async Task WebPushAsync(PushNotificationSubscription subscription, NotificationEccKeys keys, PushNotificationContent content,
        IOdinContext odinContext,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Attempting WebPush Notification - start");

        var pushSubscription = new PushSubscription(subscription.Endpoint, subscription.P256DH, subscription.Auth);
        var vapidDetails = new VapidDetails(configuration.Host.PushNotificationSubject, keys.PublicKey64, keys.PrivateKey64);

        var data = OdinSystemSerializer.Serialize(content);

        var webPushClient = new WebPushClient();
        try
        {
            await webPushClient.SendNotificationAsync(pushSubscription, data, vapidDetails, cancellationToken);
        }
        catch (WebPushException exception)
        {
            if (exception.Message.StartsWith("Subscription no longer valid", true, CultureInfo.InvariantCulture))
            {
                await RemoveDeviceAsync(subscription.AccessRegistrationId, odinContext);
                logger.LogInformation(
                    "Received WebPushException with message [{message}] removing subscription for device with accessRegistrationId: {device}",
                    exception.Message, subscription.AccessRegistrationId);

                return;
            }

            if (exception.Message.StartsWith("Received unexpected response code: 403", true, CultureInfo.InvariantCulture))
            {
                await RemoveDeviceAsync(subscription.AccessRegistrationId, odinContext);
                logger.LogInformation(
                    "Received WebPushException with message [{message}] removing subscription for device with accessRegistrationId: {device}",
                    exception.Message, subscription.AccessRegistrationId);

                return;
            }

            if (exception.HttpResponseMessage.StatusCode == HttpStatusCode.BadRequest)
            {
                logger.LogInformation(
                    "Received BadRequest WebPushException with message [{message}] removing subscription for device with accessRegistrationId: {device}",
                    exception.Message, subscription.AccessRegistrationId);

                await RemoveDeviceAsync(subscription.AccessRegistrationId, odinContext);
            }

            logger.LogError(exception, "Failed sending web push notification {exception}.  remote status code: {code}. content: {content}",
                exception,
                exception.HttpResponseMessage.StatusCode,
                exception.HttpResponseMessage.Content);

            return;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to send web push notification");
            return;
        }

        logger.LogDebug("Attempting WebPush Notification - done; no errors reported");
    }

    private async Task DevicePushAsync(PushNotificationSubscription subscription, PushNotificationPayload payload, IOdinContext odinContext)
    {
        logger.LogDebug("Attempting DevicePush Notification");

        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        var title = string.IsNullOrWhiteSpace(payload.AppDisplayName)
            ? "Homebase Notification"
            : payload.AppDisplayName;

        var body = string.IsNullOrWhiteSpace(payload.Options.UnEncryptedMessage)
            ? $"Received from {payload.SenderId.DomainName}"
            : payload.Options.UnEncryptedMessage;

        var thisDomain = odinContext.Tenant.DomainName;
        var certificate = await certificateStore.GetCertificateAsync(thisDomain);

        // Sanity check
        if (certificate == null)
        {
            logger.LogError("No certificate found for {originDomain}. This should never happen.", thisDomain);
            return;
        }

        logger.LogDebug("Sending push notification to {deviceToken}", subscription.FirebaseDeviceToken);

        try
        {
            var messageId = Guid.NewGuid().ToString();
            var signature = certificate.CreateSignature(messageId);

            var request = new DevicePushNotificationRequestV1
            {
                Body = body,
                CorrelationId = correlationContext.Id,
                Data = OdinSystemSerializer.Serialize(payload),
                DevicePlatform = subscription.FirebaseDevicePlatform,
                DeviceToken = subscription.FirebaseDeviceToken,
                FromDomain = payload.SenderId.DomainName,
                ToDomain = thisDomain,
                Id = messageId,
                OriginDomain = thisDomain,
                Signature = signature,
                Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                Title = title,
            };

            var baseUri = new Uri(configuration.PushNotification.BaseUrl);
            var httpClient = httpClientFactory.CreateClient(baseUri.Host);
            httpClient.BaseAddress = baseUri;
            httpClient.DefaultRequestHeaders.Add(ICorrelationContext.DefaultHeaderName, correlationContext.Id);
            var push = RestService.For<IDevicePushNotificationApi>(httpClient);

            await TryRetry.Create()
                .WithAttempts(5)
                .WithExponentialBackoff(TimeSpan.FromSeconds(1))
                .ExecuteAsync(async () =>
                {
                    try
                    {
                        await push.PostMessage(request);
                    }
                    catch (ApiException apiEx)
                    {
                        var problem = await apiEx.TryGetContentAsAsync<ProblemDetails>();
                        if (problem is { Status: (int)HttpStatusCode.BadGateway, Type: "NotFound" })
                        {
                            logger.LogDebug("Removing subscription {subscription}", subscription.AccessRegistrationId);
                            // PushAsync can call DevicePushAsync multiple times in parallel,
                            // so we need to make separate scopes to call the database
                            await using var dbScope = scope.BeginLifetimeScope();
                            var tkv = dbScope.Resolve<TableKeyTwoValue>();
                            await RemoveDeviceAsync(tkv, subscription.AccessRegistrationId, odinContext);
                        }
                        else if (apiEx.StatusCode == HttpStatusCode.BadRequest)
                        {
                            logger.LogError("Failed sending device push notification: {status} - {error}",
                                apiEx.StatusCode, apiEx.Content);
                        }
                        else
                        {
                            throw;
                        }
                    }
                });
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed sending device push notification");
        }
    }

//

    private Guid GetDeviceKey(IOdinContext odinContext)
    {
        //Transition code: we want to keep existing subscriptions so...
        Guid? key = odinContext.Caller.OdinClientContext?.DevicePushNotificationKey;

        if (null == key)
        {
            key = odinContext.Caller.OdinClientContext?.AccessRegistrationId;
        }

        if (key.HasValue)
        {
            return key.GetValueOrDefault();
        }

        throw new OdinSystemException("The access registration id was not set on the context");
    }

    private async Task<bool> EnqueueNotificationInternalAsync(OdinId senderId, AppNotificationOptions options, IOdinContext odinContext)
    {
        var timestamp = UnixTimeUtc.Now().milliseconds;

        //add to system list
        await notificationListService.AddNotificationInternal(senderId, new AddNotificationRequest()
            {
                Timestamp = timestamp,
                AppNotificationOptions = options,
            },
            odinContext
        );

        // var pushNotificationsRedactedOptions = options.Redacted();

        var item = new OutboxFileItem()
        {
            Priority = 100, //super high priority to ensure these are sent quickly,
            Type = OutboxItemType.PushNotification,
            File = new InternalDriveFileId()
            {
                DriveId = Guid.NewGuid(),
                FileId = options.TagId == Guid.Empty ? SequentialGuid.CreateGuid() : options.TagId
            },
            Recipient = odinContext.Tenant,
            DependencyFileId = default,
            State = new OutboxItemState()
            {
                Data = OdinSystemSerializer.Serialize(new PushNotificationOutboxRecord()
                {
                    SenderId = senderId,
                    Options = options,
                    Timestamp = timestamp
                }).ToUtf8ByteArray()
            }
        };

        logger.LogDebug("Enqueuing notification. Sender: {senderId}, Recipient: {recipient}", senderId, odinContext.Tenant);

        await peerOutbox.AddItemAsync(item);
        await mediator.Publish(new PushNotificationEnqueuedNotification()
        {
            OdinContext = odinContext,
        });
        return true;
    }
}