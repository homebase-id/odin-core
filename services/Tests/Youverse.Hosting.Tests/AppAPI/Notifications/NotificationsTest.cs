using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Serialization;
using Youverse.Core.Services.AppNotifications;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Authentication.ClientToken;
using Youverse.Hosting.Controllers.ClientToken;
using Youverse.Hosting.Controllers.OwnerToken;
using Youverse.Hosting.Tests.AppAPI.ChatStructure.Api;

namespace Youverse.Hosting.Tests.AppAPI.Notifications;

[TestFixture]
public class NotificationsTest
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
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
            await _scaffold.OldOwnerApi.AddAppClient(identity.DotYouId, appId);

        // Connect the drive to websockets
        // use the client auth token
        ClientWebSocket socket = new ClientWebSocket();
        socket.Options.Cookies = new CookieContainer();

        var cookie = new Cookie(ClientTokenConstants.ClientAuthTokenCookieName, deviceClientAuthToken.ToString());
        cookie.Domain = identity.DotYouId;
        socket.Options.Cookies.Add(cookie);
        CancellationTokenSource tokenSource = new CancellationTokenSource();

        //
        // Connect to the socket
        //
        var uri = new Uri($"wss://{identity.DotYouId}{AppApiPathConstants.NotificationsV1}/ws");
        await socket.ConnectAsync(uri, tokenSource.Token);

        //
        // Send a request indicating the drives (handshake1)
        //
        var request = new EstablishConnectionRequest()
        {
            Drives = new List<TargetDrive>() { testAppContext.TargetDrive }
        };
        var buffer = new ArraySegment<byte>(DotYouSystemSerializer.Serialize(request).ToUtf8ByteArray());
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
            var response = DotYouSystemSerializer.Deserialize<EstablishConnectionResponse>(json);
            Assert.IsNotNull(response);
            Assert.IsTrue(response.NotificationType == ClientNotificationType.DeviceHandshakeSuccess);
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
            await _scaffold.OldOwnerApi.AddAppClient(identity.DotYouId, appId);

        // Connect the drive to websockets
        // use the client auth token
        ClientWebSocket socket = new ClientWebSocket();
        socket.Options.Cookies = new CookieContainer();

        var cookie = new Cookie(ClientTokenConstants.ClientAuthTokenCookieName, deviceClientAuthToken.ToString());
        cookie.Domain = identity.DotYouId;
        socket.Options.Cookies.Add(cookie);
        CancellationTokenSource tokenSource = new CancellationTokenSource();

        //
        // Connect to the socket
        //
        var uri = new Uri($"wss://{identity.DotYouId}{AppApiPathConstants.NotificationsV1}/ws");
        await socket.ConnectAsync(uri, tokenSource.Token);

        //
        // Send a request with no drives; this should fail
        //
        var request = new EstablishConnectionRequest()
        {
            Drives = new List<TargetDrive>() { testAppContext.TargetDrive }
        };

        var buffer = new ArraySegment<byte>(DotYouSystemSerializer.Serialize(request).ToUtf8ByteArray());
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
            var response = DotYouSystemSerializer.Deserialize<EstablishConnectionResponse>(json);
            Assert.IsNotNull(response);
            Assert.IsTrue(response.NotificationType == ClientNotificationType.DeviceHandshakeSuccess);
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

        void ResponseReceived(Stream inputStream)
        {
            var response = DotYouSystemSerializer.Deserialize<TranslatedClientNotification>(inputStream, tokenSource.Token)
                .GetAwaiter().GetResult();

            Assert.IsNotNull(response);
            Assert.IsTrue(response.NotificationType == ClientNotificationType.FileAdded);

            // inputStream.Dispose();
        }

        async Task ReceiveLoop()
        {
            var loopToken = tokenSource.Token;
            MemoryStream outputStream = null;
            WebSocketReceiveResult receiveResult = null;
            var receiveBuffer = new byte[1024];
            try
            {
                while (!loopToken.IsCancellationRequested)
                {
                    outputStream = new MemoryStream(1024);
                    do
                    {
                        receiveResult = await socket.ReceiveAsync(receiveBuffer, tokenSource.Token);
                        if (receiveResult.MessageType != WebSocketMessageType.Close)
                        {
                            outputStream.Write(receiveBuffer, 0, receiveResult.Count);
                        }
                    } while (!receiveResult.EndOfMessage);

                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    outputStream.Position = 0;
                    ResponseReceived(outputStream);
                }
            }
            catch (TaskCanceledException)
            {
            }
            finally
            {
                outputStream?.Dispose();
            }
        }
    }
}