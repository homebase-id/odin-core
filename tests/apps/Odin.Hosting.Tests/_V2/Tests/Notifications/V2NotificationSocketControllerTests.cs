using System;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Hosting.Tests._Universal.ApiClient;
using Odin.Hosting.UnifiedV2;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests._V2.Tests.Notifications;

[TestFixture]
public class V2NotificationSocketControllerTests
{
    private const string BearerProtocolPrefix = "odin.bearer.";
    private const string NegotiatedSubProtocol = "odin.notify.v1";

    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
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

    [Test]
    public async Task Connect_WithoutBearerSubprotocol_Returns401()
    {
        var (_, _, _) = await SetupAppAndDeviceAsync();

        using var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.AddSubProtocol(NegotiatedSubProtocol); // no bearer entry

        await AssertConnectFailsWithAsync(socket, HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Connect_WithMalformedBearerToken_Returns401()
    {
        var (_, _, _) = await SetupAppAndDeviceAsync();

        using var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.AddSubProtocol(NegotiatedSubProtocol);
        socket.Options.AddSubProtocol(BearerProtocolPrefix + "not-a-real-token");

        await AssertConnectFailsWithAsync(socket, HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Connect_WithValidAppToken_NegotiatesProtocolAndCompletesHandshake()
    {
        var (testAppContext, deviceClientAuthToken, deviceSharedSecret) = await SetupAppAndDeviceAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var socket = await ConnectAuthenticatedAsync(deviceClientAuthToken, cts.Token);

        ClassicAssert.AreEqual(NegotiatedSubProtocol, socket.SubProtocol,
            "Server must echo back odin.notify.v1 as the negotiated subprotocol.");

        var response = await DoHandshakeAsync(socket, deviceSharedSecret, [testAppContext.TargetDrive], cts.Token);
        ClassicAssert.IsNotNull(response);
        ClassicAssert.AreEqual(ClientNotificationType.DeviceHandshakeSuccess, response.NotificationType);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
    }

    [Test]
    public async Task Connect_WithValidAppToken_ReceivesFileAddedNotification()
    {
        var (testAppContext, deviceClientAuthToken, deviceSharedSecret) = await SetupAppAndDeviceAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var socket = await ConnectAuthenticatedAsync(deviceClientAuthToken, cts.Token);

        var handshake = await DoHandshakeAsync(socket, deviceSharedSecret, [testAppContext.TargetDrive], cts.Token);
        ClassicAssert.AreEqual(ClientNotificationType.DeviceHandshakeSuccess, handshake.NotificationType);

        // Start a receive task that captures the first FileAdded notification.
        var fileAddedReceived = new TaskCompletionSource<ClientNotificationType>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var receiveTask = Task.Run(async () =>
        {
            try
            {
                while (socket.State == WebSocketState.Open && !cts.IsCancellationRequested)
                {
                    var buffer = new ArraySegment<byte>(new byte[1024 * 8]);
                    var receiveResult = await socket.ReceiveAsync(buffer, cts.Token);
                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    var array = buffer.Array!;
                    Array.Resize(ref array, receiveResult.Count);
                    var notification = DecryptClientNotification<TestClientNotification>(array, deviceSharedSecret);
                    if (notification.NotificationType == ClientNotificationType.FileAdded)
                    {
                        fileAddedReceived.TrySetResult(notification.NotificationType);
                        return;
                    }
                }
            }
            catch (OperationCanceledException) { /* expected on timeout */ }
            catch (WebSocketException) { /* server closed; ignore */ }
        }, cts.Token);

        // Trigger a file-added event on the drive the socket subscribed to.
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var uploadResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(testAppContext.TargetDrive,
            new UploadFileMetadata
            {
                AllowDistribution = false,
                IsEncrypted = false,
                AppData = new UploadAppFileMetaData
                {
                    Content = "contents are here",
                    FileType = 150
                },
                AccessControlList = AccessControlList.OwnerOnly
            });
        Assert.That(uploadResponse.IsSuccessStatusCode, Is.True, "Upload did not succeed.");

        var finished = await Task.WhenAny(fileAddedReceived.Task, Task.Delay(TimeSpan.FromSeconds(20), cts.Token));
        Assert.That(finished, Is.SameAs(fileAddedReceived.Task), "Timed out waiting for FileAdded notification.");
        ClassicAssert.AreEqual(ClientNotificationType.FileAdded, fileAddedReceived.Task.Result);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        await receiveTask;
    }

    //

    private async Task<(TestAppContext testAppContext,
            Odin.Services.Authorization.ExchangeGrants.ClientAuthenticationToken deviceClientAuthToken,
            byte[] deviceSharedSecret)>
        SetupAppAndDeviceAsync()
    {
        var identity = TestIdentities.Samwise;
        var appDrive = TargetDrive.NewTargetDrive();
        var appId = Guid.NewGuid();

        var testAppContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, identity,
            canReadConnections: true,
            targetDrive: appDrive,
            driveAllowAnonymousReads: false);

        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var (deviceClientAuthToken, deviceSharedSecret) = await ownerApiClient.AppManager.RegisterAppClient(appId);

        return (testAppContext, deviceClientAuthToken, deviceSharedSecret);
    }

    private static Uri BuildSocketUri(TestIdentity identity)
        => new($"wss://{identity.OdinId}:{WebScaffold.HttpsPort}{UnifiedApiRouteConstants.NotifySocket}");

    private async Task<ClientWebSocket> ConnectAuthenticatedAsync(
        Odin.Services.Authorization.ExchangeGrants.ClientAuthenticationToken deviceClientAuthToken,
        CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.AddSubProtocol(NegotiatedSubProtocol);
        socket.Options.AddSubProtocol(BearerProtocolPrefix + ToBase64Url(deviceClientAuthToken.ToPortableBytes()));

        await socket.ConnectAsync(BuildSocketUri(TestIdentities.Samwise), cancellationToken);
        return socket;
    }

    private static async Task<EstablishConnectionResponse> DoHandshakeAsync(
        ClientWebSocket socket,
        byte[] sharedSecret,
        List<TargetDrive> subscribedDrives,
        CancellationToken cancellationToken)
    {
        var command = new SocketCommand
        {
            Command = SocketCommandType.EstablishConnectionRequest,
            Data = OdinSystemSerializer.Serialize(new EstablishConnectionOptions
            {
                Drives = subscribedDrives,
            })
        };

        var encrypted = SharedSecretEncryptedPayload.Encrypt(
            OdinSystemSerializer.Serialize(command).ToUtf8ByteArray(),
            sharedSecret.ToSensitiveByteArray());

        var sendBuffer = OdinSystemSerializer.Serialize(encrypted).ToUtf8ByteArray();
        await socket.SendAsync(new ArraySegment<byte>(sendBuffer), WebSocketMessageType.Text, true, cancellationToken);

        var receiveBuffer = new ArraySegment<byte>(new byte[1024 * 3]);
        var receiveResult = await socket.ReceiveAsync(receiveBuffer, cancellationToken);
        if (receiveResult.MessageType != WebSocketMessageType.Text)
        {
            throw new Exception("Did not receive a text-frame handshake response.");
        }

        var array = receiveBuffer.Array!;
        Array.Resize(ref array, receiveResult.Count);
        return DecryptClientNotification<EstablishConnectionResponse>(array, sharedSecret);
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

    private static async Task AssertConnectFailsWithAsync(ClientWebSocket socket, HttpStatusCode expectedStatus)
    {
        var uri = BuildSocketUri(TestIdentities.Samwise);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var threw = false;
        try
        {
            await socket.ConnectAsync(uri, cts.Token);
        }
        catch (WebSocketException)
        {
            threw = true;
        }

        ClassicAssert.IsTrue(threw, "Expected ConnectAsync to throw on a non-101 upgrade response.");
        ClassicAssert.AreEqual(expectedStatus, socket.HttpStatusCode,
            $"Expected HTTP {(int)expectedStatus} on the failed upgrade; got {(int)socket.HttpStatusCode}.");
    }

    private static string ToBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
