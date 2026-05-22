using System;
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
using Odin.Services.Tenant.Container;

#nullable enable

namespace Odin.Services.AppNotifications.WebSocket
{
    /// <summary>
    /// Per-tenant singleton owning the single pub/sub subscription and WebSocket fan-out for the
    /// peer-app notification pipeline. Counterpart of <see cref="AppNotificationDispatcher"/>;
    /// see that class for why the subscription must be shared across all connections of a tenant.
    /// Uses a distinct channel and a distinct <see cref="SharedDeviceSocketCollection{T}"/> from
    /// the app pipeline, so the two never cross-contaminate.
    /// </summary>
    public class PeerAppNotificationDispatcher
    {
        public const string NotificationChannel = nameof(PeerAppNotificationHandler);

        private readonly ILogger<PeerAppNotificationDispatcher> _logger;
        private readonly ITenantRootScope _tenantRootScope;
        private readonly ITenantPubSub _pubSub;
        private readonly SharedDeviceSocketCollection<PeerAppNotificationHandler> _deviceSocketCollection;
        private readonly RefCountedSubscription _notificationSubscription;

        public PeerAppNotificationDispatcher(
            ILogger<PeerAppNotificationDispatcher> logger,
            ITenantRootScope tenantRootScope,
            ITenantPubSub pubSub,
            SharedDeviceSocketCollection<PeerAppNotificationHandler> deviceSocketCollection)
        {
            _logger = logger;
            _tenantRootScope = tenantRootScope;
            _pubSub = pubSub;
            _deviceSocketCollection = deviceSocketCollection;
            _notificationSubscription = new RefCountedSubscription(_pubSub, NotificationChannel, NotificationHandler);
        }

        //
        // Connection lifecycle (called per-connection by PeerAppNotificationHandler)
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
            var json = OdinSystemSerializer.Serialize(new
            {
                notification.NotificationType,
                Data = notification.GetClientData()
            });

            var message = new ClientNotificationMessage
            {
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
                await SendMessageAsync(deviceSocket, notification.Json, true);
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
                NotificationType  = notification.NotificationType,
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

                await SendMessageAsync(
                    deviceSocket,
                    json,
                    encrypt: true);
            }
        }

        //

        public async Task SendErrorMessageAsync(DeviceSocket deviceSocket, string errorText, CancellationToken cancellationToken = default)
        {
            await SendMessageAsync(deviceSocket, OdinSystemSerializer.Serialize(new
                {
                    NotificationType = ClientNotificationType.Error,
                    Data = errorText,
                }),
                deviceSocket.DeviceOdinContext?.PermissionsContext?.SharedSecretKey != null,
                sendEvenIfNoDeviceOdinContext: false,
                cancellationToken);
        }

        //

        public async Task SendMessageAsync(
            DeviceSocket deviceSocket,
            string message,
            bool encrypt,
            bool sendEvenIfNoDeviceOdinContext = false,
            CancellationToken cancellationToken = default)
        {
            var socket = deviceSocket.Socket;

            if (socket is not { State: WebSocketState.Open } || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (deviceSocket.DeviceOdinContext == null)
            {
                await _deviceSocketCollection.RemoveSocket(deviceSocket.Key);
                _logger.LogInformation("Invalid/Stale Device found; removing from list");
                if (sendEvenIfNoDeviceOdinContext)
                {
                    var payload = new ClientNotificationPayload()
                    {
                        IsEncrypted = false,
                        Payload = message
                    };

                    var json = OdinSystemSerializer.Serialize(payload);
                    await deviceSocket.FireAndForgetAsync(json, cancellationToken);
                }

                return;
            }

            try
            {
                if (encrypt)
                {
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
                _logger.LogWarning("WebSocketException: {error}", e.Message);
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
    }
}
