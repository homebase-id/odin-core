using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Json;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.PubSub;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Services.Peer;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Tenant.Container;

#nullable enable

namespace Odin.Services.AppNotifications.WebSocket
{
    /// <summary>
    /// Per-tenant singleton that owns the single pub/sub subscription and fans WebSocket
    /// notifications out to all connected device sockets.
    ///
    /// Extracted from <see cref="AppNotificationHandler"/> so the <see cref="RefCountedSubscription"/>
    /// is shared across every connection of a tenant: connection #1 registers the one broker
    /// subscription, subsequent connections ref-count onto it, and the last to disconnect tears
    /// it down. Previously each connection (its own request scope) created its own handler and
    /// therefore its own subscription, so a single notification was delivered once PER connected
    /// socket (N copies for N devices).
    ///
    /// Only singleton-safe dependencies are held here. Per-connection / DB-bound work (the command
    /// loop, inbox processing) stays in the scoped <see cref="AppNotificationHandler"/>.
    /// </summary>
    public class AppNotificationDispatcher
    {
        public const string NotificationChannel = nameof(AppNotificationHandler);

        private readonly ILogger<AppNotificationDispatcher> _logger;
        private readonly ITenantRootScope _tenantRootScope;
        private readonly ITenantPubSub _pubSub;
        private readonly SharedDeviceSocketCollection<AppNotificationHandler> _deviceSocketCollection;
        private readonly RefCountedSubscription _notificationSubscription;

        // Per-drive coalescer for server-side inbox drains triggered by InboxItemReceived
        // notifications. The cached gate (TableInboxCached.GetReadyCountAsync) keeps repeat
        // drains cheap, but under arrival bursts we'd still do redundant DB work; this keeps
        // it to one in-flight drain per drive per process. Lazy<Task> ensures GetOrAdd's
        // value factory races never start more than one drain task.
        private readonly ConcurrentDictionary<Guid, Lazy<Task>> _inFlightDrains = new();

        public AppNotificationDispatcher(
            ILogger<AppNotificationDispatcher> logger,
            ITenantRootScope tenantRootScope,
            ITenantPubSub pubSub,
            SharedDeviceSocketCollection<AppNotificationHandler> deviceSocketCollection)
        {
            _logger = logger;
            _tenantRootScope = tenantRootScope;
            _pubSub = pubSub;
            _deviceSocketCollection = deviceSocketCollection;
            _notificationSubscription = new RefCountedSubscription(_pubSub, NotificationChannel, NotificationHandler);
        }

        //
        // Connection lifecycle (called per-connection by AppNotificationHandler)
        //

        public void AddSocket(DeviceSocket deviceSocket) => _deviceSocketCollection.AddSocket(deviceSocket);

        public Task RemoveSocket(Guid key) => _deviceSocketCollection.RemoveSocket(key);

        public Task SubscribeAsync(CancellationToken cancellationToken = default) =>
            _notificationSubscription.SubscribeAsync(cancellationToken);

        public Task UnsubscribeAsync(CancellationToken cancellationToken = default) =>
            _notificationSubscription.UnsubscribeAsync(cancellationToken);

        //

        private async Task NotificationHandler(JsonEnvelope envelope)
        {
            var message = envelope.DeserializeMessage();

            switch (message)
            {
                case ClientNotificationMessage notification:
                    await WsPublishAsync(notification);
                    return;
                case DriveNotificationMessage notification:
                    await WsPublishAsync(notification);
                    return;
                case InboxItemReceivedNotificationMessage notification:
                    await WsPublishAsync(notification);
                    return;
                case null:
                    _logger.LogError("Received null message");
                    return;
                default:
                    _logger.LogError("Unknown message type: {message}", message.GetType().Name);
                    return;
            }
        }

        //
        // IClientNotification
        //

        // Src: MediatR
        // Dst: PubSub queue
        public async Task PublishClientNotificationAsync(IClientNotification notification)
        {
            var shouldEncrypt =
                !(notification.NotificationType is ClientNotificationType.ConnectionRequestAccepted
                    or ClientNotificationType.ConnectionRequestReceived);

            var json = OdinSystemSerializer.Serialize(new
            {
                notification.NotificationType,
                Data = notification.GetClientData()
            });

            var message = new ClientNotificationMessage
            {
                ShouldEncrypt = shouldEncrypt,
                Json = json,
            };

            await _pubSub.PublishAsync(NotificationChannel, JsonEnvelope.Create(message));
        }

        //

        // Src: PubSub queue
        // Dst: WebSocket
        private async Task WsPublishAsync(ClientNotificationMessage notification)
        {
            var sockets = _deviceSocketCollection.GetAll().Values;
            foreach (var deviceSocket in sockets)
            {
                await SendMessageAsync(
                    deviceSocket,
                    notification.Json,
                    notification.ShouldEncrypt);
            }
        }

