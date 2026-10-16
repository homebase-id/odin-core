using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Tests._V2.Tests.LiveRelay;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Hosting.Tests._Universal.ApiClient;

/// <summary>
/// A notification socket, for an owner or an app token, that keeps each drive event's data as JSON, to read the
/// events in the order they came
/// </summary>
public sealed class DriveEventSocket
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    private readonly ConcurrentQueue<(ClientNotificationType Type, JsonElement Data)> _events = new();
    private readonly Func<Task> _disconnect;
    private Exception _receiveError;

    private DriveEventSocket(Func<Task> disconnect) => _disconnect = disconnect;

    /// <summary>
    /// An owner socket (V1 owner route, owner cookie)
    /// </summary>
    public static async Task<DriveEventSocket> ConnectOwnerAsync(OdinId identity, OwnerAuthTokenContext token,
        EstablishConnectionOptions options)
    {
        var listener = new TestOwnerWebSocketListener();
        var socket = new DriveEventSocket(listener.DisconnectAsync);
        listener.NotificationReceived += n =>
        {
            socket.Keep(n);
            return Task.CompletedTask;
        };
        await listener.ConnectAsync(identity, token, options);
        return socket;
    }

    /// <summary>
    /// An app socket (V2 route, bearer subprotocol), as an app client such as filesync connects
    /// </summary>
    public static async Task<DriveEventSocket> ConnectAppAsync(OdinId identity, ClientAuthenticationToken token,
        byte[] sharedSecret, EstablishConnectionOptions options)
    {
        var webSocket = await LiveRelayTestHelpers.ConnectAppSocketAsync(identity, token, CancellationToken.None);
        try
        {
            var handshake = await LiveRelayTestHelpers.DoHandshakeAsync(webSocket, sharedSecret, options, CancellationToken.None);
            if (handshake?.NotificationType != ClientNotificationType.DeviceHandshakeSuccess)
            {
                throw new Exception($"Did not receive a valid handshake: {handshake?.NotificationType} {handshake?.Data}");
            }
        }
        catch
        {
            webSocket.Dispose();
            throw;
        }

        var cts = new CancellationTokenSource();
        Task receiving = null;
        var socket = new DriveEventSocket(async () =>
        {
            await cts.CancelAsync();
            webSocket.Abort();
            await receiving!;
            webSocket.Dispose();
        });
        receiving = Task.Run(async () =>
        {
            try
            {
                while (await LiveRelayTestHelpers.ReadFrameAsync(webSocket, cts.Token) is { } frame)
                {
                    socket.Keep(LiveRelayTestHelpers.DecryptClientNotification<TestClientNotification>(frame, sharedSecret));
                }
            }
            catch (Exception e) when (e is OperationCanceledException or WebSocketException)
            {
                // closed
            }
            catch (Exception e)
            {
                // Reported by the next NextAsync, rather than as a timeout
                socket._receiveError = e;
            }
        });

        return socket;
    }

    /// <summary>
    /// The next drive event, which must be of <paramref name="expected"/> type
    /// </summary>
    public async Task<JsonElement> NextAsync(ClientNotificationType expected)
    {
        var (type, data) = await NextAsync();
        Assert.That(type, Is.EqualTo(expected), data.ToString());
        return data;
    }

    /// <summary>
    /// The next drive event, of whatever type
    /// </summary>
    public async Task<(ClientNotificationType Type, JsonElement Data)> NextAsync()
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        (ClientNotificationType Type, JsonElement Data) next;
        while (!_events.TryDequeue(out next))
        {
            if (_receiveError != null)
            {
                Assert.Fail($"receiving stopped: {_receiveError}");
            }

            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"no drive event arrived within {WaitTimeout.TotalSeconds} s");
            }

            await Task.Delay(50);
        }

        return next;
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
}
