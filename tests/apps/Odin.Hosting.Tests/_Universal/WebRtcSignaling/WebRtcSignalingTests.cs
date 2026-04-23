using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Services.AppNotifications.WebRtcSignaling;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests._Universal.WebRtcSignaling;

[TestFixture]
public class WebRtcSignalingTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _scaffold = new WebScaffold(GetType().Name);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>
        {
            TestIdentities.Frodo,
            TestIdentities.Samwise,
            TestIdentities.Pippin,
            TestIdentities.Merry,
        });

        // Frodo and Samwise are connected for the whole fixture; Pippin and Merry are
        // intentionally left unconnected so the not-connected path can be exercised.
        await _scaffold.OldOwnerApi.CreateConnection(TestIdentities.Frodo.OdinId, TestIdentities.Samwise.OdinId);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _scaffold.RunAfterAnyTests();

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown() => _scaffold.AssertLogEvents();

    [Test]
    public async Task CallInvite_DeliveredToConnectedRecipient()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (frodoSocket, frodoSecret) = await OpenAppWebSocketAsync(TestIdentities.Frodo, cts.Token);
        var (samSocket, samSecret) = await OpenAppWebSocketAsync(TestIdentities.Samwise, cts.Token);

        var callId = Guid.NewGuid();
        await SendCommandAsync(frodoSocket, frodoSecret, SocketCommandType.CallInvite, new CallInvitePayload
        {
            CallId = callId,
            To = TestIdentities.Samwise.OdinId,
        }, cts.Token);

        var notification = await ReceiveNotificationAsync(samSocket, samSecret, cts.Token);
        Assert.That(notification.Type, Is.EqualTo(ClientNotificationType.CallInviteReceived));
        Assert.That((Guid)notification.Data["callId"], Is.EqualTo(callId));
        Assert.That((string)notification.Data["from"], Is.EqualTo(TestIdentities.Frodo.OdinId.DomainName));

        await CloseAsync(frodoSocket, samSocket);
    }

    [Test]
    public async Task CallOffer_DeliveredWithSdp()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (frodoSocket, frodoSecret) = await OpenAppWebSocketAsync(TestIdentities.Frodo, cts.Token);
        var (samSocket, samSecret) = await OpenAppWebSocketAsync(TestIdentities.Samwise, cts.Token);

        var callId = Guid.NewGuid();
        const string sdp = "v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\ns=test\r\n";
        await SendCommandAsync(frodoSocket, frodoSecret, SocketCommandType.CallOffer, new CallOfferPayload
        {
            CallId = callId,
            To = TestIdentities.Samwise.OdinId,
            Sdp = sdp,
        }, cts.Token);

        var notification = await ReceiveNotificationAsync(samSocket, samSecret, cts.Token);
        Assert.That(notification.Type, Is.EqualTo(ClientNotificationType.CallOfferReceived));
        Assert.That((string)notification.Data["sdp"], Is.EqualTo(sdp));

        await CloseAsync(frodoSocket, samSocket);
    }

    [Test]
    public async Task CallInvite_OfflineRecipient_GetsCallUnavailableOffline()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (frodoSocket, frodoSecret) = await OpenAppWebSocketAsync(TestIdentities.Frodo, cts.Token);
        // Sam intentionally has no socket open — relay should report offline.

        var callId = Guid.NewGuid();
        await SendCommandAsync(frodoSocket, frodoSecret, SocketCommandType.CallInvite, new CallInvitePayload
        {
            CallId = callId,
            To = TestIdentities.Samwise.OdinId,
        }, cts.Token);

        var notification = await ReceiveNotificationAsync(frodoSocket, frodoSecret, cts.Token);
        Assert.That(notification.Type, Is.EqualTo(ClientNotificationType.CallUnavailable));
        Assert.That((Guid)notification.Data["callId"], Is.EqualTo(callId));
        Assert.That((string)notification.Data["reason"], Is.EqualTo(CallUnavailableReason.Offline));

        await CloseAsync(frodoSocket);
    }

    [Test]
    public async Task CallInvite_NonConnectedSender_GetsCallUnavailableNotConnected()
    {
        // Pippin and Merry are never connected in this fixture's setup.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (pippinSocket, pippinSecret) = await OpenAppWebSocketAsync(TestIdentities.Pippin, cts.Token);

        var callId = Guid.NewGuid();
        await SendCommandAsync(pippinSocket, pippinSecret, SocketCommandType.CallInvite, new CallInvitePayload
        {
            CallId = callId,
            To = TestIdentities.Merry.OdinId,
        }, cts.Token);

        var notification = await ReceiveNotificationAsync(pippinSocket, pippinSecret, cts.Token);
        Assert.That(notification.Type, Is.EqualTo(ClientNotificationType.CallUnavailable));
        Assert.That((string)notification.Data["reason"], Is.EqualTo(CallUnavailableReason.NotConnected));

        await CloseAsync(pippinSocket);
    }

    [Test]
    public async Task Whoami_ReturnsClientEndpoint()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (socket, secret) = await OpenAppWebSocketAsync(TestIdentities.Frodo, cts.Token);

        await SendCommandAsync(socket, secret, SocketCommandType.Whoami, new { }, cts.Token);

        var notification = await ReceiveNotificationAsync(socket, secret, cts.Token);
        Assert.That(notification.Type, Is.EqualTo(ClientNotificationType.WhoamiResponse));
        Assert.That((string)notification.Data["publicIp"], Is.Not.Null.And.Not.Empty);
        Assert.That((int)notification.Data["publicPort"], Is.GreaterThan(0));

        await CloseAsync(socket);
    }

    // -- helpers ----------------------------------------------------------

    private async Task<(ClientWebSocket socket, SensitiveByteArray sharedSecret)> OpenAppWebSocketAsync(
        TestIdentity identity, CancellationToken cancellationToken)
    {
        var appId = Guid.NewGuid();
        var appContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, identity,
            canReadConnections: true,
            targetDrive: TargetDrive.NewTargetDrive(),
            driveAllowAnonymousReads: false);

        var ownerClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var (clientAuthToken, sharedSecret) = await ownerClient.AppManager.RegisterAppClient(appId);

        var socket = new ClientWebSocket { Options = { Cookies = new CookieContainer() } };
        socket.Options.Cookies.Add(new Cookie(YouAuthConstants.AppCookieName, clientAuthToken.ToString())
        {
            Domain = identity.OdinId,
        });

        var uri = new Uri($"wss://{identity.OdinId}:{WebScaffold.HttpsPort}{AppApiPathConstantsV1.NotificationsV1}/ws");
        await socket.ConnectAsync(uri, cancellationToken);

        var secretKey = sharedSecret.ToSensitiveByteArray();
        var handshake = new SocketCommand
        {
            Command = SocketCommandType.EstablishConnectionRequest,
            Data = OdinSystemSerializer.Serialize(new EstablishConnectionOptions
            {
                Drives = [appContext.TargetDrive],
            }),
        };
        await SendEncryptedAsync(socket, secretKey, OdinSystemSerializer.Serialize(handshake), cancellationToken);

        // Drain handshake reply (DeviceHandshakeSuccess) to keep frame ordering predictable.
        _ = await ReceiveOnePayloadAsync(socket, cancellationToken);
        return (socket, secretKey);
    }

    private static async Task SendCommandAsync<T>(ClientWebSocket socket, SensitiveByteArray secret,
        SocketCommandType type, T payload, CancellationToken cancellationToken)
    {
        var command = new SocketCommand
        {
            Command = type,
            Data = OdinSystemSerializer.Serialize(payload),
        };
        await SendEncryptedAsync(socket, secret, OdinSystemSerializer.Serialize(command), cancellationToken);
    }

    private static async Task SendEncryptedAsync(ClientWebSocket socket, SensitiveByteArray secret, string json,
        CancellationToken cancellationToken)
    {
        var encrypted = SharedSecretEncryptedPayload.Encrypt(json.ToUtf8ByteArray(), secret);
        var bytes = OdinSystemSerializer.Serialize(encrypted).ToUtf8ByteArray();
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<ReceivedNotification> ReceiveNotificationAsync(ClientWebSocket socket,
        SensitiveByteArray secret, CancellationToken cancellationToken)
    {
        var payload = await ReceiveOnePayloadAsync(socket, cancellationToken);
        var decryptedJson = payload.IsEncrypted
            ? SharedSecretEncryptedPayload.Decrypt(payload.Payload, secret).ToStringFromUtf8Bytes()
            : payload.Payload;

        var outer = JsonNode.Parse(decryptedJson)!.AsObject();
        var typeStr = (string)outer["notificationType"]!;
        var type = OdinSystemSerializer.Deserialize<ClientNotificationType>($"\"{typeStr}\"");
        var inner = (string)outer["data"]!;
        return new ReceivedNotification(type, JsonNode.Parse(inner)!.AsObject());
    }

    private static async Task<ClientNotificationPayload> ReceiveOnePayloadAsync(ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[4096];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        var json = ms.ToArray().ToStringFromUtf8Bytes();
        return OdinSystemSerializer.Deserialize<ClientNotificationPayload>(json);
    }

    private static async Task CloseAsync(params ClientWebSocket[] sockets)
    {
        foreach (var s in sockets)
        {
            try
            {
                await s.CloseAsync(WebSocketCloseStatus.NormalClosure, "test done", CancellationToken.None);
            }
            catch
            {
                // best-effort close
            }
            s.Dispose();
        }
    }

    private sealed record ReceivedNotification(ClientNotificationType Type, JsonObject Data);
}
