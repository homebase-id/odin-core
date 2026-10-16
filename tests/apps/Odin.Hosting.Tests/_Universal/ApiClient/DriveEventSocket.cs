using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Hosting.UnifiedV2;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Hosting.Tests._Universal.ApiClient;

/// <summary>
/// A notification socket, for an owner or an app token, that keeps each drive event's data as JSON, to read the
/// events in the order they came
/// </summary>
public sealed class DriveEventSocket
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    private readonly ConcurrentQueue<(ClientNotificationType Type, JsonElement Data)> _events = new();
    private Func<Task> _disconnect = () => Task.CompletedTask;

    /// <summary>
    /// An owner socket (V1 owner route, owner cookie)
    /// </summary>
    public static async Task<DriveEventSocket> ConnectOwnerAsync(OdinId identity, OwnerAuthTokenContext token,
        EstablishConnectionOptions options)
    {
        var socket = new DriveEventSocket();
        var listener = new TestOwnerWebSocketListener();
        listener.NotificationReceived += n =>
        {
            socket.Keep(n);
            return Task.CompletedTask;
        };
        await listener.ConnectAsync(identity, token, options);
        socket._disconnect = listener.DisconnectAsync;
        return socket;
    }

    /// <summary>
    /// An app socket (V2 route, bearer subprotocol), as an app client such as filesync connects
    /// </summary>
    public static async Task<DriveEventSocket> ConnectAppAsync(OdinId identity, ClientAuthenticationToken token,
        byte[] sharedSecret, EstablishConnectionOptions options)
    {
        var webSocket = new ClientWebSocket();
        webSocket.Options.AddSubProtocol("odin.notify.v1");
        webSocket.Options.AddSubProtocol("odin.bearer." +
                                         Convert.ToBase64String(token.ToPortableBytes()).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
        await webSocket.ConnectAsync(new Uri($"wss://{identity}:{WebScaffold.HttpsPort}{UnifiedApiRouteConstants.NotifySocket}"),
            CancellationToken.None);

        var command = new SocketCommand
        {
            Command = SocketCommandType.EstablishConnectionRequest,
            Data = OdinSystemSerializer.Serialize(options)
        };
        var request = SharedSecretEncryptedPayload.Encrypt(OdinSystemSerializer.Serialize(command).ToUtf8ByteArray(),
            sharedSecret.ToSensitiveByteArray());
        await webSocket.SendAsync(OdinSystemSerializer.Serialize(request).ToUtf8ByteArray(), WebSocketMessageType.Text, true,
            CancellationToken.None);

        var handshake = await ReceiveAsync(webSocket, sharedSecret, CancellationToken.None);
        if (handshake?.NotificationType != ClientNotificationType.DeviceHandshakeSuccess)
        {
            throw new Exception($"Did not receive a valid handshake: {handshake?.NotificationType} {handshake?.Data}");
        }

        var socket = new DriveEventSocket();
        var cts = new CancellationTokenSource();
        var receiving = Task.Run(async () =>
        {
            try
            {
                while (await ReceiveAsync(webSocket, sharedSecret, cts.Token) is { } notification)
                {
                    socket.Keep(notification);
                }
            }
            catch (Exception e) when (e is OperationCanceledException or WebSocketException)
            {
                // closed
            }
        });

        socket._disconnect = async () =>
        {
            await cts.CancelAsync();
            webSocket.Abort();
            await receiving;
            webSocket.Dispose();
        };
        return socket;
    }

    /// <summary>
    /// The next drive event, which must be of <paramref name="expected"/> type
    /// </summary>
    public async Task<JsonElement> NextAsync(ClientNotificationType expected)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        (ClientNotificationType Type, JsonElement Data) next;
        while (!_events.TryDequeue(out next))
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"no {expected} arrived within {WaitTimeout.TotalSeconds} s");
            }

            await Task.Delay(50);
        }

        Assert.That(next.Type, Is.EqualTo(expected), next.Data.ToString());
        return next.Data;
    }

    public Task DisconnectAsync() => _disconnect();

    private void Keep(TestClientNotification n)
    {
        if (n.NotificationType is ClientNotificationType.FileAdded or ClientNotificationType.FileModified
            or ClientNotificationType.FileDeleted)
        {
            _events.Enqueue((n.NotificationType, JsonDocument.Parse(n.Data).RootElement.Clone()));
        }
    }

    /// <summary>
    /// The next whole message, decrypted; null when the server closes
    /// </summary>
    private static async Task<TestClientNotification> ReceiveAsync(ClientWebSocket webSocket, byte[] sharedSecret,
        CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        var buffer = new byte[8 * 1024];
        WebSocketReceiveResult result;
        do
        {
            result = await webSocket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            message.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        var payload = OdinSystemSerializer.Deserialize<ClientNotificationPayload>(message.ToArray().ToStringFromUtf8Bytes());
        var json = payload!.IsEncrypted
            ? SharedSecretEncryptedPayload.Decrypt(payload.Payload, sharedSecret.ToSensitiveByteArray()).ToStringFromUtf8Bytes()
            : payload.Payload;
        return OdinSystemSerializer.Deserialize<TestClientNotification>(json);
    }
}
