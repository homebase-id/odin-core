using System;
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

    [Test]
    public async Task AClientThatNeverAnswersTheCloseIsAbortedWithinTheTimeout()
    {
        var silent = new FakeSocket(_ => new TaskCompletionSource().Task);

        // A TimeoutException is the failure: an unbounded close hangs instead of the run
        var count = await Collection(silent).RemoveAllSocketsAsync(WebSocketCloseStatus.EndpointUnavailable, "identity is Paused")
            .WaitAsync(CloseTimeout + TimeSpan.FromSeconds(2));

        Assert.That(count, Is.EqualTo(1));
        Assert.That(silent.State, Is.EqualTo(WebSocketState.Aborted));
    }

    [Test]
    public async Task AClientThatAnswersIsClosedAndNotAborted()
    {
        var answering = new FakeSocket(socket =>
        {
            socket.CurrentState = WebSocketState.Closed;
            return Task.CompletedTask;
        });
        await Collection(answering).RemoveAllSocketsAsync(WebSocketCloseStatus.EndpointUnavailable, "identity is Paused");

        Assert.That(answering.State, Is.EqualTo(WebSocketState.Closed));
    }

    //

    private static DeviceSocketCollection Collection(WebSocket socket)
    {
        var collection = new DeviceSocketCollection(NullLogger<DeviceSocketCollection>.Instance, CloseTimeout);
        collection.AddSocket(new DeviceSocket { Key = Guid.NewGuid(), Socket = socket });
        return collection;
    }

    // An open socket whose close does what the test says, and which an abort marks Aborted
    private sealed class FakeSocket(Func<FakeSocket, Task> close) : WebSocket
    {
        public WebSocketState CurrentState { get; set; } = WebSocketState.Open;

        public override WebSocketState State => CurrentState;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override string? SubProtocol => null;

        public override void Abort() => CurrentState = WebSocketState.Aborted;

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            close(this);

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
}
