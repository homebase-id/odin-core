using System;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Odin.Services.AppNotifications.WebSocket;

namespace Odin.Services.Tests.AppNotifications;

// Closing a socket waits for the client's close frame; a client that stopped reading never sends one (#1854)
public class DeviceSocketCollectionTests
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromMilliseconds(300);

    [TestCase(true)]
    [TestCase(false)]
    public async Task AClientThatNeverAnswersTheCloseIsAbortedWithinTheTimeout(bool honoursCancellation)
    {
        var silent = new SilentClientSocket(honoursCancellation);
        var collection = Collection(silent);

        // Raced against a deadline, so an unbounded close fails the test instead of hanging the run
        var sw = Stopwatch.StartNew();
        var removal = collection.RemoveAllSocketsAsync(WebSocketCloseStatus.EndpointUnavailable, "identity is Paused");
        var deadline = CloseTimeout + TimeSpan.FromSeconds(2);
        Assert.That(await Task.WhenAny(removal, Task.Delay(deadline)), Is.SameAs(removal), $"still closing after {deadline}");

        Assert.That(await removal, Is.EqualTo(1));
        Assert.That(silent.State, Is.EqualTo(WebSocketState.Aborted), $"after {sw.Elapsed}");
    }

    [Test]
    public async Task AClientThatAnswersIsClosedAndNotAborted()
    {
        var answering = new AnsweringClientSocket();
        await Collection(answering).RemoveAllSocketsAsync(WebSocketCloseStatus.EndpointUnavailable, "identity is Paused");

        Assert.That(answering.State, Is.EqualTo(WebSocketState.Closed));
        Assert.That(answering.Aborted, Is.False);
    }

    //

    private static DeviceSocketCollection Collection(WebSocket socket)
    {
        var collection = new DeviceSocketCollection(NullLogger<DeviceSocketCollection>.Instance, CloseTimeout);
        collection.AddSocket(new DeviceSocket { Key = Guid.NewGuid(), Socket = socket });
        return collection;
    }

    private abstract class FakeSocket : WebSocket
    {
        protected WebSocketState CurrentState = WebSocketState.Open;
        public bool Aborted { get; private set; }

        public override WebSocketState State => CurrentState;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override string? SubProtocol => null;

        public override void Abort()
        {
            Aborted = true;
            CurrentState = WebSocketState.Aborted;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Dispose()
        {
        }
    }

    // Sends nothing back: the close waits until cancelled, or forever if it does not honour the token
    private sealed class SilentClientSocket(bool honoursCancellation) : FakeSocket
    {
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            honoursCancellation ? Task.Delay(Timeout.Infinite, cancellationToken) : new TaskCompletionSource().Task;
    }

    private sealed class AnsweringClientSocket : FakeSocket
    {
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            CurrentState = WebSocketState.Closed;
            return Task.CompletedTask;
        }
    }
}
