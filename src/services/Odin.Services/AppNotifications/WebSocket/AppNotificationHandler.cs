using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Json;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.PubSub;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.WebRtcSignaling;
using Odin.Services.AppNotifications.WebRtcSignaling.PeerRelay;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Services.Peer;
using Odin.Services.Peer.Incoming.Drive.Transfer;

#nullable enable

// SEB:TODO this file needs to be combined with AppNotificationHandler.cs

// SEB:TODO this file needs to be split up into different classes handling
// - websocket incoming and outgoing messages
// - client notifications (i.e. messages coming from the "inside" and going out to a websocket)

// SEB:TODO cleanup SendMessageAsync params

namespace Odin.Services.AppNotifications.WebSocket
{
    public class AppNotificationHandler :
        INotificationHandler<IClientNotification>,
        INotificationHandler<IDriveNotification>,
        INotificationHandler<InboxItemReceivedNotification>
    {
        private const string NotificationChannel = nameof(AppNotificationHandler);

        private readonly ILogger<AppNotificationHandler> _logger;
        private readonly ITenantPubSub _pubSub;
        private readonly ILifetimeScope _tenantScope;
        private readonly PeerInboxProcessor _peerInboxProcessor;
        private readonly WebRtcSignalingService _webRtcSignalingService;
        private readonly SharedDeviceSocketCollection<AppNotificationHandler> _deviceSocketCollection;
        private readonly RefCountedSubscription _notificationSubscription;

        public AppNotificationHandler(
            ILogger<AppNotificationHandler> logger,
            ITenantPubSub pubSub,
            ILifetimeScope tenantScope,
            PeerInboxProcessor peerInboxProcessor,
            WebRtcSignalingService webRtcSignalingService,
            SharedDeviceSocketCollection<AppNotificationHandler> deviceSocketCollection)
        {
            _logger = logger;
            _pubSub = pubSub;
            _tenantScope = tenantScope;
            _peerInboxProcessor = peerInboxProcessor;
            _webRtcSignalingService = webRtcSignalingService;
            _deviceSocketCollection = deviceSocketCollection;

            _notificationSubscription =
                new RefCountedSubscription(_pubSub, NotificationChannel, NotificationHandler);
        }

        //


        /// <summary>
        /// Awaits the configuration when establishing a new web socket connection
        /// </summary>
        public async Task EstablishConnection(
            System.Net.WebSockets.WebSocket webSocket,
            IOdinContext odinContext,
            CancellationToken cancellationToken = default,
            string? remoteIpAddress = null,
            int? remotePort = null)
        {
            var webSocketKey = Guid.NewGuid();
            try
            {
                var deviceSocket = new DeviceSocket
                {
                    Key = webSocketKey,
                    Socket = webSocket,
                    RemoteIpAddress = remoteIpAddress,
                    RemotePort = remotePort,
                };
                _deviceSocketCollection.AddSocket(deviceSocket);

                await _notificationSubscription.SubscribeAsync(cancellationToken);
                await AwaitCommands(deviceSocket, odinContext, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // ignore, this exception is expected when the socket is closing behind the scenes
            }
            catch (WebSocketException e) when (e.Message ==
                                               "The remote party closed the WebSocket connection without completing the close handshake.")
            {
                // ignore, this exception is expected when the client doesn't play by the rules
            }
            catch (Exception e)
            {
                _logger.LogError(e, "WebSocket: {error}", e.Message);
            }
            finally
            {
                await _notificationSubscription.UnsubscribeAsync(CancellationToken.None);
                await _deviceSocketCollection.RemoveSocket(webSocketKey);
                _logger.LogTrace("WebSocket closed");
            }
        }

        //

        private async Task AwaitCommands(
            DeviceSocket deviceSocket,
            IOdinContext odinContext,
            CancellationToken cancellationToken = default)
        {
            var webSocket = deviceSocket.Socket;
            while (!cancellationToken.IsCancellationRequested && webSocket?.State == WebSocketState.Open)
            {
                var buffer = new ArraySegment<byte>(new byte[4096]);
                WebSocketReceiveResult receiveResult;
                using var ms = new MemoryStream();
                do
                {
                    receiveResult = await webSocket.ReceiveAsync(buffer, cancellationToken);
                    ms.Write(buffer.Array!, buffer.Offset, receiveResult.Count);
                } while (!receiveResult.EndOfMessage);

                ms.Seek(0, SeekOrigin.Begin);

                if (receiveResult.MessageType == WebSocketMessageType.Text) //must be JSON
                {
                    var completeMessage = ms.ToArray();
                    var sharedSecret = odinContext.PermissionsContext.SharedSecretKey;

                    byte[] decryptedBytes;
                    try
                    {
                        decryptedBytes = SharedSecretEncryptedPayload.Decrypt(completeMessage, sharedSecret);
                    }
                    catch (Exception)
                    {
                        // We can get here if the browser forgets to pre-auth the websocket connection...
                        await SendErrorMessageAsync(deviceSocket, "Error decrypting message", cancellationToken);

                        return; // hangup!
                    }

                    SocketCommand? command;
                    var errorText = "Error deserializing socket command";
                    try
                    {
                        command = OdinSystemSerializer.Deserialize<SocketCommand>(decryptedBytes) ?? null;
                    }
                    catch (JsonException e)
                    {
                        command = null;
                        errorText += ": " + e.Message;
                    }

                    if (command == null)
                    {
                        await SendErrorMessageAsync(deviceSocket, errorText, cancellationToken);
                    }
                    else
                    {
                        try
                        {
                            await ProcessCommand(deviceSocket, command, odinContext, cancellationToken);
                        }
                        catch (OperationCanceledException) // also CloseWebSocketException
                        {
                            return;
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, "Unhandled exception while processing command: {command}", command.Command);
                            var error = $"Unhandled exception on the backend while processing command: {command.Command}";
                            await SendErrorMessageAsync(deviceSocket, error, cancellationToken);

                            return; // hangup!
                        }
                    }
                }
            }
        }

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
        public async Task Handle(IClientNotification notification, CancellationToken cancellationToken = default)
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

        // True if any WebSocket session is currently open for this tenant. Used by the
        // peer WebRTC relay controller to short-circuit with "offline" when there's no one
        // to deliver to, rather than publishing a notification that would be a no-op.
        public bool HasOpenSockets() => _deviceSocketCollection.GetAll().Count > 0;

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
        public async Task Handle(IDriveNotification notification, CancellationToken cancellationToken = default)
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

            await using var scope = _tenantScope.BeginLifetimeScope();
            var driveManager =  scope.Resolve<IDriveManager>();
            var driveId = notification.ServerFileHeader.FileMetadata.File.DriveId;
            var drive = await driveManager.GetDriveAsync(driveId);

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

        // Src: MediatR
        // Dst: PubSub queue
        public async Task Handle(InboxItemReceivedNotification notification, CancellationToken cancellationToken = default)
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

        private async Task SendErrorMessageAsync(DeviceSocket deviceSocket, string errorText, CancellationToken cancellationToken = default)
        {
            var json = JsonMessage(ClientNotificationType.Error, errorText);
            await SendMessageAsync(
                deviceSocket,
                json,
                deviceSocket.DeviceOdinContext?.PermissionsContext?.SharedSecretKey != null,
                cancellationToken);
        }

        //

        private async Task SendMessageAsync(
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

        private async Task ProcessCommand(
            DeviceSocket deviceSocket,
            SocketCommand command,
            IOdinContext odinContext,
            CancellationToken cancellationToken = default)
        {
            //process the command
            switch (command.Command)
            {
                case SocketCommandType.EstablishConnectionRequest:
                    try
                    {
                        var drives = new List<Guid>();
                        var options = OdinSystemSerializer.Deserialize<EstablishConnectionOptions>(command.Data) ??
                                      new EstablishConnectionOptions()
                                      {
                                          Drives = []
                                      };

                        foreach (var td in options.Drives)
                        {
                            var driveId = td.Alias;
                            odinContext.PermissionsContext.AssertCanReadDrive(driveId);
                            drives.Add(driveId);
                        }

                        deviceSocket.DeviceOdinContext = odinContext.Clone();
                        deviceSocket.Drives = drives;
                    }
                    catch (OdinSecurityException e)
                    {
                        var json = JsonMessage(ClientNotificationType.AuthenticationError, e.Message);
                        await SendMessageAsync(
                            deviceSocket,
                            json,
                            encrypt: false,
                            cancellationToken);

                        throw new CloseWebSocketException();
                    }

                    var response = new EstablishConnectionResponse();
                    await SendMessageAsync(
                        deviceSocket,
                        OdinSystemSerializer.Serialize(response),
                        encrypt: true,
                        cancellationToken);
                    break;

                case SocketCommandType.ProcessTransitInstructions:
                    var d = OdinSystemSerializer.Deserialize<ExternalFileIdentifier>(command.Data);
                    if (d != null)
                    {
                        await _peerInboxProcessor.ProcessInboxAsync(d.TargetDrive, odinContext);
                    }
                    break;

                case SocketCommandType.ProcessInbox:
                    var request = OdinSystemSerializer.Deserialize<ProcessInboxRequest>(command.Data);
                    if (request != null)
                    {
                        await _peerInboxProcessor.ProcessInboxAsync(request.TargetDrive, odinContext, request.BatchSize);
                    }
                    break;

                case SocketCommandType.Ping:
                    var pong = OdinSystemSerializer.Serialize(new { NotificationType = ClientNotificationType.Pong });
                    await SendMessageAsync(deviceSocket, pong, encrypt: true, cancellationToken);
                    break;

                case SocketCommandType.CallInvite:
                {
                    var p = OdinSystemSerializer.Deserialize<CallInvitePayload>(command.Data);
                    if (p != null)
                    {
                        var req = new WebRtcRelayRequest { SignalType = WebRtcSignalType.Invite, CallId = p.CallId };
                        await RelayAndReportAsync(deviceSocket, p.To, p.CallId, req, odinContext, cancellationToken);
                    }
                    break;
                }

                case SocketCommandType.CallOffer:
                {
                    var p = OdinSystemSerializer.Deserialize<CallOfferPayload>(command.Data);
                    if (p != null)
                    {
                        var req = new WebRtcRelayRequest { SignalType = WebRtcSignalType.Offer, CallId = p.CallId, Sdp = p.Sdp };
                        await RelayAndReportAsync(deviceSocket, p.To, p.CallId, req, odinContext, cancellationToken);
                    }
                    break;
                }

                case SocketCommandType.CallAnswer:
                {
                    var p = OdinSystemSerializer.Deserialize<CallAnswerPayload>(command.Data);
                    if (p != null)
                    {
                        var req = new WebRtcRelayRequest { SignalType = WebRtcSignalType.Answer, CallId = p.CallId, Sdp = p.Sdp };
                        await RelayAndReportAsync(deviceSocket, p.To, p.CallId, req, odinContext, cancellationToken);
                    }
                    break;
                }

                case SocketCommandType.CallIce:
                {
                    var p = OdinSystemSerializer.Deserialize<CallIcePayload>(command.Data);
                    if (p != null)
                    {
                        var req = new WebRtcRelayRequest
                        {
                            SignalType = WebRtcSignalType.Ice,
                            CallId = p.CallId,
                            Candidate = p.Candidate,
                            SdpMid = p.SdpMid,
                            SdpMLineIndex = p.SdpMLineIndex,
                        };
                        await RelayAndReportAsync(deviceSocket, p.To, p.CallId, req, odinContext, cancellationToken);
                    }
                    break;
                }

                case SocketCommandType.CallHangup:
                {
                    var p = OdinSystemSerializer.Deserialize<CallHangupPayload>(command.Data);
                    if (p != null)
                    {
                        var req = new WebRtcRelayRequest { SignalType = WebRtcSignalType.Hangup, CallId = p.CallId };
                        await RelayAndReportAsync(deviceSocket, p.To, p.CallId, req, odinContext, cancellationToken);
                    }
                    break;
                }

                case SocketCommandType.CallReject:
                {
                    var p = OdinSystemSerializer.Deserialize<CallRejectPayload>(command.Data);
                    if (p != null)
                    {
                        var req = new WebRtcRelayRequest { SignalType = WebRtcSignalType.Reject, CallId = p.CallId };
                        await RelayAndReportAsync(deviceSocket, p.To, p.CallId, req, odinContext, cancellationToken);
                    }
                    break;
                }

                case SocketCommandType.Whoami:
                {
                    var data = new WhoamiResponseData
                    {
                        PublicIp = deviceSocket.RemoteIpAddress ?? "",
                        PublicPort = deviceSocket.RemotePort ?? 0,
                    };
                    await SendMessageAsync(deviceSocket, JsonNotification(ClientNotificationType.WhoamiResponse, data),
                        encrypt: true, cancellationToken);
                    break;
                }

                default:
                    await SendErrorMessageAsync(deviceSocket, "Invalid command", cancellationToken);
                    break;
            }
        }

        // Drives a single signaling message from sender's socket → recipient's server. On
        // anything other than Delivered=true, sends a callUnavailable back to the sender's
        // socket only (mirrors the Pong pattern — direct reply, no MediatR fan-out).
        private async Task RelayAndReportAsync(
            DeviceSocket deviceSocket,
            OdinId recipient,
            Guid callId,
            WebRtcRelayRequest request,
            IOdinContext odinContext,
            CancellationToken cancellationToken)
        {
            var outcome = await _webRtcSignalingService.RelayAsync(recipient, request, odinContext, cancellationToken);
            if (outcome.Delivered)
            {
                return;
            }

            var data = new CallUnavailableData { CallId = callId, Reason = outcome.FailureReason };
            await SendMessageAsync(deviceSocket, JsonNotification(ClientNotificationType.CallUnavailable, data),
                encrypt: true, cancellationToken);
        }

        private static string JsonNotification(ClientNotificationType type, object data)
        {
            return OdinSystemSerializer.Serialize(new
            {
                NotificationType = type,
                Data = OdinSystemSerializer.Serialize(data),
            });
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