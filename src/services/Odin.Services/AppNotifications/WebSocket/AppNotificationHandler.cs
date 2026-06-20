using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.LiveRelay;
using Odin.Services.Mediator;
using Odin.Services.Peer;
using Odin.Services.Peer.Incoming.Drive.Transfer;

#nullable enable

namespace Odin.Services.AppNotifications.WebSocket
{
    /// <summary>
    /// Scoped MediatR handler and per-connection WebSocket command processor.
    ///
    /// The shared subscription and the fan-out of notifications to all connected sockets live
    /// in the per-tenant singleton <see cref="AppNotificationDispatcher"/>; this class delegates
    /// to it. Keeping this class scoped lets it use request/DB-bound services (e.g.
    /// <see cref="PeerInboxProcessor"/>) for the per-connection command loop without making them
    /// captive dependencies of a singleton.
    /// </summary>
    public class AppNotificationHandler :
        INotificationHandler<IClientNotification>,
        INotificationHandler<IDriveNotification>,
        INotificationHandler<InboxItemReceivedNotification>
    {
        private readonly ILogger<AppNotificationHandler> _logger;
        private readonly AppNotificationDispatcher _dispatcher;
        private readonly PeerInboxProcessor _peerInboxProcessor;
        private readonly LiveRelayRetainedStore _liveRelayRetainedStore;

        public AppNotificationHandler(
            ILogger<AppNotificationHandler> logger,
            AppNotificationDispatcher dispatcher,
            PeerInboxProcessor peerInboxProcessor,
            LiveRelayRetainedStore liveRelayRetainedStore)
        {
            _logger = logger;
            _dispatcher = dispatcher;
            _peerInboxProcessor = peerInboxProcessor;
            _liveRelayRetainedStore = liveRelayRetainedStore;
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
                // ignore, this exception is expected when the client doesn't play by the rules
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
            if (notification.IgnoreWebSocketNotification)
            {
                return Task.CompletedTask;
            }

            return _dispatcher.PublishDriveNotificationAsync(notification);
        }

        //
        // InboxItemReceivedNotification
        //

        // Src: MediatR
        // Dst: PubSub queue (via the per-tenant dispatcher)
        public Task Handle(InboxItemReceivedNotification notification, CancellationToken cancellationToken = default)
        {
            return _dispatcher.PublishInboxItemReceivedAsync(notification);
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
            CancellationToken cancellationToken = default)
        {
            return _dispatcher.SendMessageAsync(deviceSocket, message, encrypt, cancellationToken);
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
                        // Capture the app this socket is authenticated as, so app-scoped
                        // notifications (e.g. live relay) are delivered only to its sockets.
                        deviceSocket.AppId = odinContext.Caller.OdinClientContext?.AppId?.Value;
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

                    // Hydrate the freshly-connected socket with the last live-relay data point from
                    // every sender for this app, so a (re)connecting/foregrounding client immediately
                    // sees current state without asking for anything.
                    await FlushRetainedLiveRelayAsync(deviceSocket, cancellationToken);
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

                default:
                    await SendErrorMessageAsync(deviceSocket, "Invalid command", cancellationToken);
                    break;
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

        private async Task FlushRetainedLiveRelayAsync(DeviceSocket deviceSocket, CancellationToken cancellationToken)
        {
            var appId = deviceSocket.AppId;
            if (!appId.HasValue)
            {
                return;
            }

            try
            {
                var entries = await _liveRelayRetainedStore.GetAllForAppAsync(appId.Value, cancellationToken);
                foreach (var entry in entries)
                {
                    var notification = new LiveRelayNotification
                    {
                        SenderOdinId = new OdinId(entry.SenderDomain),
                        ChannelKey = entry.ChannelKey,
                        Blob = entry.Blob,
                        ReceivedAt = new UnixTimeUtc(entry.ReceivedAtMs),
                        TargetAppId = appId.Value
                    };

                    await _dispatcher.SendClientNotificationToSocketAsync(deviceSocket, notification, cancellationToken);
                }
            }
            catch (Exception e)
            {
                // Hydration is best-effort; never let it break the socket handshake.
                _logger.LogInformation(e, "Live relay flush-on-connect failed: {error}", e.Message);
            }
        }
    }
}
