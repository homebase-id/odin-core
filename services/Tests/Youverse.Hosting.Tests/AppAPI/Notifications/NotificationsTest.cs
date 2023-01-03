using System;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Authentication.ClientToken;

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
    public async Task CanConnectToWebSocket()
    {
        var identity = TestIdentities.Samwise;
        var appDrive = TargetDrive.NewTargetDrive();

        //
        // Create an app
        //
        Guid appId = Guid.NewGuid();
        var testAppContext = await _scaffold.OwnerApi.SetupTestSampleApp(appId, identity,
            canReadConnections: true,
            targetDrive: appDrive,
            driveAllowAnonymousReads: false);

        //
        // Create a device use the app
        //
        var (deviceClientAuthToken, deviceSharedSecret) = await _scaffold.OwnerApi.AddAppClient(identity.DotYouId, appId);

        // Connect the drive to websockets
        // use the client auth token
        ClientWebSocket socket = new ClientWebSocket();
        socket.Options.Cookies = new CookieContainer();

        var cookie = new Cookie(ClientTokenConstants.ClientAuthTokenCookieName, deviceClientAuthToken.ToString());
        cookie.Domain = identity.DotYouId;
        socket.Options.Cookies.Add(cookie);
        CancellationTokenSource tokenSource = new CancellationTokenSource();

        var uri = new Uri($"wss://{identity.DotYouId}/owner/apps/v1/notify/ws");
        // var uri = new Uri("wss://echo.websocket.org");
        await socket.ConnectAsync(uri, tokenSource.Token);

        void ResponseReceived(Stream inputStream)
        {
            // TODO: handle deserializing responses and matching them to the requests.
            // IMPORTANT: DON'T FORGET TO DISPOSE THE inputStream!
        }

        async Task ReceiveLoop()
        {
            var loopToken = tokenSource.Token;
            MemoryStream outputStream = null;
            WebSocketReceiveResult receiveResult = null;
            var buffer = new byte[1024];
            try
            {
                while (!loopToken.IsCancellationRequested)
                {
                    outputStream = new MemoryStream(1024);
                    do
                    {
                        receiveResult = await socket.ReceiveAsync(buffer, tokenSource.Token);
                        if (receiveResult.MessageType != WebSocketMessageType.Close)
                        {
                            outputStream.Write(buffer, 0, receiveResult.Count);
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

        await Task.Factory.StartNew(ReceiveLoop, tokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
}