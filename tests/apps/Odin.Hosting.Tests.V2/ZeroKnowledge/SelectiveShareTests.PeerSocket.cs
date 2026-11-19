#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Hosting.Tests._Universal.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Hosting.UnifiedV2;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;
using Odin.Services.Peer.AppNotification;
using Odin.Services.Base;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// The peer socket end to end on a Private Share drive: a member's device fetches a token through its own server,
/// connects to the sharer's server with it and subscribes to the drive, and hears of a file shared with it and of no
/// other.  The Events tests register the member's socket on the server directly; this one goes through the handshake.
/// </summary>
public partial class SelectiveShareTests
{
    [Test]
    public async Task AMembersPeerSocketHearsOfWhatIsSharedWithThemAndNothingElse()
    {
        var s = await ArrangeAsync();
        var token = await s.Sam.RefitFor<IDrivePeerNotificationHttpClientApiV2>()
            .GetToken(new GetRemoteTokenRequest { Identity = s.Frodo.Identity });
        Assert.That(token.StatusCode, Is.EqualTo(HttpStatusCode.OK), token.Error?.Content);

        await using var socket = await InProcessPeerSocket.ConnectAsync(Host, s.Frodo.Identity, token.Content!.ToCat(),
            new EstablishConnectionOptions { Drives = [s.Drive] });

        // In this order: by the time the shared file's event arrives, one for the other would have come before it
        var notShared = await s.UploadAsync(SecurityGroupType.Owner);
        var shared = await s.UploadAsync(SecurityGroupType.Owner, [s.Share(Guid.Empty, DrivePermission.Read)]);

        await socket.WaitForAsync("the file shared with Sam", ClientNotificationType.FileAdded, n => n.Header?.FileId == shared);
        Assert.That(socket.DriveEvents().Select(n => n.Header?.FileId ?? n.FileRef?.FileId), Does.Not.Contain(notShared),
            "the file not shared with Sam");
    }

    /// <summary>
    /// The peer socket a device opens, over the in-process server: the handshake carries the token in-band
    /// (<see cref="SocketAuthenticationPackage"/>), and every message after it is a notification, read whole.
    /// </summary>
    private sealed class InProcessPeerSocket : IAsyncDisposable
    {
        private readonly WebSocket _socket;
        private readonly ClientAccessToken _token;
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentQueue<TestClientNotification> _received = new();
        private readonly Task _receiving;

        private InProcessPeerSocket(WebSocket socket, ClientAccessToken token)
        {
            _socket = socket;
            _token = token;
            _receiving = Task.Run(ReceiveAllAsync);
        }

        public static async Task<InProcessPeerSocket> ConnectAsync(OdinHost host, OdinId sharer, ClientAccessToken token,
            EstablishConnectionOptions options)
        {
            var socket = await host.Server.CreateWebSocketClient()
                .ConnectAsync(new Uri($"wss://{sharer}{UnifiedApiRouteConstants.PeerNotifySocket}"), CancellationToken.None);

            var request = new SocketCommand
            {
                Command = SocketCommandType.EstablishConnectionRequest,
                Data = OdinSystemSerializer.Serialize(options)
            };
            var authentication = new SocketAuthenticationPackage
            {
                ClientAuthToken64 = token.ToAuthenticationToken().ToPortableBytes64(),
                SharedSecretEncryptedOptions = SharedSecretEncryptedPayload.Encrypt(
                    OdinSystemSerializer.Serialize(request).ToUtf8ByteArray(), token.SharedSecret)
            };
            await socket.SendAsync(OdinSystemSerializer.Serialize(authentication).ToUtf8ByteArray(), WebSocketMessageType.Text,
                true, CancellationToken.None);

            var handshake = await ReceiveAsync(socket, token, CancellationToken.None);
            Assert.That(handshake?.NotificationType, Is.EqualTo(ClientNotificationType.DeviceHandshakeSuccess),
                $"the handshake: {handshake?.Data}");
            return new InProcessPeerSocket(socket, token);
        }

        /// <summary>The drive events received so far.</summary>
        public ClientDriveNotification[] DriveEvents() =>
        [
            .. _received.Where(n => n.NotificationType is ClientNotificationType.FileAdded or ClientNotificationType.FileModified
                    or ClientNotificationType.FileDeleted)
                .Select(n => OdinSystemSerializer.Deserialize<ClientDriveNotification>(n.Data)!)
        ];

        public async Task<ClientDriveNotification> WaitForAsync(string what, ClientNotificationType type,
            Func<ClientDriveNotification, bool> match)
        {
            var until = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < until)
            {
                var found = _received
                    .Where(n => n.NotificationType == type)
                    .Select(n => OdinSystemSerializer.Deserialize<ClientDriveNotification>(n.Data)!)
                    .FirstOrDefault(match);
                if (found != null)
                {
                    return found;
                }

                await Task.Delay(50);
            }

            Assert.Fail($"{what}: no {type} within 15 s; received {string.Join(", ", _received.Select(n => n.NotificationType))}");
            return null!;
        }

        private async Task ReceiveAllAsync()
        {
            try
            {
                while (_socket.State == WebSocketState.Open && !_stop.IsCancellationRequested)
                {
                    var notification = await ReceiveAsync(_socket, _token, _stop.Token);
                    if (notification != null)
                    {
                        _received.Enqueue(notification);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // closing
            }
            catch (WebSocketException)
            {
                // the server ended it
            }
        }

        private static async Task<TestClientNotification?> ReceiveAsync(WebSocket socket, ClientAccessToken token,
            CancellationToken cancellationToken)
        {
            using var message = new MemoryStream();
            var buffer = new byte[4096];
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            var payload = OdinSystemSerializer.Deserialize<ClientNotificationPayload>(message.ToArray().ToStringFromUtf8Bytes())!;
            var json = payload.IsEncrypted
                ? SharedSecretEncryptedPayload.Decrypt(payload.Payload, token.SharedSecret).ToStringFromUtf8Bytes()
                : payload.Payload;
            return OdinSystemSerializer.Deserialize<TestClientNotification>(json);
        }

        // Closed as a client closes: a close frame, then the server's answer; an abort is an error in the server's log
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_socket.State == WebSocketState.Open)
                {
                    await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
                }

                await _receiving.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                await _stop.CancelAsync();
                _socket.Dispose();
                _stop.Dispose();
            }
        }
    }
}
