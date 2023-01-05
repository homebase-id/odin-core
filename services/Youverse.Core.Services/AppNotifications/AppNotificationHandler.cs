using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Youverse.Core.Serialization;
using Youverse.Core.Services.AppNotifications.ClientNotifications;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;

namespace Youverse.Core.Services.AppNotifications
{
    public class AppNotificationHandler : INotificationHandler<IClientNotification>, INotificationHandler<IDriveClientNotification>
    {
        private readonly DeviceSocketCollection _deviceSocketCollection;
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IAppService _appService;
        private readonly IDriveService _driveService;

        public AppNotificationHandler(DotYouContextAccessor contextAccessor, IAppService appService, IDriveService driveService)
        {
            _contextAccessor = contextAccessor;
            _appService = appService;
            _driveService = driveService;
            _deviceSocketCollection = new DeviceSocketCollection();
        }

        public async Task Connect(WebSocket socket, EstablishConnectionRequest request)
        {
            var dotYouContext = _contextAccessor.GetCurrent();

            var deviceSocket = new DeviceSocket()
            {
                Key = Guid.NewGuid(),
                DeviceAuthToken = null, //TODO: where is the best place to get the cookie?
                Socket = socket,
            };

            _deviceSocketCollection.AddSocket(deviceSocket);

            var response = new EstablishConnectionResponse() { };
            await SendMessageAsync(deviceSocket, DotYouSystemSerializer.Serialize(response));
            await ListenForDisconnect(deviceSocket);
        }

        private async Task ListenForDisconnect(DeviceSocket deviceSocket)
        {
            var buffer = new byte[1024 * 4];
            var receiveResult = await deviceSocket.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (receiveResult.MessageType == WebSocketMessageType.Close)
            {
                await _deviceSocketCollection.RemoveSocket(deviceSocket.Key);
                //TODO: need to send the right response but not quite sure what that is.
                // await socket.CloseAsync(receiveResult.CloseStatus.Value, "", CancellationToken.None);
            }
        }

        public async Task Handle(IClientNotification notification, CancellationToken cancellationToken)
        {
            await this.SerializeSendToAllDevices(notification);
        }

        public Task Handle(IDriveClientNotification notification, CancellationToken cancellationToken)
        {
            //lookup the file from the _appService
            var data = DotYouSystemSerializer.Serialize(new
            {
                TargetDrive = _driveService.GetDrive(notification.File.DriveId).GetAwaiter().GetResult().TargetDriveInfo,
                Header = _appService.GetClientEncryptedFileHeader(notification.File).GetAwaiter().GetResult()
            });

            SerializeSendToAllDevices(new TranslatedClientNotification(notification.NotificationType, data)).GetAwaiter().GetResult();
            return Task.CompletedTask;
        }

        private async Task SerializeSendToAllDevices(IClientNotification notification)
        {
            //TODO:  Need to map the notification correctly so we know the type of notification AND only get the data we expect 
            var json = DotYouSystemSerializer.Serialize(new
            {
                NotificationType = notification.NotificationType,
                Data = notification.GetClientData()
            });

            var sockets = this._deviceSocketCollection.GetAll().Values;
            foreach (var deviceSocket in sockets)
            {
                await this.SendMessageAsync(deviceSocket, json);
            }
        }

        private async Task SendMessageAsync(DeviceSocket deviceSocket, string message)
        {
            var socket = deviceSocket.Socket;

            if (socket.State != WebSocketState.Open)
            {
                return;
            }

            try
            {
                await socket.SendAsync(
                    buffer: new ArraySegment<byte>(message.ToUtf8ByteArray(), 0, message.Length),
                    messageType: WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken: CancellationToken.None);
            }
            catch (Exception e)
            {
                //HACK: need to find out what is trying to write when the response is complete
                Console.WriteLine(e);
            }
        }
    }
}