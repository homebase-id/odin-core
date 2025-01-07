using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Hosting.Tests._Universal.ApiClient;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Hosting.Tests._Universal.Peer.PeerAppNotificationsWebSocket;

public sealed class PeerTestAppWebSocketListener
{
    public event Func<TestClientNotification, Task> NotificationReceived;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly ClientWebSocket _clientWebSocket = new();
    private Task _receivingTask;
    private ClientAccessToken _token;

    private async Task OnNotificationReceived(TestClientNotification message)
    {
        if (this.NotificationReceived != null)
        {
            await NotificationReceived.Invoke(message);
        }
    }

    public async Task ConnectAsync(OdinId identity, ClientAccessToken token, EstablishConnectionOptions options)
    {
        _token = token;
        _clientWebSocket.Options.Cookies = new CookieContainer();

        var cookie = new Cookie(YouAuthConstants.SubscriberCookieName, token.ToAuthenticationToken().ToString())
        {
            Domain = identity
        };

        _clientWebSocket.Options.Cookies.Add(cookie);
        CancellationTokenSource tokenSource = new CancellationTokenSource();

        //
        // Connect to the socket
        //
        var uri = new Uri($"wss://{identity}:{WebScaffold.HttpsPort}{GuestApiPathConstants.PeerNotificationsV1}/ws");
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
            _token.SharedSecret);
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

        var response = DecryptClientNotificationPayload<EstablishConnectionResponse>(array);
        if (response.NotificationType != ClientNotificationType.DeviceHandshakeSuccess)
        {
            throw new Exception("Did not receive a valid handshake");
        }

        this.StartReceiving();
    }

    public async Task DisconnectAsync()
    {
        try
        {
            await _cancellationTokenSource.CancelAsync();
            await _receivingTask;
            _clientWebSocket.Dispose();
        }
        catch (TaskCanceledException)
        {
            //expected
        }
    }

    private void StartReceiving()
    {
        _receivingTask = Task.Run(async () =>
        {
            try
            {
                while (_clientWebSocket.State == WebSocketState.Open)
                {
                    var receiveBuffer = new ArraySegment<byte>(new byte[1024 * 4]);
                    var receiveResult = await _clientWebSocket.ReceiveAsync(receiveBuffer, _cancellationTokenSource.Token);

                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        await _clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                    }
                    else
                    {
                        var array = receiveBuffer.Array;
                        Array.Resize(ref array, receiveResult.Count);
                        await OnNotificationReceived(DecryptClientNotificationPayload<TestClientNotification>(array));
                    }
                }
            }
            catch (WebSocketException e)
            {
                if (e.WebSocketErrorCode != WebSocketError.ConnectionClosedPrematurely) //server killed the connection
                {
                    throw;
                }
            }
        });
    }

    private T DecryptClientNotificationPayload<T>(byte[] array)
    {
        var json = array.ToStringFromUtf8Bytes();
        var n = OdinSystemSerializer.Deserialize<ClientNotificationPayload>(json);
        if (n.IsEncrypted)
        {
            var decryptedResponse = SharedSecretEncryptedPayload.Decrypt(n.Payload, _token.SharedSecret);
            var response = OdinSystemSerializer.Deserialize<T>(decryptedResponse);
            return response;
        }

        return OdinSystemSerializer.Deserialize<T>(n.Payload);
    }
}