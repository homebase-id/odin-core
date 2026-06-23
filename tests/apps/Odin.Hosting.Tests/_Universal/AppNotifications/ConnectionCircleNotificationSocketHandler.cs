using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Serialization;
using Odin.Hosting.Tests._Universal.ApiClient;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.WebSocket;

namespace Odin.Hosting.Tests._Universal.AppNotifications;

// Passive owner WebSocket listener that captures the connection/circle state-change notifications
// (ConnectionChanged = 5002, CircleDefinitionChanged = 5003) pushed to the owner's own sessions.
// Records every occurrence (no de-duplication) and parses the JSON-string Data payload so tests can
// assert on the change kind plus the affected identity / circle id.
public sealed class ConnectionCircleNotificationSocketHandler
{
    // Mirrors ConnectionChangedNotification.GetClientData()
    public sealed class ConnectionChangedPayload
    {
        public string Identity { get; set; }
        public string Change { get; set; }
        public Guid? CircleId { get; set; }
    }

    // Mirrors CircleDefinitionChangedNotification.GetClientData()
    public sealed class CircleDefinitionChangedPayload
    {
        public Guid CircleId { get; set; }
        public string Change { get; set; }
    }

    private readonly TestOwnerWebSocketListener _listener = new();
    private readonly ConcurrentQueue<ConnectionChangedPayload> _connectionEvents = new();
    private readonly ConcurrentQueue<CircleDefinitionChangedPayload> _circleEvents = new();

    public IReadOnlyList<ConnectionChangedPayload> ConnectionEvents => _connectionEvents.ToArray();
    public IReadOnlyList<CircleDefinitionChangedPayload> CircleEvents => _circleEvents.ToArray();

    public async Task ConnectAsync(OwnerApiClientRedux client)
    {
        _listener.NotificationReceived += OnNotificationReceived;

        // Drive subscription is irrelevant for these client notifications (they fan out to every
        // owner socket regardless of subscribed drives), so connect with an empty drive set.
        await _listener.ConnectAsync(
            client.Identity.OdinId,
            client.GetTokenContext(),
            new EstablishConnectionOptions { Drives = [] });
    }

    public Task DisconnectAsync() => _listener.DisconnectAsync();

    private Task OnNotificationReceived(TestClientNotification notification)
    {
        switch (notification.NotificationType)
        {
            case ClientNotificationType.ConnectionChanged:
                var cc = OdinSystemSerializer.Deserialize<ConnectionChangedPayload>(notification.Data);
                if (cc != null)
                {
                    _connectionEvents.Enqueue(cc);
                }

                break;

            case ClientNotificationType.CircleDefinitionChanged:
                var cd = OdinSystemSerializer.Deserialize<CircleDefinitionChangedPayload>(notification.Data);
                if (cd != null)
                {
                    _circleEvents.Enqueue(cd);
                }

                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>Waits for a ConnectionChanged of the given kind for the given identity, or null on timeout.</summary>
    public async Task<ConnectionChangedPayload> WaitForConnectionChange(
        ConnectionChangeType change, string identity, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var hit = ConnectionEvents.LastOrDefault(
                e => e.Change == change.ToString() && string.Equals(e.Identity, identity, StringComparison.OrdinalIgnoreCase));
            if (hit != null)
            {
                return hit;
            }

            await Task.Delay(50);
        }

        return null;
    }

    /// <summary>Waits for a CircleDefinitionChanged of the given kind for the given circle, or null on timeout.</summary>
    public async Task<CircleDefinitionChangedPayload> WaitForCircleDefinitionChange(
        CircleDefinitionChangeType change, Guid circleId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var hit = CircleEvents.LastOrDefault(e => e.Change == change.ToString() && e.CircleId == circleId);
            if (hit != null)
            {
                return hit;
            }

            await Task.Delay(50);
        }

        return null;
    }
}
