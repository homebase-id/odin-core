using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Core.Services.AppNotifications.ClientNotifications;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Mediator;
using Odin.Core.Services.Peer.ReceivingHost;

namespace Odin.Core.Services.AppNotifications.WebSocket
{
    public class AppNotificationHandler : INotificationHandler<IClientNotification>, INotificationHandler<IDriveNotification>,
        INotificationHandler<TransitFileReceivedNotification>
    {
        private readonly DeviceSocketCollection _deviceSocketCollection;
        private readonly OdinContextAccessor _contextAccessor;
        private readonly TransitInboxProcessor _transitInboxProcessor;
        private readonly DriveManager _driveManager;
        private readonly ILogger<AppNotificationHandler> _logger;

        public AppNotificationHandler(
            OdinContextAccessor contextAccessor,
            TransitInboxProcessor transitInboxProcessor,
            DriveManager driveManager,
            ILogger<AppNotificationHandler> logger)
        {
            _contextAccessor = contextAccessor;
            _transitInboxProcessor = transitInboxProcessor;
            _driveManager = driveManager;
            _logger = logger;
            _deviceSocketCollection = new DeviceSocketCollection();
        }

        public async Task Connect(System.Net.WebSockets.WebSocket socket, EstablishConnectionRequest request)
        {
            var dotYouContext = _contextAccessor.GetCurrent();

            List<Guid> drives = new List<Guid>();
            foreach (var td in request.Drives)
            {
                var driveId = dotYouContext.PermissionsContext.GetDriveId(td);
                dotYouContext.PermissionsContext.AssertCanReadDrive(driveId);
                drives.Add(driveId);
            }

            var deviceSocket = new DeviceSocket()
            {
                Key = Guid.NewGuid(),
                SharedSecretKey = _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey,
                DeviceAuthToken = null, //TODO: where is the best place to get the cookie?
                Socket = socket,
                Drives = drives
            };

            _deviceSocketCollection.AddSocket(deviceSocket);

            var response = new EstablishConnectionResponse() { };
            await SendMessageAsync(deviceSocket, OdinSystemSerializer.Serialize(response));
            await AwaitCommands(deviceSocket);
        }

        /// <summary>
        /// Awaits the configuration when establishing a new web socket connection
        /// </summary>
        public async Task EstablishConnection(System.Net.WebSockets.WebSocket webSocket)
        {
            var buffer = new byte[1024 * 4];

            try
            {
                //Wait for the caller to request the connection parameters
                var receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                EstablishConnectionRequest request = null;
                if (receiveResult.MessageType == WebSocketMessageType.Text) //must be JSON
                {
                    Array.Resize(ref buffer, receiveResult.Count);
                    var decryptedBytes = SharedSecretEncryptedPayload.Decrypt(buffer, _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey);
                    request = OdinSystemSerializer.Deserialize<EstablishConnectionRequest>(decryptedBytes);
                }

                if (null == request)
                {
                    //send a close method
                    await webSocket.CloseOutputAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Closing",
                        CancellationToken.None);
                }
                else
                {
                    await Connect(webSocket, request);
                }
            }
            catch (WebSocketException e)
            {
                _logger.LogWarning("WebSocketException: {error}", e.Message);
            }
            catch (System.Text.Json.JsonException e)
            {
                _logger.LogWarning("JsonException: {error}", e.Message);
            }
        }

