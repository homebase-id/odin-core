using System;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Hosting.Tests._Universal.Outbox.Performance;

public class TestClientNotification
{
    public ClientNotificationType NotificationType { get; set; }
    public string Data { get; set; }
}

public sealed class TestWebSocketListener
{
    public event Func<TestClientNotification, Task> NotificationReceived;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly ClientWebSocket _clientWebSocket = new();
    private Task _receivingTask;

    private async Task OnNotificationReceived(TestClientNotification message)
    {
        if (this.NotificationReceived != null)
        {
            await NotificationReceived.Invoke(message);
        }
    }

    public async Task ConnectAsync(OdinId identity, OwnerAuthTokenContext tokenContext, EstablishConnectionOptions options)
    {
        ClientAuthenticationToken clientAuthToken = tokenContext.AuthenticationResult;
        byte[] sharedSecret = tokenContext.SharedSecret.GetKey();

        _clientWebSocket.Options.Cookies = new CookieContainer();

        var cookie = new Cookie(YouAuthConstants.AppCookieName, clientAuthToken.ToString())
        {
            Domain = identity
        };
        _clientWebSocket.Options.Cookies.Add(cookie);
        CancellationTokenSource tokenSource = new CancellationTokenSource();

        //
        // Connect to the socket
        //
        var uri = new Uri($"wss://{identity}:{WebScaffold.HttpsPort}{AppApiPathConstants.NotificationsV1}/ws");
        await _clientWebSocket.ConnectAsync(uri, tokenSource.Token);

        //
        // Send a request indicating the drives (handshake1)
        //
        var request = new SocketCommand
        {
            Command = SocketCommandType.EstablishConnectionRequest,
            Data = OdinSystemSerializer.Serialize(options)
        };

        var ssp = SharedSecretEncryptedPayload.Encrypt(
            OdinSystemSerializer.Serialize(request).ToUtf8ByteArray(),
            sharedSecret.ToSensitiveByteArray());
        var sendBuffer = OdinSystemSerializer.Serialize(ssp).ToUtf8ByteArray();
        await _clientWebSocket.SendAsync(new ArraySegment<byte>(sendBuffer), WebSocketMessageType.Text, true, tokenSource.Token);

        //
        // Wait for the reply 
        //
        var receiveBuffer = new ArraySegment<byte>(new byte[1024 * 3]);
        var receiveResult = await _clientWebSocket.ReceiveAsync(receiveBuffer, tokenSource.Token);
        if (receiveResult.MessageType != WebSocketMessageType.Text)
        {
            throw new Exception("Did not receive a valid handshake");
        }

        var array = receiveBuffer.Array;
        Array.Resize(ref array, receiveResult.Count);

        var json = array.ToStringFromUtf8Bytes();
        var n = OdinSystemSerializer.Deserialize<ClientNotificationPayload>(json);
        var decryptedResponse = SharedSecretEncryptedPayload.Decrypt(n.Payload, sharedSecret.ToSensitiveByteArray());
        var response = OdinSystemSerializer.Deserialize<EstablishConnectionResponse>(decryptedResponse);

        if (response.NotificationType != ClientNotificationType.DeviceHandshakeSuccess)
        {
            throw new Exception("Did not receive a valid handshake");
        }

        this.StartReceiving();
    }

    public async Task DisconnectAsync()
    {
        await _cancellationTokenSource.CancelAsync();
        await _receivingTask;
        _clientWebSocket.Dispose();
    }

    private void StartReceiving()
    {
        _receivingTask = Task.Run(async () =>
        {
            var buffer = new byte[1024 * 4];

            while (_clientWebSocket.State == WebSocketState.Open)
            {
                var receiveBuffer = new ArraySegment<byte>(new byte[1024 * 3]);
                var receiveResult = await _clientWebSocket.ReceiveAsync(receiveBuffer, _cancellationTokenSource.Token);

                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    await _clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                }
                else
                {
                    var array = receiveBuffer.Array;
                    Array.Resize(ref array, receiveResult.Count);

                    var message = Encoding.UTF8.GetString(buffer, 0, receiveResult.Count);
                    var notification = OdinSystemSerializer.Deserialize<TestClientNotification>(message);

                    await OnNotificationReceived(notification);
                }
            }
        });
    }
}