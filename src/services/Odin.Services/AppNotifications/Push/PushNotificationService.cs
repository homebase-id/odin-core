using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
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
using Odin.Core.Storage.Database.Identity.Wrappers;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Core.X509;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.Data;
using Odin.Services.Apps;
using Odin.Services.Authorization;
using Odin.Services.Authorization.Apps;
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
    TableKeyTwoValueCached twoKeyValue,
    ClientRegistrationStorage clientRegistrationStorage)
    : INotificationHandler<ConnectionRequestAcceptedNotification>,
        INotificationHandler<ConnectionRequestReceivedNotification>,
        INotificationHandler<ShamirPasswordRecoverySufficientShardsCollectedNotification>,
        INotificationHandler<ShamirPasswordRecoveryShardCollectedNotification>,
        INotificationHandler<ShamirPasswordRecoveryShardRequestedNotification>,
        INotificationHandler<TemporalDriveAccessedNotification>
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
        subscription.AppId = odinContext.Caller.HasMasterKey ? null : odinContext.Caller.OdinClientContext?.AppId?.Value;
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

    /// <summary>Stores <paramref name="subscription"/> as it is, under its key</summary>
    internal Task SaveSubscriptionAsync(PushNotificationSubscription subscription) =>
        DeviceSubscriptionStorage.UpsertAsync(twoKeyValue, subscription.AccessRegistrationId, DeviceStorageDataType, subscription);

    /// <summary>
    /// For a push running in parallel with the others (PushAsync): its own scope, as this scope's connection is shared
    /// </summary>
    private async Task RemoveDeviceInOwnScopeAsync(PushNotificationSubscription subscription, IOdinContext odinContext)
    {
        await using var dbScope = scope.BeginLifetimeScope();
        await RemoveDeviceAsync(dbScope.Resolve<TableKeyTwoValueCached>(), subscription.AccessRegistrationId, odinContext);
    }

    /// <summary>Keeps the subscription but forgets its PushKit token; the next Ring goes out as an alert until the app re-registers one.</summary>
    private static async Task ClearVoipTokenAsync(TableKeyTwoValueCached twoKeyValue, PushNotificationSubscription subscription)
    {
        subscription.VoipDeviceToken = null;
        await DeviceSubscriptionStorage.UpsertAsync(twoKeyValue, subscription.AccessRegistrationId, DeviceStorageDataType, subscription);
    }

    public static async Task RemoveDeviceAsync(TableKeyTwoValueCached twoKeyValue, Guid deviceKey, IOdinContext odinContext)
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
                AppId = SystemAppConstants.SystemAppId,
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
                AppId = SystemAppConstants.SystemAppId,
                TypeId = notification.NotificationTypeId,
                TagId = notification.Sender.ToHashId(),
                Silent = false
            },
            notification.OdinContext);
    }

    public async Task Handle(TemporalDriveAccessedNotification notification, CancellationToken cancellationToken)
    {
        await this.EnqueueNotificationInternalAsync(notification.Accessor, new AppNotificationOptions()
            {
                AppId = SystemAppConstants.SystemAppId,
                TypeId = notification.NotificationTypeId,
                TagId = notification.Accessor.ToHashId(),
                Silent = false,
                UnEncryptedMessage = $"{notification.Accessor.DomainName} accessed your location"
            },
            notification.OdinContext);
    }

    
    public async Task Handle(ShamirPasswordRecoveryShardRequestedNotification notification, CancellationToken cancellationToken)
    {
        await this.EnqueueNotificationInternalAsync(notification.Sender, new AppNotificationOptions()
            {
                AppId = SystemAppConstants.SystemAppId,
                TypeId = notification.NotificationTypeId,
                TagId = notification.Sender.ToHashId(),
                Silent = false,
                UnEncryptedMessage = notification.AdditionalMessage
            },
            notification.OdinContext);
    }
    
    public async Task Handle(ShamirPasswordRecoveryShardCollectedNotification notification, CancellationToken cancellationToken)
    {
        await this.EnqueueNotificationInternalAsync(notification.Sender, new AppNotificationOptions()
            {
                AppId = SystemAppConstants.SystemAppId,
                TypeId = notification.NotificationTypeId,
                TagId = notification.Sender.ToHashId(),
                Silent = false,
                UnEncryptedMessage = notification.AdditionalMessage
            },
            notification.OdinContext);
    }

    public async Task Handle(ShamirPasswordRecoverySufficientShardsCollectedNotification notification, CancellationToken cancellationToken)
    {
        // using the tenant here since this is more of a system notification
        var sender = notification.OdinContext.Tenant;
        await this.EnqueueNotificationInternalAsync(sender, new AppNotificationOptions()
            {
                AppId = SystemAppConstants.SystemAppId,
                TypeId = notification.NotificationTypeId,
                TagId = sender.ToHashId(),
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

        // Every app first, one at a time: the lookups use this scope's connection, which the pushes below share in parallel
        foreach (var subscription in subscriptions.Where(sub => string.IsNullOrEmpty(sub.FirebaseDeviceToken)))
        {
            await FillAppIdAsync(subscription);
        }

        var tasks = new List<Task>();
        foreach (var subscription in subscriptions)
        {
            if (string.IsNullOrEmpty(subscription.FirebaseDeviceToken))
            {
                var forSubscriber = WebPushContentFor(subscription, content);
                if (forSubscriber != null)
                {
                    tasks.Add(WebPushAsync(subscription, keys, forSubscriber, odinContext, cancellationToken));
                }
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

    /// <summary>
    /// A subscription made before the app was kept (#2006): when an app made it, its key is that app client's access
    /// registration, so the app is found there and kept, and the lookup isn't made again for it.  The owner console's key
    /// is no app client: its subscriptions stay null, looked up on every push.  A failed lookup leaves it null, so the push
    /// still goes out (as before this was kept).
    /// </summary>
    internal async Task FillAppIdAsync(PushNotificationSubscription subscription)
    {
        if (subscription.AppId != null)
        {
            return;
        }

        try
        {
            var (client, row) = await clientRegistrationStorage.GetWithRowAsync<AppClientRegistration>(subscription.AccessRegistrationId);
            if (row?.catType != AppClientRegistration.CatType || client?.AppId?.Value is not { } appId)
            {
                return;
            }

            subscription.AppId = appId;

            // Read again before writing: the device may have subscribed again, or unsubscribed, since this push read it.
            // Not atomic: an unsubscribe between this read and the write would be undone, once, for an older subscription
            var stored = await DeviceSubscriptionStorage.GetAsync<PushNotificationSubscription>(twoKeyValue, subscription.AccessRegistrationId);
            if (stored is { AppId: null } && stored.Endpoint == subscription.Endpoint)
            {
                stored.AppId = appId;
                await SaveSubscriptionAsync(stored);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Could not find the app of push subscription {subscription}; it gets every app's pushes",
                subscription.AccessRegistrationId);
        }
    }

    /// <summary>
    /// The payloads of <paramref name="content"/> for a web push subscription (see
    /// <see cref="PushNotificationSubscription.AppId"/>); null when none is left to send.
    /// </summary>
    internal static PushNotificationContent WebPushContentFor(PushNotificationSubscription subscription,
        PushNotificationContent content)
    {
        if (subscription.AppId == null)
        {
            return content;
        }

        var payloads = content.Payloads.Where(p => p.Options?.AppId == subscription.AppId).ToList();
        return payloads.Count == 0 ? null : new PushNotificationContent { Payloads = payloads };
    }

    private async Task WebPushAsync(PushNotificationSubscription subscription, NotificationEccKeys keys, PushNotificationContent content,
        IOdinContext odinContext,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Attempting WebPush Notification - start");

        var pushSubscription = new PushSubscription(subscription.Endpoint, subscription.P256DH, subscription.Auth);
        var vapidDetails = new VapidDetails(configuration.Host.PushNotificationSubject, keys.PublicKey64, keys.PrivateKey64);

        var data = OdinSystemSerializer.Serialize(content);
        var options = WebPushOptions(content, vapidDetails);

        var webPushClient = new WebPushClient();
        try
        {
            await webPushClient.SendNotificationAsync(pushSubscription, data, options, cancellationToken);
        }
        catch (WebPushException exception)
        {
            if (exception.Message.StartsWith("Subscription no longer valid", true, CultureInfo.InvariantCulture))
            {
                await RemoveDeviceInOwnScopeAsync(subscription, odinContext);
                logger.LogInformation(
                    "Received WebPushException with message [{message}] removing subscription for device with accessRegistrationId: {device}",
                    exception.Message, subscription.AccessRegistrationId);

                return;
            }

            if (exception.Message.StartsWith("Received unexpected response code: 403", true, CultureInfo.InvariantCulture))
            {
                await RemoveDeviceInOwnScopeAsync(subscription, odinContext);
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

                await RemoveDeviceInOwnScopeAsync(subscription, odinContext);
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

    // RFC 8030: a Topic is at most 32 URL-safe base64 characters.
    private static readonly Regex WebPushTopicPattern = new("^[A-Za-z0-9_-]{1,32}$", RegexOptions.Compiled);

    /// <summary>
    /// The WebPush library takes TTL and raw HTTP headers through an options dictionary. A WebPush
    /// carries one payload (the outbox worker builds one per item); the delivery options come from
    /// that payload, and a content with any other count gets none.
    /// </summary>
    private static Dictionary<string, object> WebPushOptions(PushNotificationContent content, VapidDetails vapidDetails)
    {
        var options = new Dictionary<string, object> { ["vapidDetails"] = vapidDetails };
        var delivery = content.Payloads.Count == 1 ? content.Payloads[0].Options : null;
        if (delivery == null)
        {
            return options;
        }

        if (delivery.TimeToLiveSeconds is > 0)
        {
            options["TTL"] = delivery.TimeToLiveSeconds.Value;
        }

        var headers = new Dictionary<string, object>();
        if (delivery.TimeSensitive)
        {
            headers["Urgency"] = "high";
        }
        if (delivery.CollapseId != null && WebPushTopicPattern.IsMatch(delivery.CollapseId))
        {
            headers["Topic"] = delivery.CollapseId;
        }
        if (headers.Count > 0)
        {
            options["headers"] = headers;
        }

        return options;
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

        logger.LogDebug("Sending push notification to subscription {subscription}", subscription.AccessRegistrationId);

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
                TimeToLiveSeconds = payload.Options.TimeToLiveSeconds,
                CollapseId = payload.Options.CollapseId,
                Silent = payload.Options.Silent,
                TimeSensitive = payload.Options.TimeSensitive,
                // The relay chooses the platform push type from these (Odin.PushNotification.PushRouter).
                Kind = payload.Options.Kind.ToString(),
                VoipDeviceToken = subscription.VoipDeviceToken,
            };

            var baseUri = new Uri(configuration.PushNotification.BaseUrl);
            var httpClient = httpClientFactory.CreateClient(baseUri.Host);
            httpClient.BaseAddress = baseUri;
            httpClient.DefaultRequestHeaders.Add(OdinHeaderNames.CorrelationId, correlationContext.Id);
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
                        if (problem is { Status: (int)HttpStatusCode.BadGateway, Type: DevicePushNotificationRequestV1.ProblemTypes.DeviceGone })
                        {
                            logger.LogDebug("Removing subscription {subscription}", subscription.AccessRegistrationId);
                            // PushAsync can call DevicePushAsync multiple times in parallel,
                            // so we need to make separate scopes to call the database
                            await using var dbScope = scope.BeginLifetimeScope();
                            var tkv = dbScope.Resolve<TableKeyTwoValueCached>();
                            await RemoveDeviceAsync(tkv, subscription.AccessRegistrationId, odinContext);
                        }
                        else if (problem is { Status: (int)HttpStatusCode.BadGateway, Type: DevicePushNotificationRequestV1.ProblemTypes.VoipTokenGone })
                        {
                            // Only the PushKit token is dead (PushKit rotates them); the FCM one was
                            // fine. Forget the VoIP token and let the retry send this push as an alert.
                            logger.LogDebug("Clearing VoIP token on subscription {subscription}", subscription.AccessRegistrationId);
                            await using var dbScope = scope.BeginLifetimeScope();
                            var tkv = dbScope.Resolve<TableKeyTwoValueCached>();
                            await ClearVoipTokenAsync(tkv, subscription);
                            request.VoipDeviceToken = null;
                            throw;
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
        // Every route ends here. The kind's defaults are filled in first so they are stored and
        // travel every hop; then the bounds are what the outbox and the relay accept, so an
        // out-of-range value is refused now rather than dropped by the relay later.
        PushKindDefaults.Apply(options);
        PushDeliveryOptionsValidation.AssertDeliveryBounds(options);

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