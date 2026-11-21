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
using Odin.Hosting.Tests._V2.Tests.LiveRelay;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Hosting.Tests._Universal.Peer.PeerAppNotificationsWebSocket;

/// <summary>What the server did with a refused handshake: the frame it answered with (null if none), then its close</summary>
public sealed record RefusedHandshake(TestClientNotification Answer, WebSocketCloseStatus? CloseStatus, string CloseReason);

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

    public async Task ConnectAsync(OdinId identity, ClientAccessToken token, EstablishConnectionOptions options,
        string pathOverride = null)
    {
        var response = await HandshakeAsync(identity, token, options, pathOverride);
        if (response?.NotificationType != ClientNotificationType.DeviceHandshakeSuccess)
        {
            throw new Exception($"Did not receive a valid handshake: {response?.NotificationType} {response?.Data}");
        }

        this.StartReceiving();
    }

    /// <summary>
    /// Connects with a token the server should refuse: the frame it answered with (null if none came before the close),
    /// and the close it then sent
    /// </summary>
    public async Task<RefusedHandshake> ConnectRefusedAsync(
        OdinId identity, ClientAccessToken token, EstablishConnectionOptions options, string pathOverride = null)
    {
        var answer = await HandshakeAsync(identity, token, options, pathOverride);
        if (_clientWebSocket.CloseStatus == null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                if (await LiveRelayTestHelpers.ReadFrameAsync(_clientWebSocket, timeout.Token) != null)
                {
                    throw new Exception($"Expected the server to close the socket after {Describe(answer)}, got another frame");
                }
            }
            catch (Exception e) when (e is OperationCanceledException or WebSocketException)
            {
                throw new Exception($"No close from the server after {Describe(answer)} ({e.GetType().Name}: {e.Message})", e);
            }
        }

        return new RefusedHandshake(answer, _clientWebSocket.CloseStatus, _clientWebSocket.CloseStatusDescription);
    }

    private static string Describe(TestClientNotification answer) =>
        answer == null ? "no frame" : $"{answer.NotificationType} '{answer.Data}'";

    // Sends the authentication package; the first frame back, or null when the server closed the socket instead
    private async Task<TestClientNotification> HandshakeAsync(OdinId identity, ClientAccessToken token,
        EstablishConnectionOptions options, string pathOverride)
    {
        _token = token;
        // _clientWebSocket.Options.Cookies = new CookieContainer();
        // var cookie = new Cookie(YouAuthConstants.SubscriberCookieName, token.ToAuthenticationToken().ToString())
        // {
        //     Domain = identity
        // };
        //
        // _clientWebSocket.Options.Cookies.Add(cookie);
        CancellationTokenSource tokenSource = new CancellationTokenSource();

        //
        // Connect to the socket
        //
        var path = pathOverride ?? (GuestApiPathConstantsV1.PeerNotificationsV1 + "/ws");
        var uri = new Uri($"wss://{identity}:{WebScaffold.HttpsPort}{path}");
        await _clientWebSocket.ConnectAsync(uri, tokenSource.Token);

        var request = new SocketCommand
        {
            Command = SocketCommandType.EstablishConnectionRequest,
            Data = OdinSystemSerializer.Serialize(options)
        };

        var ssp = SharedSecretEncryptedPayload.Encrypt(
            OdinSystemSerializer.Serialize(request).ToUtf8ByteArray(),
            _token.SharedSecret);
        //
        // Send a request indicating the drives (handshake1)
        //
        var authPackage = new SocketAuthenticationPackage
        {
            ClientAuthToken64 = token.ToAuthenticationToken().ToPortableBytes64(),
            SharedSecretEncryptedOptions = ssp
        };
        
        var sendBuffer = OdinSystemSerializer.Serialize(authPackage).ToUtf8ByteArray();
        await _clientWebSocket.SendAsync(new ArraySegment<byte>(sendBuffer), WebSocketMessageType.Text, true, tokenSource.Token);

        //
        // Wait for the reply 
        //
        var frame = await LiveRelayTestHelpers.ReadFrameAsync(_clientWebSocket, tokenSource.Token);

        // Read as a generic notification: EstablishConnectionResponse.NotificationType is fixed to
        // DeviceHandshakeSuccess, so checking it would pass any first frame, an error included
        return frame == null ? null : DecryptClientNotificationPayload<TestClientNotification>(frame);
    }

    public async Task DisconnectAsync()
    {
        try
        {
            await _cancellationTokenSource.CancelAsync();
            if (null != _receivingTask)
            {
                await _receivingTask;
            }
            
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