        private async Task AwaitCommands(DeviceSocket deviceSocket)
        {
            var buffer = new byte[1024 * 4];

            while (true)
            {
                try
                {
                    var receiveResult =
                        await deviceSocket.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        await _deviceSocketCollection.RemoveSocket(deviceSocket.Key);
                        //TODO: need to send the right response but not quite sure what that is.
                        // await socket.CloseAsync(receiveResult.CloseStatus.Value, "", CancellationToken.None);
                        break;
                    }

                    if (receiveResult.MessageType == WebSocketMessageType.Text) //must be JSON
                    {
                        Array.Resize(ref buffer, receiveResult.Count);
                        var decryptedBytes = SharedSecretEncryptedPayload.Decrypt(buffer,
                            _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey);
                        var command = OdinSystemSerializer.Deserialize<SocketCommand>(decryptedBytes);
                        if (null != command)
                        {
                            await ProcessCommand(deviceSocket, command);
                        }
                    }
                }
                catch (WebSocketException e)
                {
                    _logger.LogWarning("WebSocketException: {error}", e.Message);
                    break;
                }
                catch (System.Text.Json.JsonException e)
                {
                    _logger.LogWarning("JsonException: {error}", e.Message);
                    break;
                }
            }
        }

        public async Task Handle(IClientNotification notification, CancellationToken cancellationToken)
        {
            var shouldEncrypt = !(notification.NotificationType is ClientNotificationType.ConnectionRequestAccepted or ClientNotificationType.ConnectionRequestReceived);

            var json = OdinSystemSerializer.Serialize(new
            {
                NotificationType = notification.NotificationType,
                Data = notification.GetClientData()
            });

            var sockets = this._deviceSocketCollection.GetAll().Values;
            foreach (var deviceSocket in sockets)
            {
                await this.SendMessageAsync(deviceSocket, json, shouldEncrypt);
            }
        }

        public async Task Handle(IDriveNotification notification, CancellationToken cancellationToken)
        {
            var hasSharedSecret = null != _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey;

            var data = OdinSystemSerializer.Serialize(new
            {
                TargetDrive = _driveManager.GetDrive(notification.File.DriveId).GetAwaiter().GetResult().TargetDriveInfo,
                Header = hasSharedSecret ? DriveFileUtility.ConvertToSharedSecretEncryptedClientFileHeader(notification.ServerFileHeader, _contextAccessor) : null
            });

            var translated = new TranslatedClientNotification(notification.NotificationType, data);
            await SerializeSendToAllDevicesForDrive(notification.File.DriveId, translated);
        }

        public async Task Handle(TransitFileReceivedNotification notification, CancellationToken cancellationToken)
        {
            var notificationDriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(notification.TempFile.TargetDrive);
            var translated = new TranslatedClientNotification(notification.NotificationType,
                OdinSystemSerializer.Serialize(new
                {
                    ExternalFileIdentifier = notification.TempFile,
                    TransferFileType = notification.TransferFileType,
                    FileSystemType = notification.FileSystemType
                }));

            await SerializeSendToAllDevicesForDrive(notificationDriveId, translated, false);
        }

        private async Task SerializeSendToAllDevicesForDrive(Guid targetDriveId, IClientNotification notification, bool encrypt = true)
        {
            var json = OdinSystemSerializer.Serialize(new
            {
                NotificationType = notification.NotificationType,
                Data = notification.GetClientData()
            });

            var sockets = this._deviceSocketCollection.GetAll().Values
                .Where(ds => ds.Drives.Any(driveId => driveId == targetDriveId));

            foreach (var deviceSocket in sockets)
            {
                await this.SendMessageAsync(deviceSocket, json, encrypt);
            }
        }


        private async Task SendMessageAsync(DeviceSocket deviceSocket, string message, bool encrypt = true)
        {
            var socket = deviceSocket.Socket;

            if (socket.State != WebSocketState.Open)
            {
                return;
            }

            try
            {
                if (encrypt)
                {
                    // var key = _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey;
                    var key = deviceSocket.SharedSecretKey;
                    var encryptedPayload = SharedSecretEncryptedPayload.Encrypt(message.ToUtf8ByteArray(), key);
                    message = OdinSystemSerializer.Serialize(encryptedPayload);
                }

                var json = OdinSystemSerializer.Serialize(new ClientNotificationPayload()
                {
                    IsEncrypted = encrypt,
                    Payload = message
                });

                var jsonBytes = json.ToUtf8ByteArray();

                await socket.SendAsync(
                    buffer: new ArraySegment<byte>(jsonBytes, 0, json.Length),
                    messageType: WebSocketMessageType.Text,
                    messageFlags: GetMessageFlags(endOfMessage: true, compressMessage: true),
                    cancellationToken: CancellationToken.None);
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

        public async Task ProcessCommand(DeviceSocket deviceSocket, SocketCommand command)
        {
            //process the command
            switch (command.Command)
            {
                case SocketCommandType.ProcessTransitInstructions:
                    var d = OdinSystemSerializer.Deserialize<ExternalFileIdentifier>(command.Data);
                    await _transitInboxProcessor.ProcessInbox(d.TargetDrive);
                    break;

                case SocketCommandType.ProcessInbox:
                    var request = OdinSystemSerializer.Deserialize<ProcessInboxRequest>(command.Data);
                    await _transitInboxProcessor.ProcessInbox(request.TargetDrive, request.BatchSize);
                    break;

                case SocketCommandType.Ping:
                    await this.SendMessageAsync(deviceSocket, OdinSystemSerializer.Serialize(new
                    {
                        NotificationType = ClientNotificationType.Pong,
                    }));
                    break;

                default:
                    throw new Exception("Invalid command");
            }
        }

        private static WebSocketMessageFlags GetMessageFlags(bool endOfMessage, bool compressMessage)
        {
            WebSocketMessageFlags flags = WebSocketMessageFlags.None;

            if (endOfMessage)
            {
                flags |= WebSocketMessageFlags.EndOfMessage;
            }

            if (!compressMessage)
            {
                flags |= WebSocketMessageFlags.DisableCompression;
            }

            return flags;
        }
    }
}
