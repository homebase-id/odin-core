using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Mediator;
using Odin.Services.Peer.AppNotification;
using Odin.Services.Util;

#nullable enable

namespace Odin.Services.AppNotifications.WebSocket
{
    /// <summary>
    /// Scoped MediatR handler and per-connection WebSocket command processor for the peer-app
    /// notification pipeline. The shared subscription and notification fan-out live in the
    /// per-tenant singleton <see cref="PeerAppNotificationDispatcher"/>; this class delegates to
    /// it. Kept scoped so it can use the request/DB-bound <see cref="PeerAppNotificationService"/>
    /// for connection authentication without making it a captive dependency of a singleton.
    /// </summary>
    public class PeerAppNotificationHandler :
        INotificationHandler<IClientNotification>,
        INotificationHandler<IDriveNotification>
    {
        private readonly ILogger<PeerAppNotificationHandler> _logger;
        private readonly PeerAppNotificationDispatcher _dispatcher;
        private readonly PeerAppNotificationService _peerAppNotificationService;

        public PeerAppNotificationHandler(
            ILogger<PeerAppNotificationHandler> logger,
            PeerAppNotificationDispatcher dispatcher,
            PeerAppNotificationService peerAppNotificationService)
        {
            _logger = logger;
            _dispatcher = dispatcher;
            _peerAppNotificationService = peerAppNotificationService;
        }

        //

        /// <summary>
        /// Awaits the configuration when establishing a new web socket connection
        /// </summary>
        public async Task EstablishConnection(
            System.Net.WebSockets.WebSocket webSocket,
            IOdinContext odinContext,
            CancellationToken cancellationToken = default)
        {
            var webSocketKey = Guid.NewGuid();
            try
            {
                var deviceSocket = new DeviceSocket
                {
                    Key = webSocketKey,
                    Socket = webSocket,
                };
                _dispatcher.AddSocket(deviceSocket);

                await _dispatcher.SubscribeAsync(cancellationToken);
                await AwaitCommands(deviceSocket, odinContext, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // ignore, this exception is expected when the socket is closing behind the scenes
            }
            catch (WebSocketException e) when (e.Message ==
                                               "The remote party closed the WebSocket connection without completing the close handshake.")
            {
                // ignore, this exception is expected when the client doesn't play by the rules; yea!  the rulez
            }
            catch (Exception e)
            {
                _logger.LogError(e, "WebSocket: {error}", e.Message);
            }
            finally
            {
                await _dispatcher.UnsubscribeAsync(CancellationToken.None);
                await _dispatcher.RemoveSocket(webSocketKey);
                _logger.LogTrace("WebSocket closed");
            }
        }

        //

        private async Task AwaitCommands(
            DeviceSocket deviceSocket,
            IOdinContext currentOdinContext,
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
                    byte[] decryptedBytes;

                    try
                    {
                        if (deviceSocket.DeviceOdinContext == null)
                        {
                            var authenticationPackage = OdinSystemSerializer.Deserialize<SocketAuthenticationPackage>(completeMessage);

                            OdinValidationUtils.AssertNotNull(authenticationPackage, "authenticationPackage");
                            OdinValidationUtils.AssertNotNull(authenticationPackage!.ClientAuthToken64,
                                nameof(authenticationPackage.ClientAuthToken64));
                            OdinValidationUtils.AssertNotNull(authenticationPackage.SharedSecretEncryptedOptions,
                                nameof(authenticationPackage.SharedSecretEncryptedOptions));

                            var clientAuthToken64 = authenticationPackage.ClientAuthToken64;
                            deviceSocket.DeviceOdinContext = await HandleAuthentication(clientAuthToken64, currentOdinContext);
                            decryptedBytes =
                                authenticationPackage.SharedSecretEncryptedOptions!.Decrypt(deviceSocket.DeviceOdinContext
                                    .PermissionsContext.SharedSecretKey);
                        }
                        else
                        {
                            var sharedSecret = deviceSocket.DeviceOdinContext.PermissionsContext!.SharedSecretKey;
                            decryptedBytes = SharedSecretEncryptedPayload.Decrypt(completeMessage, sharedSecret);
                        }
                    }
                    catch (OdinSecurityException)
                    {
                        await SendMessageAsync(deviceSocket, OdinSystemSerializer.Serialize(new
                            {
                                NotificationType = ClientNotificationType.AuthenticationError,
                                Data = "Invalid Token",
                            }),
                            deviceSocket.DeviceOdinContext?.PermissionsContext?.SharedSecretKey != null,
                            sendEvenIfNoDeviceOdinContext: true,
                            cancellationToken);
                        continue;
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
                            await ProcessCommand(deviceSocket, command, cancellationToken);
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
        // IClientNotification
        //

        // Src: MediatR
        // Dst: PubSub queue (via the per-tenant dispatcher)
        public Task Handle(IClientNotification notification, CancellationToken cancellationToken = default)
        {
            return _dispatcher.PublishClientNotificationAsync(notification);
        }

        //
        // IDriveNotification
        //

        // Src: MediatR
        // Dst: PubSub queue (via the per-tenant dispatcher)
        public Task Handle(IDriveNotification notification, CancellationToken cancellationToken = default)
        {
            return _dispatcher.PublishDriveNotificationAsync(notification);
        }

        //

        private Task SendErrorMessageAsync(DeviceSocket deviceSocket, string errorText, CancellationToken cancellationToken = default)
        {
            return _dispatcher.SendErrorMessageAsync(deviceSocket, errorText, cancellationToken);
        }

        //

        private Task SendMessageAsync(
            DeviceSocket deviceSocket,
            string message,
            bool encrypt,
            bool sendEvenIfNoDeviceOdinContext = false,
            CancellationToken cancellationToken = default)
        {
            return _dispatcher.SendMessageAsync(deviceSocket, message, encrypt, sendEvenIfNoDeviceOdinContext, cancellationToken);
        }

        //

        private async Task ProcessCommand(DeviceSocket deviceSocket, SocketCommand command, CancellationToken cancellationToken = default)
        {
            if (null == deviceSocket.DeviceOdinContext)
            {
                throw new OdinSystemException("DeviceOdinContext is null");
            }

            var odinContext = deviceSocket.DeviceOdinContext;

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

                        deviceSocket.Drives = drives;
                    }
                    catch (OdinSecurityException e)
                    {
                        var error = $"[Command:{command.Command}] {e.Message}";
                        await SendErrorMessageAsync(deviceSocket, error, cancellationToken);
                        throw new CloseWebSocketException();
                    }

                    var response = new EstablishConnectionResponse();
                    await SendMessageAsync(
                        deviceSocket,
                        OdinSystemSerializer.Serialize(response),
                        encrypt: true,
                        sendEvenIfNoDeviceOdinContext: false,
                        cancellationToken);
                    break;

                case SocketCommandType.Ping:
                    await SendMessageAsync(
                        deviceSocket,
                        OdinSystemSerializer.Serialize(new { NotificationType = ClientNotificationType.Pong }),
                        encrypt: true,
                        sendEvenIfNoDeviceOdinContext: false,
                        cancellationToken);
                    break;

                default:
                    await SendErrorMessageAsync(deviceSocket, "Invalid command", cancellationToken);
                    break;
            }
        }

        private async Task<IOdinContext> HandleAuthentication(string clientAuthToken64, IOdinContext currentOdinContext)
        {
            if (ClientAuthenticationToken.TryParse(clientAuthToken64, out var clientAuthToken))
            {
                if (clientAuthToken.ClientTokenType != ClientTokenType.PeerNotificationSubscriber)
                {
                    throw new OdinSecurityException("Invalid Client Token Type");
                }

                // authToken comes from ICR, not the app registration
                // because it's a caller wanting to get peer app notifications
                var ctx = await _peerAppNotificationService.GetDotYouContext(clientAuthToken, currentOdinContext);
                if (null == ctx)
                {
                    throw new OdinSecurityException("Invalid Client Token");
                }

                ctx.SetAuthContext("websocket-peer-app-subscriber-token");
                return ctx;
            }

            throw new OdinSecurityException("No token provided");
        }
    }
}
