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
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Services.Peer.Incoming.Drive.Transfer;

#nullable enable

namespace Odin.Services.AppNotifications.WebSocket
{
    public class AppNotificationHandler(
        PeerInboxProcessor peerInboxProcessor,
        IDriveManager driveManager,
        ILogger<AppNotificationHandler> logger,
        SharedDeviceSocketCollection<AppNotificationHandler> deviceSocketCollection)
        :
            INotificationHandler<IClientNotification>,
            INotificationHandler<IDriveNotification>,
            INotificationHandler<InboxItemReceivedNotification>
    {

        //

        /// <summary>
        /// Awaits the configuration when establishing a new web socket connection
        /// </summary>
        public async Task EstablishConnection(System.Net.WebSockets.WebSocket webSocket, CancellationToken cancellationToken,
            IOdinContext odinContext)
        {
            var webSocketKey = Guid.NewGuid();
            try
            {
                var deviceSocket = new DeviceSocket
                {
                    Key = webSocketKey,
                    Socket = webSocket,
                };
                deviceSocketCollection.AddSocket(deviceSocket);
                await AwaitCommands(deviceSocket, cancellationToken, odinContext);
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
                logger.LogError(e, "WebSocket: {error}", e.Message);
            }
            finally
            {
                deviceSocketCollection.RemoveSocket(webSocketKey);
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

                logger.LogTrace("WebSocket closed");
            }
        }

        //

        private async Task AwaitCommands(DeviceSocket deviceSocket, CancellationToken cancellationToken, IOdinContext odinContext)
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
                            await ProcessCommand(deviceSocket, command, cancellationToken, odinContext);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        catch (Exception e)
                        {
                            logger.LogError(e, "Unhandled exception while processing command: {command}", command.Command);
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
                !(notification.NotificationType is ClientNotificationType.ConnectionRequestAccepted
                    or ClientNotificationType.ConnectionRequestReceived);

            var json = OdinSystemSerializer.Serialize(new
            {
                notification.NotificationType,
                Data = notification.GetClientData()
            });

            var sockets = deviceSocketCollection.GetAll().Values;
            foreach (var deviceSocket in sockets)
            {
                await SendMessageAsync(deviceSocket, json, cancellationToken, shouldEncrypt);
            }
        }

        //

        public async Task Handle(IDriveNotification notification, CancellationToken cancellationToken)
        {
            var sockets = deviceSocketCollection.GetAll().Values
                .Where(ds => ds.Drives.Any(driveId => driveId == notification.File.DriveId));

            foreach (var deviceSocket in sockets)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    var deviceOdinContext = deviceSocket.DeviceOdinContext;
                    var hasSharedSecret = null != deviceOdinContext?.PermissionsContext?.SharedSecretKey;

                    var o = new ClientDriveNotification
                    {
                        TargetDrive = (await driveManager.GetDriveAsync(notification.File.DriveId)).TargetDriveInfo,
                        Header = hasSharedSecret
                            ? DriveFileUtility.CreateClientFileHeader(notification.ServerFileHeader, deviceOdinContext)
                            : null,
                        PreviousServerFileHeader = hasSharedSecret
                            ? DriveFileUtility.AddIfDeletedNotification(notification, deviceOdinContext!)
                            : null
                    };

                    var json = OdinSystemSerializer.Serialize(new
                    {
                        notification.NotificationType,
                        Data = OdinSystemSerializer.Serialize(o)
                    });

                    await SendMessageAsync(deviceSocket, json, cancellationToken, encrypt: true, groupId: notification.File.FileId);
                }
            }
        }
        //

        public async Task Handle(InboxItemReceivedNotification notification, CancellationToken cancellationToken)
        {
            var notificationDriveId = notification.TargetDrive.Alias;
            var translated = new TranslatedClientNotification(notification.NotificationType,
                OdinSystemSerializer.Serialize(new
                {
                    TargetDrive = notification.TargetDrive,
                    notification.TransferFileType,
                    notification.FileSystemType
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
                notification.NotificationType,
                Data = notification.GetClientData()
            });

            var sockets = deviceSocketCollection.GetAll().Values
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
                deviceSocket.DeviceOdinContext?.PermissionsContext?.SharedSecretKey != null);
        }

        //

        private async Task SendMessageAsync(DeviceSocket deviceSocket, string message, CancellationToken cancellationToken,
            bool encrypt = true, Guid? groupId = null)
        {
            var socket = deviceSocket.Socket;

            if (socket is not { State: WebSocketState.Open } || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (deviceSocket.DeviceOdinContext == null)
            {
                deviceSocketCollection.RemoveSocket(deviceSocket.Key);
                logger.LogInformation("Invalid/Stale Device found; removing from list");
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
                logger.LogInformation("WebSocketException: {error}", e.Message);
            }
            catch (Exception e)
            {
                //HACK: need to find out what is trying to write when the response is complete
                logger.LogError(e, "SendMessageAsync: {error}", e.Message);
            }
        }

        //

        private async Task ProcessCommand(DeviceSocket deviceSocket, SocketCommand command, CancellationToken cancellationToken,
            IOdinContext odinContext)
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
                        var error = $"[Command:{command.Command}] {e.Message}";
                        await SendErrorMessageAsync(deviceSocket, error, cancellationToken);
                        throw new CloseWebSocketException();
                    }

                    var response = new EstablishConnectionResponse();
                    await SendMessageAsync(deviceSocket, OdinSystemSerializer.Serialize(response), cancellationToken);
                    break;

                case SocketCommandType.ProcessTransitInstructions:
                {
                    var d = OdinSystemSerializer.Deserialize<ExternalFileIdentifier>(command.Data);
                    if (d != null)
                    {
                        await peerInboxProcessor.ProcessInboxAsync(d.TargetDrive, odinContext);
                    }
                }
                    break;

                case SocketCommandType.ProcessInbox:
                {
                    var request = OdinSystemSerializer.Deserialize<ProcessInboxRequest>(command.Data);
                    if (request != null)
                    {
                        await peerInboxProcessor.ProcessInboxAsync(request.TargetDrive, odinContext, request.BatchSize);
                    }
                }
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
    }
}