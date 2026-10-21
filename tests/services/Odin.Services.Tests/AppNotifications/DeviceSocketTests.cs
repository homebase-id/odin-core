using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Odin.Services.AppNotifications.WebSocket;

namespace Odin.Services.Tests.AppNotifications;

// A message that can't be delivered drops the connection, so the client reconnects and catches up instead of never
// learning it missed the message (#1966)
public class DeviceSocketTests
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan WaitLimit = SendTimeout + TimeSpan.FromSeconds(5);

    [Test]
    public async Task AMessageThatTimesOutWaitingBehindAStuckSendAbortsTheSocket()
    {
        // The first send never finishes and holds the socket's lock; the second times out waiting for it
        var stuck = new FakeSocket(_ => new TaskCompletionSource().Task);
        var deviceSocket = new DeviceSocket { Key = Guid.NewGuid(), Socket = stuck, Timeout = SendTimeout };

        await deviceSocket.FireAndForgetAsync("{}", NullLogger.Instance);
        await deviceSocket.FireAndForgetAsync("{}", NullLogger.Instance);

        await stuck.Aborted.Task.WaitAsync(WaitLimit);
        Assert.That(stuck.State, Is.EqualTo(WebSocketState.Aborted));
    }

    [Test]
    public async Task AMessageWhoseSendTimesOutAbortsTheSocket()
    {
        var slow = new FakeSocket(ct => Task.Delay(Timeout.Infinite, ct));
        var deviceSocket = new DeviceSocket { Key = Guid.NewGuid(), Socket = slow, Timeout = SendTimeout };

        await deviceSocket.FireAndForgetAsync("{}", NullLogger.Instance);

        await slow.Aborted.Task.WaitAsync(WaitLimit);
        Assert.That(slow.State, Is.EqualTo(WebSocketState.Aborted));
    }

    [Test]
    public async Task AMessageWhoseSendFailsAbortsTheSocket()
    {
        var failing = new FakeSocket(_ => Task.FromException(new WebSocketException("broken pipe")));
        var deviceSocket = new DeviceSocket { Key = Guid.NewGuid(), Socket = failing, Timeout = SendTimeout };

        await deviceSocket.FireAndForgetAsync("{}", NullLogger.Instance);

        await failing.Aborted.Task.WaitAsync(WaitLimit);
        Assert.That(failing.State, Is.EqualTo(WebSocketState.Aborted));
    }

    [Test]
    public async Task DeliveredMessagesLeaveTheSocketOpen()
    {
        var healthy = new FakeSocket(_ => Task.CompletedTask, expectedSends: 100);
        var deviceSocket = new DeviceSocket { Key = Guid.NewGuid(), Socket = healthy, Timeout = SendTimeout };

        for (var i = 0; i < 100; i++)
        {
            await deviceSocket.FireAndForgetAsync("{}", NullLogger.Instance);
        }

        await healthy.AllSent.Task.WaitAsync(WaitLimit);
        await Task.Delay(SendTimeout * 2);
        Assert.That(healthy.State, Is.EqualTo(WebSocketState.Open));
    }

    //

    // An open socket whose sends do what the test says, and which an abort marks Aborted
    private sealed class FakeSocket(Func<CancellationToken, Task> send, int expectedSends = 0) : WebSocket
    {
        private int _sent;

        public WebSocketState CurrentState { get; private set; } = WebSocketState.Open;
        public TaskCompletionSource Aborted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override WebSocketState State => CurrentState;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override string? SubProtocol => null;

        public override void Abort()
        {
            CurrentState = WebSocketState.Aborted;
            Aborted.TrySetResult();
        }

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage,
            CancellationToken cancellationToken)
        {
            await send(cancellationToken);
            if (Interlocked.Increment(ref _sent) == expectedSends)
            {
                AllSent.TrySetResult();
            }
        }

        public override void Dispose()
        {
        }
    }
}
