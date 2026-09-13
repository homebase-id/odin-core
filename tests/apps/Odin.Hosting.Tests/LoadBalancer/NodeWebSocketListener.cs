#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Hosting.Tests.LoadBalancer;

/// <summary>
/// Owner notification socket client that can target a specific node's port, and records what it
/// receives so a test can wait for a notification instead of sleeping. A copy of
/// <see cref="_Universal.ApiClient.TestOwnerWebSocketListener"/>, which hardcodes the scaffold port.
/// </summary>
public sealed class NodeWebSocketListener : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly ClientWebSocket _socket = new();
    private readonly ConcurrentQueue<(ClientNotificationType type, string data)> _received = new();
    private readonly SemaphoreSlim _signal = new(0);
    private SensitiveByteArray _sharedSecret = null!;
    private Task? _receiving;

    public async Task ConnectAsync(OdinId identity, int port, ClientAuthenticationToken token, SensitiveByteArray sharedSecret,
        EstablishConnectionOptions options)
    {
        _sharedSecret = sharedSecret;
        _socket.Options.Cookies = new CookieContainer();
        _socket.Options.Cookies.Add(new Cookie(OwnerAuthConstants.CookieName, token.ToString()) { Domain = identity });
        _socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;

        var uri = new Uri($"wss://{identity}:{port}{OwnerApiPathConstants.NotificationsV1}/ws");
        await _socket.ConnectAsync(uri, _cts.Token);

        var request = new SocketCommand
        {
            Command = SocketCommandType.EstablishConnectionRequest,
            Data = OdinSystemSerializer.Serialize(options),
        };
        var encrypted = SharedSecretEncryptedPayload.Encrypt(
            OdinSystemSerializer.Serialize(request).ToUtf8ByteArray(), _sharedSecret);
        await _socket.SendAsync(OdinSystemSerializer.Serialize(encrypted).ToUtf8ByteArray(),
            WebSocketMessageType.Text, true, _cts.Token);

        var buffer = new ArraySegment<byte>(new byte[1024 * 4]);
        var result = await _socket.ReceiveAsync(buffer, _cts.Token);
        var bytes = buffer.Array!;
        Array.Resize(ref bytes, result.Count);
        var handshake = Decrypt<EstablishConnectionResponse>(bytes);
        if (handshake.NotificationType != ClientNotificationType.DeviceHandshakeSuccess)
        {
            throw new Exception($"handshake failed on port {port}: {handshake.NotificationType}");
        }

        StartReceiving();
    }

    /// <summary>Waits for a notification of the given type, or returns null if none arrives in time.</summary>
    public async Task<string?> WaitForAsync(ClientNotificationType type, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var (t, data) in _received)
            {
                if (t == type)
                {
                    return data;
                }
            }

            await _signal.WaitAsync(TimeSpan.FromMilliseconds(250));
        }

        return null;
    }

    public int CountOf(ClientNotificationType type)
    {
        var count = 0;
        foreach (var (t, _) in _received)
        {
            if (t == type) count++;
        }

        return count;
    }

    private void StartReceiving()
    {
        _receiving = Task.Run(async () =>
        {
            try
            {
                while (_socket.State == WebSocketState.Open)
                {
                    var buffer = new ArraySegment<byte>(new byte[1024 * 8]);
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _socket.ReceiveAsync(buffer, _cts.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            return;
                        }

                        ms.Write(buffer.Array!, buffer.Offset, result.Count);
                    } while (!result.EndOfMessage);

                    var notification = Decrypt<TestNotification>(ms.ToArray());
                    _received.Enqueue((notification.NotificationType, notification.Data ?? ""));
                    _signal.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // disposing
            }
            catch (WebSocketException e) when (e.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                // server went away
            }
        });
    }

    private T Decrypt<T>(byte[] bytes)
    {
        var json = bytes.ToStringFromUtf8Bytes();
        var payload = OdinSystemSerializer.Deserialize<ClientNotificationPayload>(json)
                      ?? throw new Exception($"could not read a notification envelope: {json}");
        var inner = payload.IsEncrypted
            ? SharedSecretEncryptedPayload.Decrypt(payload.Payload, _sharedSecret).ToStringFromUtf8Bytes()
            : payload.Payload;
        return OdinSystemSerializer.Deserialize<T>(inner)
               ?? throw new Exception($"could not read a {typeof(T).Name} from a notification");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _cts.CancelAsync();
            if (_receiving != null)
            {
                await _receiving;
            }
        }
        catch (Exception)
        {
            // teardown
        }
        finally
        {
            _socket.Dispose();
            _cts.Dispose();
        }
    }

    private sealed class TestNotification
    {
        public ClientNotificationType NotificationType { get; set; }
        public string? Data { get; set; }
    }
}
