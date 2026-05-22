using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Serialization;
using Odin.Hosting.Tests._Universal.ApiClient;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Apps;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests._Universal.AppNotifications;

// Passive owner WebSocket listener that captures the two reaction-related notifications
// (StatisticsChanged and FileModified) together with their full headers. Unlike
// WebSocketDrainTestSocketHandler it records EVERY occurrence (no de-duplication) so tests can
// inspect snapshot consistency (localReactions, updated, versionTag, reactionPreview) and count
// how many notifications of each type a single reaction produces.
public sealed class ReactionNotificationSocketHandler
{
    public sealed record CapturedNotification(
        ClientNotificationType Type,
        TargetDrive Drive,
        SharedSecretEncryptedFileHeader Header);

    private readonly TestOwnerWebSocketListener _socketListener = new();
    private readonly ConcurrentQueue<CapturedNotification> _events = new();

    public IReadOnlyList<CapturedNotification> Events => _events.ToArray();

    public async Task ConnectAsync(OwnerApiClientRedux client, TargetDrive targetDrive)
    {
        _socketListener.NotificationReceived += OnNotificationReceived;
        await _socketListener.ConnectAsync(
            client.Identity.OdinId,
            client.GetTokenContext(),
            new EstablishConnectionOptions { Drives = [targetDrive] });
    }

    public Task DisconnectAsync() => _socketListener.DisconnectAsync();

    private Task OnNotificationReceived(TestClientNotification notification)
    {
        if (notification.NotificationType is ClientNotificationType.StatisticsChanged
            or ClientNotificationType.FileModified)
        {
            var driveNotification = OdinSystemSerializer.Deserialize<ClientDriveNotification>(notification.Data);
            if (driveNotification?.Header != null && driveNotification.TargetDrive != null)
            {
                _events.Enqueue(new CapturedNotification(
                    notification.NotificationType,
                    driveNotification.TargetDrive,
                    driveNotification.Header));
            }
        }

        return Task.CompletedTask;
    }

    //

    public CapturedNotification[] EventsFor(ClientNotificationType type, Guid globalTransitId) =>
        Events
            .Where(e => e.Type == type && e.Header.FileMetadata?.GlobalTransitId == globalTransitId)
            .ToArray();

    public int CountByType(ClientNotificationType type, Guid globalTransitId) =>
        EventsFor(type, globalTransitId).Length;

    /// <summary>Returns the most recent notification of <paramref name="type"/> for the file, or null on timeout.</summary>
    public async Task<CapturedNotification> WaitForNotification(
        ClientNotificationType type, Guid globalTransitId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var hit = EventsFor(type, globalTransitId).LastOrDefault();
            if (hit != null)
            {
                return hit;
            }

            await Task.Delay(50);
        }

        return null;
    }

    /// <summary>Waits until at least <paramref name="count"/> notifications of <paramref name="type"/> have arrived for the file.</summary>
    public async Task<bool> WaitForCount(
        ClientNotificationType type, Guid globalTransitId, int count, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (CountByType(type, globalTransitId) >= count)
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }

    /// <summary>Waits until BOTH a StatisticsChanged and a FileModified for the file have arrived.</summary>
    public async Task<bool> WaitForBoth(Guid globalTransitId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (EventsFor(ClientNotificationType.StatisticsChanged, globalTransitId).Any()
                && EventsFor(ClientNotificationType.FileModified, globalTransitId).Any())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }
}