        //
        // IDriveNotification
        //

        // Src: MediatR
        // Dst: PubSub queue
        public async Task PublishDriveNotificationAsync(IDriveNotification notification)
        {
            var message = new DriveNotificationMessage
            {
                NotificationType = notification.NotificationType,
                File = notification.File,
                IsDeleteNotification = notification is DriveFileDeletedNotification,
                ServerFileHeader = notification.ServerFileHeader,
                PreviousServerFileHeader = (notification as DriveFileDeletedNotification)?.PreviousServerFileHeader
            };

            await _pubSub.PublishAsync(NotificationChannel, JsonEnvelope.Create(message));
        }

        //

        // Src: PubSub queue
        // Dst: WebSocket
        private async Task WsPublishAsync(DriveNotificationMessage notification)
        {
            var sockets = _deviceSocketCollection.GetAll().Values
                .Where(ds => ds.Drives.Any(driveId => driveId == notification.File.DriveId))
                .ToList();

            if (sockets.Count == 0)
            {
                return;
            }

            await using var scope = _tenantRootScope.BeginLifetimeScope();
            if (scope == null)
            {
                return;
            }

            var driveManager =  scope.Resolve<IDriveManager>();
            var drive = await driveManager.GetDriveAsync(notification.File.DriveId);

            foreach (var deviceSocket in sockets)
            {
                var deviceOdinContext = deviceSocket.DeviceOdinContext;
                var hasSharedSecret = null != deviceOdinContext?.PermissionsContext?.SharedSecretKey;

                var o = new ClientDriveNotification
                {
                    TargetDrive = drive?.TargetDriveInfo,
                    Header = hasSharedSecret
                        ? DriveFileUtility.CreateClientFileHeader(notification.ServerFileHeader, deviceOdinContext)
                        : null,
                    PreviousServerFileHeader = hasSharedSecret
                        ? notification.IsDeleteNotification
                            ? DriveFileUtility.CreateClientFileHeader(notification.PreviousServerFileHeader, deviceOdinContext!)
                            : null
                        : null
                };

                var json = OdinSystemSerializer.Serialize(new
                {
                    notification.NotificationType,
                    Data = OdinSystemSerializer.Serialize(o)
                });

                await SendMessageAsync(deviceSocket, json, encrypt: true);
            }
        }

        //
        // InboxItemReceivedNotification
        //

        // TODO: This notification is largely redundant now. TryDrainInboxForDriveAsync runs
        // synchronously before we send InboxItemReceived to clients, and any
        // DriveFileAddedNotification it produces fans out as FileAdded over the same WS
        // (via PublishDriveNotificationAsync) — carrying the actual file header. The bare
        // "something arrived" ping is therefore useful only when no FileAdded will follow.
        // Suppress per TransferFileType once clients have migrated: keep it for types that
        // don't produce a FileAdded (e.g. read receipts, reactions) and drop it for the rest.

        // Src: MediatR
        // Dst: PubSub queue
        public async Task PublishInboxItemReceivedAsync(InboxItemReceivedNotification notification)
        {
            var message = new InboxItemReceivedNotificationMessage
            {
                NotificationType = notification.NotificationType,
                TargetDrive = notification.TargetDrive,
                FileSystemType = notification.FileSystemType,
                TransferFileType = notification.TransferFileType,
            };

            await _pubSub.PublishAsync(NotificationChannel, JsonEnvelope.Create(message));
        }

        // Src: PubSub queue
        // Dst: WebSocket
        private async Task WsPublishAsync(InboxItemReceivedNotificationMessage notification)
        {
            var notificationDriveId = notification.TargetDrive.Alias;

            // Opportunistic server-side inbox drain, mirroring the QueryBatch path.
            // We use an owner-acting context already cached on a locally-subscribed
            // DeviceSocket; any DriveFileAdded events the drain produces will fan out
            // through PublishDriveNotificationAsync onto the same sockets.
            await TryDrainInboxForDriveAsync(notificationDriveId);

            var translated = new TranslatedClientNotification(notification.NotificationType,
                OdinSystemSerializer.Serialize(new
                {
                    TargetDrive = notification.TargetDrive,
                    notification.TransferFileType,
                    notification.FileSystemType
                }));

            await SerializeSendToAllDevicesForDrive(notificationDriveId, translated, false);
        }

        //

