using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.UnifiedV2;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests._V2.Tests.Notifications;

/// <summary>
/// FLAGGED: in the OLD WebScaffold framework for the same reason as <see cref="V2NotificationSocketControllerTests"/>:
/// a real upgrade on a real Kestrel, over HTTP/1.1 and over HTTP/2 Extended CONNECT, which is what a browser may use and
/// what TestServer can't do. The rules themselves are covered by <c>V2CookieSocketTests</c> in the fast framework.
/// </summary>
[TestFixture]
public class V2NotificationCookieSocketKestrelTests
{
    private const string SubProtocol = "odin.notify.v1";

    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _scaffold = new WebScaffold(GetType().Name);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity> { TestIdentities.Samwise });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown()
    {
        _scaffold.AssertLogEvents();
    }

    private static string OwnOrigin => $"https://{TestIdentities.Samwise.OdinId}:{WebScaffold.HttpsPort}";

    [Test]
    public async Task Owner_CompletesTheHandshake([Values(1, 2)] int httpMajorVersion)
    {
        var auth = _scaffold.OldOwnerApi.GetOwnerAuthContext(TestIdentities.Samwise.OdinId);
        var cookie = $"{OwnerAuthConstants.CookieName}={auth.AuthenticationResult}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var socket = NewSocket(OwnOrigin, cookie);
        await ConnectAsync(socket, UnifiedApiRouteConstants.NotifySocketOwnerCookie, httpMajorVersion, cts.Token);

        Assert.That(socket.SubProtocol, Is.EqualTo(SubProtocol));
        await AssertHandshakeAsync(socket, auth.SharedSecret.GetKey(), cts.Token);
    }

    [Test]
    public async Task App_CompletesTheHandshake_WithItsCookie_BesideTheOwners([Values(1, 2)] int httpMajorVersion)
    {
        var (token, sharedSecret) = await RegisterAppClientAsync();
        var owner = _scaffold.OldOwnerApi.GetOwnerAuthContext(TestIdentities.Samwise.OdinId);
        var cookie = $"{OwnerAuthConstants.CookieName}={owner.AuthenticationResult}; {YouAuthConstants.AppCookieName}={token}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var socket = NewSocket(OwnOrigin, cookie);
        await ConnectAsync(socket, UnifiedApiRouteConstants.NotifySocketAppCookie, httpMajorVersion, cts.Token);

        await AssertHandshakeAsync(socket, sharedSecret, cts.Token);
    }

    [Test]
    public async Task AHostileOrigin_IsRefused([Values(1, 2)] int httpMajorVersion)
    {
        var auth = _scaffold.OldOwnerApi.GetOwnerAuthContext(TestIdentities.Samwise.OdinId);
        var cookie = $"{OwnerAuthConstants.CookieName}={auth.AuthenticationResult}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var socket = NewSocket("https://evil.example.com", cookie);

        Assert.ThrowsAsync<WebSocketException>(() =>
            ConnectAsync(socket, UnifiedApiRouteConstants.NotifySocketOwnerCookie, httpMajorVersion, cts.Token));
        Assert.That(socket.HttpStatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    //

    private async Task<(Odin.Services.Authorization.ExchangeGrants.ClientAuthenticationToken token, byte[] sharedSecret)>
        RegisterAppClientAsync()
    {
        var appId = Guid.NewGuid();
        await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, TestIdentities.Samwise, canReadConnections: true,
            targetDrive: TargetDrive.NewTargetDrive(), driveAllowAnonymousReads: false);

        return await _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise).AppManager.RegisterAppClient(appId);
    }

    private static ClientWebSocket NewSocket(string origin, string cookie)
    {
        var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.AddSubProtocol(SubProtocol);
        socket.Options.SetRequestHeader("Origin", origin);
        socket.Options.SetRequestHeader("Cookie", cookie);
        return socket;
    }

    private static async Task ConnectAsync(ClientWebSocket socket, string route, int httpMajorVersion, CancellationToken cancellationToken)
    {
        var uri = new Uri($"wss://{TestIdentities.Samwise.OdinId}:{WebScaffold.HttpsPort}{route}");

        if (httpMajorVersion == 1)
        {
            await socket.ConnectAsync(uri, cancellationToken);
            return;
        }

        socket.Options.HttpVersion = HttpVersion.Version20;
        socket.Options.HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        using var invoker = new HttpMessageInvoker(new SocketsHttpHandler());
        await socket.ConnectAsync(uri, invoker, cancellationToken);
    }

    private static async Task AssertHandshakeAsync(ClientWebSocket socket, byte[] sharedSecret, CancellationToken cancellationToken)
    {
        var command = new SocketCommand
        {
            Command = SocketCommandType.EstablishConnectionRequest,
            Data = OdinSystemSerializer.Serialize(new EstablishConnectionOptions { Drives = [] })
        };
        var encrypted = SharedSecretEncryptedPayload.Encrypt(
            OdinSystemSerializer.Serialize(command).ToUtf8ByteArray(), sharedSecret.ToSensitiveByteArray());
        await socket.SendAsync(OdinSystemSerializer.Serialize(encrypted).ToUtf8ByteArray(), WebSocketMessageType.Text, true, cancellationToken);

        var buffer = new byte[1024 * 8];
        var received = await socket.ReceiveAsync(buffer, cancellationToken);
        Assert.That(received.MessageType, Is.EqualTo(WebSocketMessageType.Text));

        var payload = OdinSystemSerializer.Deserialize<ClientNotificationPayload>(buffer[..received.Count].ToStringFromUtf8Bytes());
        var response = OdinSystemSerializer.Deserialize<EstablishConnectionResponse>(
            SharedSecretEncryptedPayload.Decrypt(payload!.Payload, sharedSecret.ToSensitiveByteArray()));
        Assert.That(response!.NotificationType.ToString(), Is.EqualTo("DeviceHandshakeSuccess"));

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cancellationToken);
    }
}
