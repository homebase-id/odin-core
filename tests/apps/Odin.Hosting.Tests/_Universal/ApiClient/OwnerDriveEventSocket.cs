using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authentication.Owner;

namespace Odin.Hosting.Tests._Universal.ApiClient;

/// <summary>
/// An owner notification socket that keeps each drive event's data as JSON, to read the events in the order they came
/// </summary>
public sealed class OwnerDriveEventSocket
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    private readonly TestOwnerWebSocketListener _listener = new();
    private readonly ConcurrentQueue<(ClientNotificationType Type, JsonElement Data)> _events = new();

    public static async Task<OwnerDriveEventSocket> ConnectAsync(OdinId identity, OwnerAuthTokenContext token,
        EstablishConnectionOptions options)
    {
        var socket = new OwnerDriveEventSocket();
        socket._listener.NotificationReceived += n =>
        {
            if (n.NotificationType is ClientNotificationType.FileAdded or ClientNotificationType.FileModified
                or ClientNotificationType.FileDeleted)
            {
                socket._events.Enqueue((n.NotificationType, JsonDocument.Parse(n.Data).RootElement.Clone()));
            }

            return Task.CompletedTask;
        };
        await socket._listener.ConnectAsync(identity, token, options);
        return socket;
    }

    /// <summary>
    /// The next drive event, which must be of <paramref name="expected"/> type
    /// </summary>
    public async Task<JsonElement> NextAsync(ClientNotificationType expected)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        (ClientNotificationType Type, JsonElement Data) next;
        while (!_events.TryDequeue(out next))
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"no {expected} arrived within {WaitTimeout.TotalSeconds} s");
            }

            await Task.Delay(50);
        }

        Assert.That(next.Type, Is.EqualTo(expected), next.Data.ToString());
        return next.Data;
    }

    public Task DisconnectAsync() => _listener.DisconnectAsync();
}