        private async Task TryDrainInboxForDriveAsync(Guid driveId)
        {
            var deviceOdinContext = _deviceSocketCollection.GetAll().Values
                .Select(ds => ds.DeviceOdinContext)
                .FirstOrDefault(ctx => ctx != null && PeerInboxDriveQueue.IsAuthorizedToDrain(driveId, ctx));

            if (deviceOdinContext == null)
            {
                return;
            }

            var lazy = _inFlightDrains.GetOrAdd(driveId,
                id => new Lazy<Task>(() => RunDrainAsync(id, deviceOdinContext)));

            try
            {
                await lazy.Value;
            }
            catch (Exception e)
            {
                // InboxDrainOnQuery already swallows and logs internally; this is defensive
                // so a drain failure can never break the WS notification fan-out.
                _logger.LogInformation(e, "Inbox drain on WS notify failed: {error}", e.Message);
            }
        }

        private async Task RunDrainAsync(Guid driveId, IOdinContext deviceOdinContext)
        {
            try
            {
                await using var scope = _tenantRootScope.BeginLifetimeScope();
                if (scope == null)
                {
                    return;
                }

                var drain = scope.Resolve<InboxDrainOnQuery>();
                await drain.DrainIfReadyAsync(driveId, deviceOdinContext);
            }
            finally
            {
                _inFlightDrains.TryRemove(driveId, out _);
            }
        }

        //

        private async Task SerializeSendToAllDevicesForDrive(
            Guid targetDriveId,
            IClientNotification notification,
            bool encrypt,
            CancellationToken cancellationToken = default)
        {
            var json = OdinSystemSerializer.Serialize(new
            {
                notification.NotificationType,
                Data = notification.GetClientData()
            });

            var sockets = _deviceSocketCollection.GetAll().Values
                .Where(ds => ds.Drives.Any(driveId => driveId == targetDriveId));

            foreach (var deviceSocket in sockets)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    await SendMessageAsync(deviceSocket, json, encrypt, cancellationToken);
                }
            }
        }

        //

        private static string JsonMessage(ClientNotificationType notificationType, string message)
        {
            return OdinSystemSerializer.Serialize(new
            {
                NotificationType = notificationType,
                Data = message,
            });
        }

        //

        public async Task SendErrorMessageAsync(DeviceSocket deviceSocket, string errorText, CancellationToken cancellationToken = default)
        {
            var json = JsonMessage(ClientNotificationType.Error, errorText);
            await SendMessageAsync(
                deviceSocket,
                json,
                deviceSocket.DeviceOdinContext?.PermissionsContext?.SharedSecretKey != null,
                cancellationToken);
        }

        //

        public async Task SendMessageAsync(
            DeviceSocket deviceSocket,
            string message,
            bool encrypt,
            CancellationToken cancellationToken = default)
        {
            var socket = deviceSocket.Socket;

            if (socket is not { State: WebSocketState.Open } || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                if (encrypt)
                {
                    if (deviceSocket.DeviceOdinContext == null)
                    {
                        await _deviceSocketCollection.RemoveSocket(deviceSocket.Key);
                        _logger.LogInformation("Invalid/Stale Device found; removing from list; closing socket");
                        throw new WebSocketException("Missing device odin context");
                    }

                    if (deviceSocket.DeviceOdinContext.PermissionsContext?.SharedSecretKey == null)
                    {
                        throw new OdinSystemException("Cannot encrypt message without shared secret key");
                    }

                    var key = deviceSocket.DeviceOdinContext.PermissionsContext.SharedSecretKey;
                    var encryptedPayload = SharedSecretEncryptedPayload.Encrypt(message.ToUtf8ByteArray(), key);
                    message = OdinSystemSerializer.Serialize(encryptedPayload);
                }

                var payload = new ClientNotificationPayload()
                {
                    IsEncrypted = encrypt,
                    Payload = message
                };

                var json = OdinSystemSerializer.Serialize(payload);
                await deviceSocket.FireAndForgetAsync(json, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // ignore, this exception is expected when the socket is closing behind the scenes
            }
            catch (WebSocketException e)
            {
                _logger.LogInformation("WebSocketException: {error}", e.Message);
            }
            catch (Exception e)
            {
                //HACK: need to find out what is trying to write when the response is complete
                _logger.LogError(e, "SendMessageAsync: {error}", e.Message);
            }
        }

        //

        private class ClientNotificationMessage
        {
            public required bool ShouldEncrypt { get; init; }
            public required string Json { get; init; }
        }

        private class DriveNotificationMessage
        {
            public required ClientNotificationType NotificationType { get; init; }
            public required InternalDriveFileId File { get; init; }
            public required bool IsDeleteNotification { get; init; }
            public required ServerFileHeader ServerFileHeader { get; init; }
            public required ServerFileHeader? PreviousServerFileHeader { get; init; }
        }

        private class InboxItemReceivedNotificationMessage
        {
            public required ClientNotificationType NotificationType { get; init; }
            public required TargetDrive TargetDrive { get; init; }
            public required FileSystemType FileSystemType { get; init; }
            public required TransferFileType TransferFileType { get; init; }
        }
    }
}
