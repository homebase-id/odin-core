using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Tests._Universal.ApiClient;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient.Factory;
using Odin.Hosting.UnifiedV2;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.LiveRelay;
using Odin.Services.Peer.Outgoing.Drive;
using Refit;

namespace Odin.Hosting.Tests._V2.Tests.LiveRelay;

/// <summary>
/// Shared setup + websocket helpers for the live-relay tests. Mirrors the proven app peer-send
/// pattern (PeerNotificationTests) and the V2 notification websocket pattern
/// (V2NotificationSocketControllerTests).
/// </summary>
internal static class LiveRelayTestHelpers
{
    public const string BearerProtocolPrefix = "odin.bearer.";
    public const string NegotiatedSubProtocol = "odin.notify.v1";

    /// <summary>The client-facing shape of a LiveRelay notification's Data field.</summary>
    public class LiveRelayClientData
    {
        public string SenderOdinId { get; set; }
        public Guid ChannelKey { get; set; }
        public string Blob { get; set; }
        public long ReceivedAt { get; set; }
    }

    /// <summary>
    /// Registers a drive + circle + an app (with UseTransitWrite) on the given identity, so the app
    /// can both share over peer and be reached by connected identities. Returns the circle id to
    /// grant when connecting. Both the sender and recipient must register the SAME appId.
    /// </summary>
    public static async Task<Guid> PrepareAppAccessAsync(OwnerApiClientRedux owner, Guid appId, TargetDrive targetDrive)
    {
        var circleId = Guid.NewGuid();

        await owner.DriveManager.CreateDrive(targetDrive, "Live Relay Drive", "", false);
        await owner.Network.CreateCircle(circleId, "Live Relay Participants", new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(PermissionKeys.ReadWhoIFollow) // just need a circle to connect into
        });

