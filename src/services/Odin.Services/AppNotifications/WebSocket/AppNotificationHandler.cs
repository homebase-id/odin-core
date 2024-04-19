using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Services.Peer.Incoming.Drive.Transfer;

namespace Odin.Services.AppNotifications.WebSocket
{
    public class AppNotificationHandler :
        INotificationHandler<IClientNotification>,
        INotificationHandler<IDriveNotification>,
        INotificationHandler<TransitFileReceivedNotification>
    {
        private readonly DeviceSocketCollection _deviceSocketCollection;

        private readonly PeerInboxProcessor _peerInboxProcessor;
        private readonly DriveManager _driveManager;
        private readonly ILogger<AppNotificationHandler> _logger;

        public AppNotificationHandler(
            PeerInboxProcessor peerInboxProcessor,
            DriveManager driveManager,
            ILogger<AppNotificationHandler> logger)
        {
            _peerInboxProcessor = peerInboxProcessor;
            _driveManager = driveManager;
            _logger = logger;
            _deviceSocketCollection = new DeviceSocketCollection();
        }

        //

        /// <summary>
        /// Awaits the configuration when establishing a new web socket connection
        /// </summary>
        public async Task EstablishConnection(System.Net.WebSockets.WebSocket webSocket, CancellationToken cancellationToken, IOdinContext odinContext)
        {
            var webSocketKey = Guid.NewGuid();
            try
            {
                var deviceSocket = new DeviceSocket
                {
                    Key = webSocketKey,
                    Socket = webSocket,
                };
                _deviceSocketCollection.AddSocket(deviceSocket);
                await AwaitCommands(deviceSocket, cancellationToken, odinContext);
            }
            catch (OperationCanceledException)
            {
                // ignore, this exception is expected when the socket is closing behind the scenes
            }
            catch (WebSocketException e) when (e.Message == "The remote party closed the WebSocket connection without completing the close handshake.")
            {
                // ignore, this exception is expected when the client doesn't play by the rules
            }
            catch (Exception e)
            {
                _logger.LogError(e, "WebSocket: {error}", e.Message);
            }
            finally
            {
                _deviceSocketCollection.RemoveSocket(webSocketKey);
                if (webSocket.State != WebSocketState.Closed && webSocket.State != WebSocketState.Aborted)
                {
                    try
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, cancellationToken);
                    }
                    catch (Exception)
                    {
                        // End of the line - nothing we can do here
                    }
                }

                _logger.LogTrace("WebSocket closed");
            }
        }

        //

        private async Task AwaitCommands(DeviceSocket deviceSocket, CancellationToken cancellationToken, IOdinContext odinContext)
        {
            var webSocket = deviceSocket.Socket;
            while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
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

                    SocketCommand command = null;
                    var errorText = "Error deserializing socket command";
                    try
                    {
                        command = OdinSystemSerializer.Deserialize<SocketCommand>(decryptedBytes);
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
                            await ProcessCommand(deviceSocket, command, cancellationToken, odinContext);
                        }
                        catch (OperationCanceledException)
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

        public async Task Handle(IClientNotification notification, CancellationToken cancellationToken)
        {
            var shouldEncrypt =
                !(notification.NotificationType is ClientNotificationType.ConnectionRequestAccepted or ClientNotificationType.ConnectionRequestReceived);

            var json = OdinSystemSerializer.Serialize(new
            {
                NotificationType = notification.NotificationType,
                Data = notification.GetClientData()
            });

            var sockets = _deviceSocketCollection.GetAll().Values;
            foreach (var deviceSocket in sockets)
            {
                await SendMessageAsync(deviceSocket, json, cancellationToken, shouldEncrypt);
            }
        }

        //

        public async Task Handle(IDriveNotification notification, CancellationToken cancellationToken)
        {
            var odinContext = notification.OdinContext;
            var hasSharedSecret = null != odinContext.PermissionsContext.SharedSecretKey;

            var data = OdinSystemSerializer.Serialize(new
            {
                TargetDrive = (await _driveManager.GetDrive(notification.File.DriveId)).TargetDriveInfo,
                Header = hasSharedSecret
                    ? DriveFileUtility.ConvertToSharedSecretEncryptedClientFileHeader(notification.ServerFileHeader, odinContext)
                    : null
            });

            var translated = new TranslatedClientNotification(notification.NotificationType, data);
            await SerializeSendToAllDevicesForDrive(notification.File.DriveId, translated, cancellationToken);
        }

        //

        public async Task Handle(TransitFileReceivedNotification notification, CancellationToken cancellationToken)
        {
            var notificationDriveId = notification.OdinContext.PermissionsContext.GetDriveId(notification.TempFile.TargetDrive);
            var translated = new TranslatedClientNotification(notification.NotificationType,
                OdinSystemSerializer.Serialize(new
                {
                    ExternalFileIdentifier = notification.TempFile,
                    TransferFileType = notification.TransferFileType,
                    FileSystemType = notification.FileSystemType
                }));

            await SerializeSendToAllDevicesForDrive(notificationDriveId, translated, cancellationToken, false);
        }

        //

        private async Task SerializeSendToAllDevicesForDrive(
            Guid targetDriveId,
            IClientNotification notification,
            CancellationToken cancellationToken,
            bool encrypt = true)
        {
            var json = OdinSystemSerializer.Serialize(new
            {
                NotificationType = notification.NotificationType,
                Data = notification.GetClientData()
            });

            var sockets = _deviceSocketCollection.GetAll().Values
                .Where(ds => ds.Drives.Any(driveId => driveId == targetDriveId));

            foreach (var deviceSocket in sockets)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    await SendMessageAsync(deviceSocket, json, cancellationToken, encrypt);
                }
            }
        }

        //

        private async Task SendErrorMessageAsync(DeviceSocket deviceSocket, string errorText, CancellationToken cancellationToken)
        {
            await SendMessageAsync(deviceSocket, OdinSystemSerializer.Serialize(new
                {
                    NotificationType = ClientNotificationType.Error,
                    Data = errorText,
                }), cancellationToken,
                deviceSocket.SharedSecretKey != null);
        }

        //

        private async Task SendMessageAsync(DeviceSocket deviceSocket, string message, CancellationToken cancellationToken, bool encrypt = true)
        {
            var socket = deviceSocket.Socket;

            if (socket.State != WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                if (encrypt)
                {
                    if (deviceSocket.SharedSecretKey == null)
                    {
                        throw new OdinSystemException("Cannot encrypt message without shared secret key");
                    }

                    // var key = odinContext.PermissionsContext.SharedSecretKey;
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
                    cancellationToken: cancellationToken);
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

        private async Task ProcessCommand(DeviceSocket deviceSocket, SocketCommand command, CancellationToken cancellationToken, IOdinContext odinContext)
        {
            //process the command
            switch (command.Command)
            {
                case SocketCommandType.EstablishConnectionRequest:
                    try
                    {
                        var drivesRequest = OdinSystemSerializer.Deserialize<List<TargetDrive>>(command.Data);
                        var drives = new List<Guid>();
                        foreach (var td in drivesRequest)
                        {
                            var driveId = odinContext.PermissionsContext.GetDriveId(td);
                            odinContext.PermissionsContext.AssertCanReadDrive(driveId);
                            drives.Add(driveId);
                        }

                        deviceSocket.SharedSecretKey = odinContext.PermissionsContext.SharedSecretKey;
                        deviceSocket.DeviceAuthToken = null; //TODO: where is the best place to get the cookie?
                        deviceSocket.Drives = drives;
                    }
                    catch (OdinSecurityException e)
                    {
                        var error = $"[Command:{command.Command}] {e.Message}";
                        await SendErrorMessageAsync(deviceSocket, error, cancellationToken);
                        throw new CloseWebSocketException();
                    }

                    var response = new EstablishConnectionResponse();
                    await SendMessageAsync(deviceSocket, OdinSystemSerializer.Serialize(response), cancellationToken);
                    break;

                case SocketCommandType.ProcessTransitInstructions:
                    var d = OdinSystemSerializer.Deserialize<ExternalFileIdentifier>(command.Data);
                    await _peerInboxProcessor.ProcessInbox(d.TargetDrive, odinContext);
                    break;

                case SocketCommandType.ProcessInbox:
                    var request = OdinSystemSerializer.Deserialize<ProcessInboxRequest>(command.Data);
                    await _peerInboxProcessor.ProcessInbox(request.TargetDrive, odinContext, request.BatchSize);
                    break;

                case SocketCommandType.Ping:
                    await SendMessageAsync(deviceSocket, OdinSystemSerializer.Serialize(new
                    {
                        NotificationType = ClientNotificationType.Pong,
                    }), cancellationToken);
                    break;

                default:
                    await SendErrorMessageAsync(deviceSocket, "Invalid command", cancellationToken);
                    break;
            }
        }

        //

        private static WebSocketMessageFlags GetMessageFlags(bool endOfMessage, bool compressMessage)
        {
            var flags = WebSocketMessageFlags.None;

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

        //
    }
}