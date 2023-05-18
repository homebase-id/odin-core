using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Youverse.Core.Serialization;
using Youverse.Core.Services.AppNotifications.ClientNotifications;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.Management;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Services.Transit.ReceivingHost;

namespace Youverse.Core.Services.AppNotifications
{
    public class AppNotificationHandler : INotificationHandler<IClientNotification>, INotificationHandler<IDriveNotification>,
        INotificationHandler<TransitFileReceivedNotification>
    {
        private readonly DeviceSocketCollection _deviceSocketCollection;
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly TransitInboxProcessor _transitInboxProcessor;
        private readonly DriveManager _driveManager;

        public AppNotificationHandler(DotYouContextAccessor contextAccessor, TransitInboxProcessor transitInboxProcessor, DriveManager driveManager)
        {
            _contextAccessor = contextAccessor;
            _transitInboxProcessor = transitInboxProcessor;
            _driveManager = driveManager;
            _deviceSocketCollection = new DeviceSocketCollection();
        }

        public async Task Connect(WebSocket socket, EstablishConnectionRequest request)
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
                DeviceAuthToken = null, //TODO: where is the best place to get the cookie?
                Socket = socket,
                Drives = drives
            };

            _deviceSocketCollection.AddSocket(deviceSocket);

            var response = new EstablishConnectionResponse() { };
            await SendMessageAsync(deviceSocket, DotYouSystemSerializer.Serialize(response));
            await AwaitCommands(deviceSocket);
        }

        /// <summary>
        /// Awaits the configuration when establishing a new web socket connection
        /// </summary>
        public async Task EstablishConnection(WebSocket webSocket)
        {
            var buffer = new byte[1024 * 4];

            //Wait for the caller to request the connection parameters
            var receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            EstablishConnectionRequest request = null;
            if (receiveResult.MessageType == WebSocketMessageType.Text) //must be JSON
            {
                Array.Resize(ref buffer, receiveResult.Count);
                var decryptedBytes = SharedSecretEncryptedPayload.Decrypt(buffer, _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey);
                request = DotYouSystemSerializer.Deserialize<EstablishConnectionRequest>(decryptedBytes);
            }

            if (null == request)
            {
                //send a close method
                await webSocket.SendAsync(
                    new ArraySegment<byte>(buffer, 0, 0),
                    WebSocketMessageType.Close,
                    WebSocketMessageFlags.EndOfMessage,
                    CancellationToken.None);
            }

            await this.Connect(webSocket, request);
        }

        private async Task AwaitCommands(DeviceSocket deviceSocket)
        {
            var buffer = new byte[1024 * 4];

            while (true)
            {
                var receiveResult = await deviceSocket.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

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
                    var decryptedBytes = SharedSecretEncryptedPayload.Decrypt(buffer, _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey);
                    var command = DotYouSystemSerializer.Deserialize<SocketCommand>(decryptedBytes);
                    if (null != command)
                    {
                        await ProcessCommand(command);
                    }
                }
            }
        }

        public async Task Handle(IClientNotification notification, CancellationToken cancellationToken)
        {
            var shouldEncrypt = notification.NotificationType is ClientNotificationType.ConnectionRequestAccepted or ClientNotificationType.ConnectionRequestReceived;

            var json = DotYouSystemSerializer.Serialize(new
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
            var data = DotYouSystemSerializer.Serialize(new
            {
                TargetDrive = _driveManager.GetDrive(notification.File.DriveId).GetAwaiter().GetResult().TargetDriveInfo,
                Header = notification.SharedSecretEncryptedFileHeader
            });

            var translated = new TranslatedClientNotification(notification.NotificationType, data);
            await SerializeSendToAllDevicesForDrive(notification.File.DriveId, translated);
        }

        public async Task Handle(TransitFileReceivedNotification notification, CancellationToken cancellationToken)
        {
            var notificationDriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(notification.TempFile.TargetDrive);
            var translated = new TranslatedClientNotification(notification.NotificationType,
                DotYouSystemSerializer.Serialize(new
                {
                    ExternalFileIdentifier = notification.TempFile,
                    TransferFileType = notification.TransferFileType,
                    FileSystemType = notification.FileSystemType
                }));

            await SerializeSendToAllDevicesForDrive(notificationDriveId, translated, false);
        }

        private async Task SerializeSendToAllDevicesForDrive(Guid targetDriveId, IClientNotification notification, bool encrypt = true)
        {
            var json = DotYouSystemSerializer.Serialize(new
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
                    var key = _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey;
                    var encryptedPayload = SharedSecretEncryptedPayload.Encrypt(message.ToUtf8ByteArray(), key);
                    message = DotYouSystemSerializer.Serialize(encryptedPayload);
                }

                var json = DotYouSystemSerializer.Serialize(new ClientNotificationPayload()
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
            catch (Exception e)
            {
                //HACK: need to find out what is trying to write when the response is complete
                Console.WriteLine(e);
            }
        }

        public async Task ProcessCommand(SocketCommand command)
        {
            //process the command
            switch (command.Command)
            {
                case SocketCommandType.ProcessTransitInstructions:
                    var d = DotYouSystemSerializer.Deserialize<ExternalFileIdentifier>(command.Data);
                    await _transitInboxProcessor.ProcessInbox(d.TargetDrive);
                    break;

                case SocketCommandType.ProcessInbox:
                    var request = DotYouSystemSerializer.Deserialize<ProcessInboxRequest>(command.Data);
                    await _transitInboxProcessor.ProcessInbox(request.TargetDrive, request.BatchSize);
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