        var appPermissions = new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                }
            },
            PermissionSet = new PermissionSet(PermissionKeys.UseTransitWrite)
        };

        var circles = new List<Guid> { circleId };
        var circlePermissions = new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.Write
                    }
                }
            }
        };

        await owner.AppManager.RegisterApp(appId, appPermissions, circles, circlePermissions);
        return circleId;
    }

    /// <summary>
    /// Registers the same app on both identities, connects them, and returns an app client token
    /// for each. Undo with <see cref="DisconnectAsync"/>.
    /// </summary>
    public static async Task<(ClientAuthenticationToken frodoAppToken, byte[] frodoAppSecret,
            ClientAuthenticationToken samAppToken, byte[] samAppSecret)>
        ConnectAndSetupAppAsync(
            OwnerApiClientRedux ownerFrodo, OwnerApiClientRedux ownerSam,
            TestIdentity frodo, TestIdentity sam, Guid appId)
    {
        // Same app on both identities (a single shared appId — e.g. the chat app).
        var frodoCircleId = await PrepareAppAccessAsync(ownerFrodo, appId, TargetDrive.NewTargetDrive());
        var samCircleId = await PrepareAppAccessAsync(ownerSam, appId, TargetDrive.NewTargetDrive());

        await ownerFrodo.Connections.SendConnectionRequest(sam.OdinId, new List<GuidId> { frodoCircleId });
        await ownerSam.Connections.AcceptConnectionRequest(frodo.OdinId, new List<GuidId> { samCircleId });

        var (frodoAppToken, frodoAppSecret) = await ownerFrodo.AppManager.RegisterAppClient(appId);
        var (samAppToken, samAppSecret) = await ownerSam.AppManager.RegisterAppClient(appId);

        return (frodoAppToken, frodoAppSecret, samAppToken, samAppSecret);
    }

    public static async Task DisconnectAsync(
        OwnerApiClientRedux ownerFrodo, OwnerApiClientRedux ownerSam, TestIdentity frodo, TestIdentity sam)
    {
        await ownerFrodo.Connections.DisconnectFrom(sam.OdinId);
        await ownerSam.Connections.DisconnectFrom(frodo.OdinId);
    }

    /// <summary>Hop 1 as the app: POST /api/v2/live-relay with the app token, optionally with a push.</summary>
    public static async Task<IApiResponse> SendRelayAsync(
        TestIdentity sender, ClientAuthenticationToken appToken, byte[] appSecret,
        Guid channelKey, List<string> recipients, string blob, AppNotificationOptions push = null)
    {
        var factory = new ApiClientFactoryV2(YouAuthConstants.AppCookieName, appToken, appSecret);
        var client = factory.CreateHttpClient(sender.OdinId, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<ILiveRelayHttpClientApiV2>(client, sharedSecret);
        return await svc.Relay(new LiveRelayRequest
        {
            ChannelKey = channelKey,
            Recipients = recipients,
            Blob = blob,
            Push = push
        });
    }

    public static async Task<ClientWebSocket> ConnectAppSocketAsync(
        OdinId identity,
        ClientAuthenticationToken appToken,
        CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.AddSubProtocol(NegotiatedSubProtocol);
        socket.Options.AddSubProtocol(BearerProtocolPrefix + ToBase64Url(appToken.ToPortableBytes()));

        var uri = new Uri($"wss://{identity}:{WebScaffold.HttpsPort}{UnifiedApiRouteConstants.NotifySocket}");
        await socket.ConnectAsync(uri, cancellationToken);
        return socket;
    }

    public static async Task<EstablishConnectionResponse> DoHandshakeAsync(
        ClientWebSocket socket,
        byte[] sharedSecret,
        List<TargetDrive> subscribedDrives,
        CancellationToken cancellationToken)
    {
        var command = new SocketCommand
        {
            Command = SocketCommandType.EstablishConnectionRequest,
            Data = OdinSystemSerializer.Serialize(new EstablishConnectionOptions { Drives = subscribedDrives })
        };

        var encrypted = SharedSecretEncryptedPayload.Encrypt(
            OdinSystemSerializer.Serialize(command).ToUtf8ByteArray(),
            sharedSecret.ToSensitiveByteArray());

        var sendBuffer = OdinSystemSerializer.Serialize(encrypted).ToUtf8ByteArray();
        await socket.SendAsync(new ArraySegment<byte>(sendBuffer), WebSocketMessageType.Text, true, cancellationToken);

        var frame = await ReadFrameAsync(socket, cancellationToken);
        return DecryptClientNotification<EstablishConnectionResponse>(frame, sharedSecret);
    }

    /// <summary>
    /// Reads websocket frames until one decodes to a LiveRelay notification, or returns null on
    /// timeout/close. Skips any other notifications received in the meantime. The timeout does NOT
    /// cancel the in-flight receive (which would abort the socket and break a clean close) — it just
    /// stops waiting and leaves the pending receive to be drained by the subsequent close.
    /// </summary>
    public static async Task<LiveRelayClientData> WaitForLiveRelayAsync(
        ClientWebSocket socket,
        byte[] sharedSecret,
        TimeSpan timeout)
    {
        var deadline = Task.Delay(timeout);
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var frameTask = ReadFrameAsync(socket, CancellationToken.None);
                var completed = await Task.WhenAny(frameTask, deadline);
                if (completed != frameTask)
                {
                    // Timed out. Observe the still-pending receive's eventual exception so it
                    // doesn't surface as unobserved when the socket is later closed/disposed.
                    _ = frameTask.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
                    return null;
                }

                var frame = await frameTask;
                if (frame == null)
                {
                    return null;
                }

                var notification = DecryptClientNotification<TestClientNotification>(frame, sharedSecret);
                if (notification.NotificationType == ClientNotificationType.LiveRelay)
                {
                    return OdinSystemSerializer.Deserialize<LiveRelayClientData>(notification.Data);
                }
            }
        }
        catch (WebSocketException) { /* server closed */ }

        return null;
    }

    /// <summary>
    /// Closes a socket without throwing if it is not in a closable state (e.g. a pending receive is
    /// still outstanding from a timed-out wait).
    /// </summary>
    public static async Task CloseQuietlyAsync(ClientWebSocket socket)
    {
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
        }
        catch
        {
            // best-effort
        }
    }

    private static async Task<byte[]> ReadFrameAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new ArraySegment<byte>(new byte[1024 * 8]);
        using var ms = new MemoryStream();
        WebSocketReceiveResult receiveResult;
        do
        {
            receiveResult = await socket.ReceiveAsync(buffer, cancellationToken);
            if (receiveResult.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            ms.Write(buffer.Array!, buffer.Offset, receiveResult.Count);
        } while (!receiveResult.EndOfMessage);

        return ms.ToArray();
    }

    private static T DecryptClientNotification<T>(byte[] frameBytes, byte[] sharedSecret)
    {
        var json = frameBytes.ToStringFromUtf8Bytes();
        var payload = OdinSystemSerializer.Deserialize<ClientNotificationPayload>(json);
        if (payload.IsEncrypted)
        {
            var decrypted = SharedSecretEncryptedPayload.Decrypt(payload.Payload, sharedSecret.ToSensitiveByteArray());
            return OdinSystemSerializer.Deserialize<T>(decrypted);
        }

        return OdinSystemSerializer.Deserialize<T>(payload.Payload);
    }

    public static string ToBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
