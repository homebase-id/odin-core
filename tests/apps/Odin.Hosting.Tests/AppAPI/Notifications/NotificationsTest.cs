using System;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.ClientToken.App;

namespace Odin.Hosting.Tests.AppAPI.Notifications;

[TestFixture]
public class NotificationsTest
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Samwise });
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
    public async Task CanConnectToWebSocketWithHandShake()
    {
        var identity = TestIdentities.Samwise;
        var appDrive = TargetDrive.NewTargetDrive();

        //
        // Create an app
        //
        Guid appId = Guid.NewGuid();
        var testAppContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, identity,
            canReadConnections: true,
            targetDrive: appDrive,
            driveAllowAnonymousReads: false);

        //
        // Create a device use the app
        //
        var (deviceClientAuthToken, deviceSharedSecret) =
            await _scaffold.OldOwnerApi.AddAppClient(identity.OdinId, appId);

        // Connect the drive to websockets
        // use the client auth token
        ClientWebSocket socket = new ClientWebSocket();
        socket.Options.Cookies = new CookieContainer();

        var cookie = new Cookie(YouAuthConstants.AppCookieName, deviceClientAuthToken.ToString());
        cookie.Domain = identity.OdinId;
        socket.Options.Cookies.Add(cookie);
        CancellationTokenSource tokenSource = new CancellationTokenSource();

        //
        // Connect to the socket
        //
        var uri = new Uri($"wss://{identity.OdinId}:{WebScaffold.HttpsPort}{AppApiPathConstants.NotificationsV1}/ws");
        await socket.ConnectAsync(uri, tokenSource.Token);

        //
        // Send a request indicating the drives (handshake1)
        //
        var request = new SocketCommand
        {
            Command = SocketCommandType.EstablishConnectionRequest,
            Data = OdinSystemSerializer.Serialize(new EstablishConnectionOptions
            {
                Drives = [testAppContext.TargetDrive],
            })
        };

        var ssp = SharedSecretEncryptedPayload.Encrypt(
            OdinSystemSerializer.Serialize(request).ToUtf8ByteArray(),
            deviceSharedSecret.ToSensitiveByteArray());
        var sendBuffer = OdinSystemSerializer.Serialize(ssp).ToUtf8ByteArray();
        await socket.SendAsync(new ArraySegment<byte>(sendBuffer), WebSocketMessageType.Text, true, tokenSource.Token);

        //
        // Wait for the reply 
        //
        // var innerBuffer = new byte[200];
        var receiveBuffer = new ArraySegment<byte>(new byte[1024 * 3]);
        var receiveResult = await socket.ReceiveAsync(receiveBuffer, tokenSource.Token);
        if (receiveResult.MessageType == WebSocketMessageType.Text)
        {
            var array = receiveBuffer.Array;
            Array.Resize(ref array, receiveResult.Count);

            var json = array.ToStringFromUtf8Bytes();
            var n = OdinSystemSerializer.Deserialize<ClientNotificationPayload>(json);

            ClassicAssert.IsTrue(n.IsEncrypted);
            var decryptedResponse = SharedSecretEncryptedPayload.Decrypt(n.Payload, deviceSharedSecret.ToSensitiveByteArray());
            var response = OdinSystemSerializer.Deserialize<EstablishConnectionResponse>(decryptedResponse);

            ClassicAssert.IsNotNull(response);
            ClassicAssert.IsTrue(response.NotificationType == ClientNotificationType.DeviceHandshakeSuccess);
        }
        else
        {
            Assert.Fail("Did not receive a valid handshake");
        }
    }

    [Test]
    [Ignore("work in progress")]
    public async Task CanReceiveFileAddedNotification()
    {
        var identity = TestIdentities.Samwise;
        var appDrive = TargetDrive.NewTargetDrive();

        //
        // Create an app
        //
        Guid appId = Guid.NewGuid();
        var testAppContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, identity,
            canReadConnections: true,
            targetDrive: appDrive,
            driveAllowAnonymousReads: false);

        //
        // Create a device use the app
        //
        var (deviceClientAuthToken, deviceSharedSecret) =
            await _scaffold.OldOwnerApi.AddAppClient(identity.OdinId, appId);

        // Connect the drive to websockets
        // use the client auth token
        ClientWebSocket socket = new ClientWebSocket();
        socket.Options.Cookies = new CookieContainer();

        var cookie = new Cookie(YouAuthConstants.AppCookieName, deviceClientAuthToken.ToString());
        cookie.Domain = identity.OdinId;
        socket.Options.Cookies.Add(cookie);
        CancellationTokenSource tokenSource = new CancellationTokenSource();

        //
        // Connect to the socket
        //
        var uri = new Uri($"wss://{identity.OdinId}:{WebScaffold.HttpsPort}{AppApiPathConstants.NotificationsV1}/ws");
        await socket.ConnectAsync(uri, tokenSource.Token);

        //
        // Send a request with no drives; this should fail
        //
        var request = new SocketCommand
        {
            Command = SocketCommandType.EstablishConnectionRequest,
            Data = OdinSystemSerializer.Serialize(new List<TargetDrive>() { testAppContext.TargetDrive })
        };


        var buffer = new ArraySegment<byte>(OdinSystemSerializer.Serialize(request).ToUtf8ByteArray());
        await socket.SendAsync(buffer, WebSocketMessageType.Text, true, tokenSource.Token);

        //
        // Wait for the reply 
        //
        var receiveResult = await socket.ReceiveAsync(buffer, tokenSource.Token);
        if (receiveResult.MessageType == WebSocketMessageType.Text)
        {
            var array = buffer.Array;
            Array.Resize(ref array, receiveResult.Count);

            var json = array.ToStringFromUtf8Bytes();
            var response = OdinSystemSerializer.Deserialize<EstablishConnectionResponse>(json);
            ClassicAssert.IsNotNull(response);
            ClassicAssert.IsTrue(response.NotificationType == ClientNotificationType.DeviceHandshakeSuccess);
        }
        else
        {
            Assert.Fail("Did not valid acknowledgement");
        }

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);

        //
        // We are connected, start listening
        //
        // await Task.Factory.StartNew(ReceiveLoop, tokenSource.Token, TaskCreationOptions.LongRunning,
        //     TaskScheduler.Default);

        //
        // Add a file to the target drive, should be received in the receive loop method below
        //
        var (instructionSet, fileMetadata) =
            NotificationTestUtils.RandomEncryptedFileHeaderNoPayload("contents are here", testAppContext.TargetDrive);
        await _scaffold.AppApi.UploadFile(testAppContext, instructionSet, fileMetadata, false, "payload data");

        // void ResponseReceived(Stream inputStream)
        // {
        //     var response = DotYouSystemSerializer.Deserialize<TranslatedClientNotification>(inputStream, tokenSource.Token)
        //         .GetAwaiter().GetResult();
        //
        //     ClassicAssert.IsNotNull(response);
        //     ClassicAssert.IsTrue(response.NotificationType == ClientNotificationType.FileAdded);
        //
        //     // inputStream.Dispose();
        // }

        // async Task ReceiveLoop()
        // {
        //     var loopToken = tokenSource.Token;
        //     MemoryStream outputStream = null;
        //     WebSocketReceiveResult receiveResult = null;
        //     var receiveBuffer = new byte[1024];
        //     try
        //     {
        //         while (!loopToken.IsCancellationRequested)
        //         {
        //             outputStream = new MemoryStream(1024);
        //             do
        //             {
        //                 receiveResult = await socket.ReceiveAsync(receiveBuffer, tokenSource.Token);
        //                 if (receiveResult.MessageType != WebSocketMessageType.Close)
        //                 {
        //                     outputStream.Write(receiveBuffer, 0, receiveResult.Count);
        //                 }
        //             } while (!receiveResult.EndOfMessage);
        //
        //             if (receiveResult.MessageType == WebSocketMessageType.Close)
        //             {
        //                 break;
        //             }
        //
        //             outputStream.Position = 0;
        //             ResponseReceived(outputStream);
        //         }
        //     }
        //     catch (TaskCanceledException)
        //     {
        //     }
        //     finally
        //     {
        //         outputStream?.Dispose();
        //     }
        // }
    }
}